using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Ecommerce.Api.Infrastructure;

namespace Ecommerce.Api.Tests;

/// <summary>Recording stand-in for the Meta Conversions API.</summary>
public class FakeMetaCapi : IMetaCapiClient
{
    public List<(string PixelId, string Token, string? TestCode, CapiPurchase Purchase)> Sent { get; } = new();
    public bool FailNext { get; set; }

    public Task<string?> SendPurchaseAsync(string pixelId, string accessToken, string? testEventCode, CapiPurchase purchase, CancellationToken ct = default)
    {
        lock (Sent) Sent.Add((pixelId, accessToken, testEventCode, purchase));
        if (FailNext) { FailNext = false; return Task.FromResult<string?>("Meta responded 400: bad token"); }
        return Task.FromResult<string?>(null);
    }
}
/// <summary>Recording stand-in for the Steadfast API: tests set the next outcome and inspect what was sent.</summary>
public class FakeSteadfast : ISteadfastClient
{
    public bool FailNext { get; set; }
    public string StatusToReturn { get; set; } = "in_review";
    public List<(string Key, string Secret, string Invoice, string Phone, string Address, decimal Cod)> Bookings { get; } = new();
    private int _seq;

    public Task<CourierBooking> CreateOrderAsync(string apiKey, string secretKey, string invoice, string name,
        string phone, string address, decimal codAmount, string? note, CancellationToken ct = default)
    {
        Bookings.Add((apiKey, secretKey, invoice, phone, address, codAmount));
        if (FailNext) { FailNext = false; return Task.FromResult(new CourierBooking(false, null, null, null, "Invalid API credentials.")); }
        var n = Interlocked.Increment(ref _seq);
        return Task.FromResult(new CourierBooking(true, $"CID{n}", $"TRK{n}", "in_review", null));
    }

    public Task<CourierStatusResult> GetStatusAsync(string apiKey, string secretKey, string consignmentId, CancellationToken ct = default)
        => Task.FromResult(new CourierStatusResult(true, StatusToReturn, null));
}

