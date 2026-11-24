using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch10CreditTests(ApiFixture f)
{
    private static string Phone() => "017" + Random.Shared.Next(10000000, 99999999);

    private Task<HttpResponseMessage> CreditSale(HttpClient c, int productId, int qty, string? phone, string? name = "Rahim", decimal? paid = null, string? clientRef = null) =>
        c.PostAsJsonAsync("/api/pos/checkout", new
        {
            items = new[] { new { productId, quantity = qty, price = (decimal?)null } },
            discount = 0, paymentMethod = "CREDIT", customerName = name, customerPhone = phone, amountPaid = paid, clientRef
        });

    private async Task<decimal> Balance(HttpClient c, string phone)
    {
        var r = await c.GetAsync($"/api/ledger/customers/{phone}");
        await Shop.Ok(r, "ledger");
        return (await Shop.Body(r)).GetProperty("balance").GetDecimal();
    }

    private Task SetLimit(Shop s, string? v) => v == null
        ? f.Exec("DELETE FROM company_settings WHERE company_id=@c AND key='credit_limit'", ("c", s.CompanyId))
        : f.Exec("INSERT INTO company_settings (company_id,key,value,group_name,created_date,updated_date,is_deleted) VALUES (@c,'credit_limit',@v,'POS',NOW(),NOW(),0)", ("c", s.CompanyId), ("v", v));

    private Task<int> Stock(int id) => f.Scalar<int>("SELECT stock_quantity FROM products WHERE id=@i", ("i", id));

    [Fact]
    public async Task CreditSale_NeedsAPhoneAndName_AndAddsToTheCustomersBalance()
    {
        var admin = await f.A.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await CreditSale(admin, f.A.ProductId, 1, null)).StatusCode);              // no phone
        Assert.Equal(HttpStatusCode.BadRequest, (await CreditSale(admin, f.A.ProductId, 1, "12345")).StatusCode);          // bad phone
        Assert.Equal(HttpStatusCode.BadRequest, (await CreditSale(admin, f.A.ProductId, 1, Phone(), name: "")).StatusCode); // no name
        Assert.Equal(HttpStatusCode.BadRequest, (await CreditSale(admin, f.A.ProductId, 1, Phone(), paid: -5)).StatusCode);

        var phone = Phone();
        var before = await Stock(f.A.ProductId);
        var r = await CreditSale(admin, f.A.ProductId, 2, phone);                     // 2 x 1000, nothing paid now
        await Shop.Ok(r, "credit sale");
        var receipt = (await Shop.Body(r)).GetProperty("receipt");
        Assert.Equal("CREDIT", receipt.GetProperty("paymentMethod").GetString());
        Assert.Equal(2000m, receipt.GetProperty("due").GetDecimal());
        Assert.Equal(2000m, receipt.GetProperty("balance").GetDecimal());
        Assert.Equal(2000m, await Balance(admin, phone));
        Assert.Equal(before - 2, await Stock(f.A.ProductId));                           // the goods really left the shelf
        Assert.Equal("PENDING", await f.Scalar<string>("SELECT payment_status FROM orders WHERE company_id=@c AND customer_phone=@p ORDER BY id DESC LIMIT 1", ("c", f.A.CompanyId), ("p", phone)));
    }

    [Fact]
    public async Task PartPaymentAtTheTill_ReducesWhatIsOwed_AndFullPaymentMarksTheOrderPaid()
    {
        var admin = await f.A.LoginAsync();
        var phone = Phone();
        var r = await CreditSale(admin, f.A.ProductId, 1, phone, paid: 400);
        await Shop.Ok(r, "part paid");
        Assert.Equal(600m, (await Shop.Body(r)).GetProperty("receipt").GetProperty("due").GetDecimal());
        Assert.Equal(600m, await Balance(admin, phone));

        Assert.Equal(HttpStatusCode.BadRequest, (await CreditSale(admin, f.A.ProductId, 1, Phone(), paid: 5000)).StatusCode);   // more than the total

        var full = await CreditSale(admin, f.A.ProductId, 1, Phone(), paid: 1000);
        await Shop.Ok(full, "fully paid");
        Assert.Equal("PAID", await f.Scalar<string>("SELECT payment_status FROM orders WHERE id=@i", ("i", (await Shop.Body(full)).GetProperty("orderId").GetInt32())));
    }

    [Fact]
    public async Task CreditLimit_StopsASaleThatWouldGoOverIt()
    {
        var admin = await f.A.LoginAsync();
        await SetLimit(f.A, "1500");
        try
        {
            var phone = Phone();
            await Shop.Ok(await CreditSale(admin, f.A.ProductId, 1, phone), "within limit");          // owes 1000
            var over = await CreditSale(admin, f.A.ProductId, 1, phone);                               // would be 2000 > 1500
            Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
            Assert.Contains("limit", (await Shop.Body(over)).GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1000m, await Balance(admin, phone));                                          // the refused sale left no trace

            await Shop.Ok(await admin.PostAsJsonAsync("/api/ledger/payments", new { phone, amount = 600, method = "cash" }), "pay");
            await Shop.Ok(await CreditSale(admin, f.A.ProductId, 1, phone), "after paying down");     // 400 + 1000 = 1400 <= 1500
        }
        finally { await SetLimit(f.A, null); }
    }

    [Fact]
    public async Task ParallelSales_CannotBothSlipPastTheLimit()
    {
        var admin = await f.A.LoginAsync();
        await SetLimit(f.A, "1500");
        try
        {
            var phone = Phone();
            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => CreditSale(admin, f.A.ProductId, 1, phone))));
            Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.OK));               // exactly one fits under 1500
            Assert.Equal(1000m, await Balance(admin, phone));
        }
        finally { await SetLimit(f.A, null); }
    }

    [Fact]
    public async Task RetryingACreditSale_DoesNotChargeTheCustomerTwice()
    {
        var admin = await f.A.LoginAsync();
        var phone = Phone();
        var r = "c" + Guid.NewGuid().ToString("N");
        await Shop.Ok(await CreditSale(admin, f.A.ProductId, 1, phone, paid: 100, clientRef: r), "first");
        var again = await CreditSale(admin, f.A.ProductId, 1, phone, paid: 100, clientRef: r);
        await Shop.Ok(again, "retry");
        var b = await Shop.Body(again);
        Assert.True(b.GetProperty("duplicate").GetBoolean());
        Assert.Equal(900m, b.GetProperty("receipt").GetProperty("due").GetDecimal());
        Assert.Equal(900m, await Balance(admin, phone));
    }

    [Fact]
    public async Task Payments_ReduceTheBalance_AndAreValidated()
    {
        var admin = await f.A.LoginAsync();
        var phone = Phone();
        await Shop.Ok(await CreditSale(admin, f.A.ProductId, 2, phone), "sale");

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/ledger/payments", new { phone = Phone(), amount = 100, method = "cash" })).StatusCode);  // never bought on credit
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/ledger/payments", new { phone, amount = 0, method = "cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/ledger/payments", new { phone, amount = -50, method = "cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/ledger/payments", new { phone, amount = 100, method = "paypal" })).StatusCode);

        var p = await Shop.Body(await admin.PostAsJsonAsync("/api/ledger/payments", new { phone, amount = 750, method = "bkash", note = "TXN9" }));
        Assert.Equal(2000m, p.GetProperty("previousBalance").GetDecimal());
        Assert.Equal(1250m, p.GetProperty("balance").GetDecimal());

        var detail = await Shop.Body(await admin.GetAsync($"/api/ledger/customers/{phone}"));
        var types = detail.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("entryType").GetString()).ToArray();
        Assert.Equal(new[] { "PAYMENT", "SALE" }, types);                                              // newest first
    }

    [Fact]
    public async Task Adjustments_NeedANote_AndEveryChangeStaysInTheHistory()
    {
        var admin = await f.A.LoginAsync();
        var phone = Phone();
        await Shop.Ok(await CreditSale(admin, f.A.ProductId, 1, phone), "sale");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/ledger/adjustments", new { phone, amount = -100, note = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/ledger/adjustments", new { phone, amount = 0, note = "x" })).StatusCode);
        var a = await Shop.Body(await admin.PostAsJsonAsync("/api/ledger/adjustments", new { phone, amount = -50, note = "Small amount forgiven" }));
        Assert.Equal(950m, a.GetProperty("balance").GetDecimal());
        Assert.Equal(2, await f.Scalar<int>("SELECT COUNT(*)::int FROM credit_ledger WHERE company_id=@c AND phone=@p", ("c", f.A.CompanyId), ("p", phone)));   // the sale is not edited away
    }

    [Fact]
    public async Task EachShopSeesOnlyItsOwnCustomers()
    {
        var a = await f.A.LoginAsync(); var b = await f.B.LoginAsync();
        var phone = Phone();
        await Shop.Ok(await CreditSale(a, f.A.ProductId, 1, phone), "A sale");

        Assert.Equal(0m, await Balance(b, phone));                                                      // B has no account for this number
        Assert.DoesNotContain(JsonDocument.Parse(await b.GetStringAsync("/api/ledger/customers")).RootElement.EnumerateArray(), x => x.GetProperty("phone").GetString() == phone);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync("/api/ledger/payments", new { phone, amount = 100, method = "cash" })).StatusCode);
        Assert.Equal(1000m, await Balance(a, phone));
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().GetAsync("/api/ledger/customers")).StatusCode);
    }

    [Fact]
    public async Task Summary_AddsUpWhatIsOwed()
    {
        var admin = await f.A.LoginAsync();
        var s0 = await Shop.Body(await admin.GetAsync("/api/ledger/summary"));
        var phone = Phone();
        await Shop.Ok(await CreditSale(admin, f.A.ProductId, 1, phone), "sale");
        var s1 = await Shop.Body(await admin.GetAsync("/api/ledger/summary"));
        Assert.Equal(s0.GetProperty("totalDue").GetDecimal() + 1000m, s1.GetProperty("totalDue").GetDecimal());
        Assert.Equal(s0.GetProperty("customersWithDue").GetInt32() + 1, s1.GetProperty("customersWithDue").GetInt32());
    }

    [Fact]
    public async Task CancellingACreditOrder_WritesOffTheDebt_RestocksOnce_AndCannotBeRepeated()
    {
        var admin = await f.A.LoginAsync();
        var phone = Phone();
        var before = await Stock(f.A.ProductId);
        var r = await CreditSale(admin, f.A.ProductId, 3, phone, paid: 500);                          // owes 2500
        var id = (await Shop.Body(r)).GetProperty("orderId").GetInt32();
        Assert.Equal(2500m, await Balance(admin, phone));

        await Shop.Ok(await admin.PostAsync($"/api/orders/{id}/cancel", null), "cancel");
        Assert.Equal(0m, await Balance(admin, phone));                                                 // nothing left to collect
        Assert.Equal(before, await Stock(f.A.ProductId));                                              // 3 back on the shelf

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/orders/{id}/cancel", null)).StatusCode);   // second cancel refused
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/orders/{id}/status", new { status = "CANCELLED" })).StatusCode);
        Assert.Equal(before, await Stock(f.A.ProductId));                                              // not added twice
    }

    [Fact]
    public async Task SettingAnOrderToCancelledFromTheStatusList_AlsoPutsStockBack()
    {
        var admin = await f.A.LoginAsync();
        var before = await Stock(f.A.ProductId);
        var (orderId, _) = await f.A.PlaceOrder(qty: 2);
        Assert.Equal(before - 2, await Stock(f.A.ProductId));
        Await(await admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { status = "CANCELLED" }));
        Assert.Equal(before, await Stock(f.A.ProductId));
    }

    private static void Await(HttpResponseMessage r) => Assert.Equal(HttpStatusCode.OK, r.StatusCode);
}

[Collection("api")]
public class Batch10CreditNameTests(ApiFixture f)
{
    [Fact]
    public async Task CustomerKeepsTheirNameInTheList_EvenAfterPaymentsWhichCarryNoName()
    {
        var admin = await f.A.LoginAsync();
        var phone = "017" + Random.Shared.Next(10000000, 99999999);
        await Shop.Ok(await admin.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId = f.A.ProductId, quantity = 1, price = (decimal?)null } }, discount = 0, paymentMethod = "CREDIT", customerName = "Nasima Begum", customerPhone = phone }), "sale");
        await Shop.Ok(await admin.PostAsJsonAsync("/api/ledger/payments", new { phone, amount = 100, method = "cash" }), "pay");
        var rows = JsonDocument.Parse(await admin.GetStringAsync("/api/ledger/customers?search=" + phone)).RootElement.EnumerateArray().ToList();
        Assert.Equal("Nasima Begum", Assert.Single(rows).GetProperty("name").GetString());
    }
}
