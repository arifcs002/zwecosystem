using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using System.Net.Sockets;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// APK uploads (Mobile App Releases) run well past Kestrel's 30MB default —
// raise the body size limit so a release upload doesn't get rejected mid-stream.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 300_000_000; // 300 MB
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 300_000_000;
});

// Add services to the container.
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<IFileTextLogger, FileTextLogger>();
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// Distributed cache: Redis when REDIS_CONNECTION_STRING is set, otherwise falls back to
// an in-memory distributed cache so local dev / single-instance deploys work without Redis.
var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
if (!string.IsNullOrEmpty(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "zwecosystem:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// Configure Swagger with JWT support
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Multi-Tenant E-Commerce, Inventory, and POS SaaS API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT access token"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ================================================================
// SECURITY: Load sensitive config from Environment Variables first.
// Server uses ENV VARs set via .env file (loaded by Docker Compose).
// Local dev uses appsettings.json (git-ignored).
// ================================================================

// DB Connection: ENV VAR takes priority over appsettings.json
var connectionString =
    Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "Database connection string is missing. Set DB_CONNECTION_STRING env var or configure appsettings.json.");
}

// JWT Secret: ENV VAR takes priority
var jwtSecret =
    Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT_SECRET env var or Jwt:Secret config is required.");

Console.WriteLine($"--> DB Host: {connectionString.Split(';').FirstOrDefault(s => s.StartsWith("Host"))?.Split('=').LastOrDefault() ?? "unknown"}");
Console.WriteLine($"--> JWT Secret configured: {(jwtSecret.Length >= 16 ? "YES (" + jwtSecret.Length + " chars)" : "TOO SHORT - minimum 32 chars required")}");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString)
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
Console.WriteLine("--> Using PostgreSQL Database.");

// Configure CORS for Angular Dashboard frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure JWT Authentication (jwtSecret already resolved from ENV VAR above)
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

var fileLogger = app.Services.GetRequiredService<IFileTextLogger>();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        fileLogger.LogError(
            "UNHANDLED",
            $"{context.Request.Method} {context.Request.Path} failed with status {context.Response.StatusCode}",
            ex);
        throw;
    }
});

// Configure the HTTP request pipeline.
app.UseCors("AllowAll"); // MUST be first so CORS headers appear even on errors

app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "POS SaaS API v1");
        c.RoutePrefix = "swagger"; // Swagger dashboard is at /swagger
    });
}

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Uploaded files are named with a content hash, so the same URL always
        // serves the same bytes — safe to cache for a year on the client.
        if (ctx.File.Name.Length > 0)
        {
            ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=31536000,immutable";
        }
    }
});

app.UseAuthentication();

// Session validation: every protected request must have a valid DB session
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var isPublic = path.Contains("/auth/login") || path.Contains("/auth/register") ||
                   path.Contains("/auth/forgot-password") || path.Contains("/auth/reset-password-otp") ||
                   path.Contains("/swagger") || path.Contains("/health");

    if (!isPublic)
    {
        var authHeader = context.Request.Headers["Authorization"].ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader.Substring(7).Trim();
            var memCache = context.RequestServices.GetRequiredService<IMemoryCache>();
            var cacheKey = $"session:{token}";

            // Session lookups happen on every single request; a 20s cache turns N DB
            // round-trips per page load into ~1 every 20s without meaningfully
            // delaying logout/expiry enforcement.
            if (!memCache.TryGetValue(cacheKey, out UserSession? session))
            {
                var dbContext = context.RequestServices.GetRequiredService<ApplicationDbContext>();
                session = await dbContext.UserSessions.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.SessionToken == token && s.IsActive && s.ExpiresAt > DateTime.UtcNow);
                memCache.Set(cacheKey, session, TimeSpan.FromSeconds(20));
            }

            if (session == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { message = "Session expired or invalid. Please login again." });
                return;
            }

            // Build ClaimsPrincipal from session data so [Authorize] works
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString()),
                new Claim("sub", session.UserId.ToString()),
                new Claim(ClaimTypes.Email, session.Email),
                new Claim("name", session.FullName)
            };
            if (session.CompanyId.HasValue)
                claims.Add(new Claim("company_id", session.CompanyId.Value.ToString()));
            foreach (var role in session.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim(ClaimTypes.Role, role.Trim()));

            var identity = new ClaimsIdentity(claims, "SessionToken");
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
    }

    await next();
});

app.UseAuthorization();

app.MapControllers();

