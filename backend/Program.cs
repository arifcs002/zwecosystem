using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using System.Net.Sockets;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<IFileTextLogger, FileTextLogger>();

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
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "POS SaaS API v1");
    c.RoutePrefix = "swagger"; // Swagger dashboard is at /swagger
});

app.UseCors("AllowAll");

app.UseStaticFiles();

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
            var dbContext = context.RequestServices.GetRequiredService<ApplicationDbContext>();
            var session = await dbContext.UserSessions
                .FirstOrDefaultAsync(s => s.SessionToken == token && s.IsActive && s.ExpiresAt > DateTime.UtcNow);

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
        var spSqlPath = Path.Combine(AppContext.BaseDirectory, "StoredProcedures", "procedures.sql");
        if (!File.Exists(spSqlPath))
            spSqlPath = Path.Combine(Directory.GetCurrentDirectory(), "StoredProcedures", "procedures.sql");
        if (File.Exists(spSqlPath))
        {
            var spSql = File.ReadAllText(spSqlPath);
            dbContext.Database.ExecuteSqlRaw(spSql);
            Console.WriteLine("--> Stored procedures loaded.");
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

        Console.WriteLine("--> Database is ready & seeded.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"--> Error setting up database: {ex.Message}");
        fileLogger.LogError("STARTUP", "Database setup failed", ex);
    }
}

app.Run();
