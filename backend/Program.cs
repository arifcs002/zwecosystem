using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using System.Net.Sockets;

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
        Console.WriteLine("--> Database is ready & seeded.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"--> Error setting up database: {ex.Message}");
        fileLogger.LogError("STARTUP", "Database setup failed", ex);
    }
}

app.Run();