/// <summary>
/// Boots the real API in-process against a LOCAL TEST database and gives every test two
/// independent shops (A and B) so cross-tenant behaviour can be asserted.
///
/// SAFETY: the connection string must come from DB_CONNECTION_STRING and its database name
/// must contain "test". Anything else aborts â€” these tests insert and delete data and must
/// never run against a real (e.g. production) database.
/// </summary>
public class ApiFixture : IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public FakeSteadfast Courier { get; } = new();
    public FakeSms Sms { get; } = new();
    public FakePathao Pathao { get; } = new();
    public FakeMetaCapi Capi { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = default!;
    public string ConnectionString { get; private set; } = "";
    public Shop A { get; private set; } = default!;
    public Shop B { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set DB_CONNECTION_STRING to a LOCAL TEST database (name must contain 'test') before running these tests.");
        var db = new NpgsqlConnectionStringBuilder(ConnectionString).Database ?? "";
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to run: database '{db}' does not look like a test database.");

        Environment.SetEnvironmentVariable("JWT_SECRET",
            Environment.GetEnvironmentVariable("JWT_SECRET") ?? Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", null); // never touch a shared Redis

        var backend = FindBackendDir();
        Directory.SetCurrentDirectory(backend); // the app looks for StoredProcedures/ and Migrations/Sql here
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing"); // backend/appsettings.Testing.json: in-memory cache, no Redis
            b.UseContentRoot(backend);
            // Never call the real courier from tests — swap in a recording fake.
            b.ConfigureServices(s =>
            {
                s.RemoveAll<Ecommerce.Api.Infrastructure.ISteadfastClient>();
                s.AddSingleton<Ecommerce.Api.Infrastructure.ISteadfastClient>(Courier);
                s.RemoveAll<Ecommerce.Api.Infrastructure.IMetaCapiClient>();
                s.AddSingleton<Ecommerce.Api.Infrastructure.IMetaCapiClient>(Capi);
                s.RemoveAll<Ecommerce.Api.Infrastructure.IPathaoClient>();
                s.AddSingleton<Ecommerce.Api.Infrastructure.IPathaoClient>(Pathao);
                s.RemoveAll<Ecommerce.Api.Infrastructure.ISmsSender>();
                s.AddSingleton<Ecommerce.Api.Infrastructure.ISmsSender>(Sms);
            });
        });

        var run = Guid.NewGuid().ToString("N")[..8];
        A = await CreateShop($"ta{run}");
        B = await CreateShop($"tb{run}");
    }

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }

    private static string FindBackendDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "backend", "Ecommerce.Api.csproj"))) dir = dir.Parent;
        return dir != null ? Path.Combine(dir.FullName, "backend") : throw new DirectoryNotFoundException("backend/ folder not found.");
    }

    public HttpClient NewClient() => Factory.CreateClient();
    private static Task Ok(HttpResponseMessage r, string what) => Shop.Ok(r, what);

    public NpgsqlConnection OpenDb()
    {
        var c = new NpgsqlConnection(ConnectionString);
        c.Open();
        return c;
    }

    public async Task<T> Scalar<T>(string sql, params (string, object)[] args)
    {
        await using var c = OpenDb();
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task Exec(string sql, params (string, object)[] args)
    {
        await using var c = OpenDb();
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Registers a company through the public API, approves it, logs its admin in, adds a category + product.</summary>
    private async Task<Shop> CreateShop(string slug)
    {
        var anon = NewClient();
        var reg = await anon.PostAsJsonAsync("/api/auth/register-company", new
        {
            companyName = $"Test Shop {slug}", subdomain = slug, ownerFirstName = "Test", ownerLastName = "Owner",
            ownerEmail = $"{slug}@example.test", ownerPhone = "01700000000", password = "Test@12345",
            address = "x", division = "x", district = "x", thana = "x"
        });
        await Ok(reg, "register-company");
        var regJson = JsonDocument.Parse(await reg.Content.ReadAsStringAsync()).RootElement;
        var companyId = regJson.GetProperty("companyId").GetInt32();
        await Exec("UPDATE companies SET approval_status='Approved', is_active=true WHERE id=@id", ("id", companyId));

        var shop = new Shop(this, companyId, slug, $"{slug}@example.test", "Test@12345");
        await shop.LoginAsync();

        var cat = await shop.Admin.PostAsJsonAsync("/api/categories", new { name = "Shoes", description = "t" });
        await Ok(cat, "create category");
        var catId = JsonDocument.Parse(await cat.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        var prod = await shop.Admin.PostAsJsonAsync("/api/products/batch", new
        {
            name = $"Item {slug}", price = 1000, wholesalePrice = 600, categoryId = catId,
            sizes = new[] { new { size = "40", quantity = 500 } }
        });
        await Ok(prod, "create product");
        shop.ProductId = await Scalar<int>("SELECT id FROM products WHERE company_id=@c ORDER BY id LIMIT 1", ("c", companyId));
        return shop;
    }
}

/// <summary>One tenant. <see cref="Admin"/> is logged in as its company admin; <see cref="Public"/> is anonymous with its X-Tenant-ID.</summary>
public class Shop
{
    private readonly ApiFixture _f;
    public int CompanyId { get; }
    public string Slug { get; }
    public string Email { get; }
    private readonly string _password;
    public int ProductId { get; set; }
    public HttpClient Admin { get; private set; }
    public HttpClient Public { get; }

    public Shop(ApiFixture f, int companyId, string slug, string email, string password)
    {
        _f = f; CompanyId = companyId; Slug = slug; Email = email; _password = password;
        Admin = f.NewClient();
        Public = f.NewClient();
        Public.DefaultRequestHeaders.Add("X-Tenant-ID", companyId.ToString());
    }

    /// <summary>Logs in and returns a client holding a BRAND-NEW session (its first request hits the session-cache-miss path).</summary>
    public async Task<HttpClient> LoginAsync()
    {
        var c = _f.NewClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email = Email, password = _password, loginContext = Slug });
        await Ok(r, "login");
        var token = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("sessionToken").GetString()!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Admin = c;
        return c;
    }

    /// <summary>Places a guest order through the public checkout; returns (orderId, orderNumber).</summary>
    public async Task<(int Id, string Number)> PlaceOrder(string phone = "01711111111", string? coupon = null, int qty = 1)
    {
        var r = await Public.PostAsJsonAsync("/api/orders/public", new
        {
            items = new[] { new { productId = ProductId, quantity = qty } },
            customerName = "Buyer", customerPhone = phone, shippingAddress = "Addr", paymentMethod = "COD", couponCode = coupon
        });
        r.EnsureSuccessStatusCode();
        var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        return (j.GetProperty("orderId").GetInt32(), j.GetProperty("orderNumber").GetString()!);
    }

    public static async Task Ok(HttpResponseMessage r, string what) { if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"{what} failed: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}"); }

    public static async Task<JsonElement> Body(HttpResponseMessage r)
        => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture> { }

/// <summary>Records every SMS instead of calling a gateway.</summary>
public class FakeSms : Ecommerce.Api.Infrastructure.ISmsSender
{
    public List<(string? Url, string To, string Message)> Sent { get; } = new();
    public Task<bool> SendAsync(string? apiUrlTemplate, string toNumber, string message)
    { lock (Sent) Sent.Add((apiUrlTemplate, toNumber, message)); return Task.FromResult(true); }
    public string LastCodeFor(string phone) =>
        System.Text.RegularExpressions.Regex.Match(Sent.Last(s => s.To == phone).Message, @"^(\d{6})").Groups[1].Value;
}

/// <summary>Recording stand-in for the Pathao API.</summary>
public class FakePathao : IPathaoClient
{
    public bool FailNext { get; set; }
    public string StatusToReturn { get; set; } = "Pending";
    public List<(PathaoCreds Creds, string Invoice, string Phone, string Address, decimal Cod)> Bookings { get; } = new();
    private int _seq;

    public Task<CourierBooking> CreateOrderAsync(PathaoCreds c, string invoice, string name, string phone, string address,
        decimal codAmount, string? note, CancellationToken ct = default)
    {
        Bookings.Add((c, invoice, phone, address, codAmount));
        if (FailNext) { FailNext = false; return Task.FromResult(new CourierBooking(false, null, null, null, "Pathao login failed.")); }
        var n = Interlocked.Increment(ref _seq);
        return Task.FromResult(new CourierBooking(true, $"PCID{n}", $"PCID{n}", "Pending", null));
    }

    public Task<CourierStatusResult> GetStatusAsync(PathaoCreds c, string consignmentId, CancellationToken ct = default)
        => Task.FromResult(new CourierStatusResult(true, StatusToReturn, null));
}
