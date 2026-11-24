using System.Net;
using System.Text.Json;
using Ecommerce.Api.Infrastructure;

namespace Ecommerce.Api.Tests;

public class StartupChecksTests
{
    private const string Good = "q8Zr3xN1vB7mT5kLp0YwC2dHfJ6sGaUe9RiOoXtKbMn4VcEhPzWy1AqSdFgLjDk";

    [Fact]
    public void A_strong_secret_passes_in_production()
    {
        var (fatal, _) = StartupChecks.Evaluate("Production", Good, "Host=postgres;Database=x;Username=u;Password=S0me-long-random-pw", "redis:6379");
        Assert.Empty(fatal);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("this-is-a-placeholder-changeme-but-long-enough-1234567890")]
    [InlineData("ci-only-secret-key-at-least-32-characters-long")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Weak_or_placeholder_secrets_are_flagged_in_production(string secret)
    {
        var (fatal, _) = StartupChecks.Evaluate("Production", secret, "Host=postgres;Password=x", "r");
        Assert.NotEmpty(fatal);
    }

    [Fact]
    public void Development_and_testing_are_never_blocked_and_default_db_password_is_flagged()
    {
        Assert.Empty(StartupChecks.Evaluate("Development", "short", "Host=localhost", null).Fatal);
        Assert.Empty(StartupChecks.Evaluate("Testing", "ci-only", "Host=localhost;Password=postgres", null).Fatal);
        var (fatal, warn) = StartupChecks.Evaluate("Production", Good, "Host=localhost;Password=postgres", null);
        Assert.NotEmpty(fatal);
        Assert.True(warn.Count >= 2);                       // localhost host + no redis
    }
}

[Collection("api")]
public class HealthTests(ApiFixture f)
{
    [Fact]
    public async Task Health_is_public_cheap_and_reports_the_database()
    {
        var r = await f.NewClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ok", j.GetProperty("status").GetString());
        Assert.True(j.GetProperty("database").GetBoolean());
        // reveals nothing else: only these three fields
        Assert.Equal(3, j.EnumerateObject().Count());
    }
}