// Apply DB Migrations/Creation and Seeding automatically
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        Console.WriteLine("--> Checking/Creating Database...");
        // Console.WriteLine("--> Checking/Creating Database...");
        // dbContext.Database.EnsureCreated();

        dbContext.Database.EnsureCreated();

        // Run stored procedures SQL (CREATE OR REPLACE — safe to re-run)
        try
        {
            var spSqlPath = Path.Combine(AppContext.BaseDirectory, "StoredProcedures", "procedures.sql");
            if (!File.Exists(spSqlPath))
                spSqlPath = Path.Combine(Directory.GetCurrentDirectory(), "StoredProcedures", "procedures.sql");
            if (File.Exists(spSqlPath))
            {
                var spSql = File.ReadAllText(spSqlPath);
                var conn = dbContext.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = spSql;
                cmd.CommandTimeout = 60;
                cmd.ExecuteNonQuery();
                Console.WriteLine("--> Stored procedures loaded.");
            }
            else
            {
                Console.WriteLine("--> procedures.sql not found, skipping SP load.");
            }
        }
        catch (Exception spEx)
        {
            Console.WriteLine($"--> WARNING: SP load failed (non-fatal): {spEx.Message}");
            fileLogger.LogError("STARTUP", "Stored procedure load failed", spEx);
        }

        // Ensure user_sessions table exists (EnsureCreated won't add new tables to existing DB)
        dbContext.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS user_sessions (
                id SERIAL PRIMARY KEY,
                session_token TEXT NOT NULL,
                user_id INTEGER NOT NULL REFERENCES users(id),
                company_id INTEGER,
                roles TEXT NOT NULL DEFAULT '',
                user_type TEXT,
                email TEXT NOT NULL DEFAULT '',
                full_name TEXT NOT NULL DEFAULT '',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                expires_at TIMESTAMPTZ NOT NULL,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                ip_address TEXT,
                user_agent TEXT
            )");
        dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_user_sessions_token ON user_sessions(session_token)");
        dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_user_sessions_user_id ON user_sessions(user_id)");

        // Ensure pricing_tags table + products.pricing_tag_id column exist
        // (same reason as user_sessions above — EnsureCreated only creates
        // tables/columns that exist on the very first run).
        dbContext.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS pricing_tags (
                id SERIAL PRIMARY KEY,
                company_id INTEGER NOT NULL REFERENCES companies(id),
                name TEXT NOT NULL,
                profit_percent DECIMAL NOT NULL DEFAULT 0,
                discount_percent DECIMAL,
                promo_start_date TIMESTAMPTZ,
                promo_end_date TIMESTAMPTZ,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_by INTEGER,
                created_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_by INTEGER,
                updated_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                deleted_by INTEGER,
                deleted_date TIMESTAMPTZ,
                is_deleted INTEGER NOT NULL DEFAULT 0
            )");
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN IF NOT EXISTS pricing_tag_id INTEGER REFERENCES pricing_tags(id)");

        // Mobile app (Capacitor APK) version history — global, not per-company:
        // one APK build serves every company's workspace + super admin.
        dbContext.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS app_versions (
                id SERIAL PRIMARY KEY,
                version_name TEXT NOT NULL,
                version_code INTEGER NOT NULL,
                apk_url TEXT NOT NULL,
                release_notes TEXT,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                uploaded_by_user_id INTEGER
            )");

        // Online (storefront) orders carry a delivery address + optional note
        // that POS walk-in orders don't — added here (same IF NOT EXISTS pattern)
        // so existing databases get the columns without a full migration.
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE orders ADD COLUMN IF NOT EXISTS shipping_address TEXT");
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE orders ADD COLUMN IF NOT EXISTS shipping_district TEXT");
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE orders ADD COLUMN IF NOT EXISTS shipping_thana TEXT");
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE orders ADD COLUMN IF NOT EXISTS order_notes TEXT");

        // Optional "was" price — when set (and higher than price) the storefront
        // shows a strikethrough original + a Save% badge. NULL = no discount shown.
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN IF NOT EXISTS compare_at_price DECIMAL");

        // Inventory movement ledger — one row per stock change (purchase, manual
        // adjust, sale, return) so the Inventory module can show history + reports.
        dbContext.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS inventory_movements (
                id SERIAL PRIMARY KEY,
                company_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                movement_type TEXT NOT NULL,   -- PURCHASE, ADJUST_IN, ADJUST_OUT, SALE, RETURN
                quantity INTEGER NOT NULL,      -- signed: +in / -out
                reason TEXT,
                unit_cost DECIMAL,
                reference TEXT,
                stock_after INTEGER,
                created_by INTEGER,
                created_date TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )");
        dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_inv_moves_company ON inventory_movements(company_id, created_date DESC)");
        dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_inv_moves_product ON inventory_movements(product_id)");

        Console.WriteLine("--> Database is ready & seeded.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"--> Error setting up database: {ex.Message}");
        fileLogger.LogError("STARTUP", "Database setup failed", ex);
    }
}

app.Run();
