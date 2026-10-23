using System.Net;
using FamilyDispatch.Web.Data;
using Microsoft.AspNetCore.Hosting;

namespace FamilyDispatch.Tests;

/// <summary>The app with a login limit of 3 per minute, so the limit is reached quickly.</summary>
public class LowLoginLimitFactory : AppFactory
{
    public const int Limit = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:LoginPerMinute", Limit.ToString());
    }
}

public class RateLimitTests(LowLoginLimitFactory factory) : IClassFixture<LowLoginLimitFactory>
{
    [Fact]
    public async Task LoginAttempts_OverTheLimit_Get429_EvenWithTheRightPassword()
    {
        var client = factory.NewClient();
        for (var i = 0; i < LowLoginLimitFactory.Limit; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostLogin("owner@alpha.example", "guess-" + i)).StatusCode);

        var blocked = await client.PostLogin("owner@alpha.example", SeedData.DemoPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Too many sign-in attempts", await blocked.Content.ReadAsStringAsync());
        Assert.DoesNotContain(blocked.Headers, h => h.Key == "Set-Cookie" && h.Value.Any(v => v.StartsWith("fd.auth=")));

        // Viewing the sign-in page is not counted or blocked.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
    }

    [Fact]
    public async Task SpoofedLeadingForwardedFor_DoesNotResetTheLimit()
    {
        // Behind Render a client can send its own X-Forwarded-For; the proxy appends the real IP last.
        // Only that last entry counts, so rotating the fake leading entry must not give a fresh allowance.
        var client = factory.NewClient();
        HttpResponseMessage last = null!;
        for (var i = 0; i <= LowLoginLimitFactory.Limit; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"198.51.100.{i}, 203.0.113.7");
            last = await client.PostLogin("owner@alpha.example", "guess-" + i);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
    }

    [Fact]
    public async Task DifferentClientIps_GetSeparateAllowances()
    {
        // Proves the forwarded header is really used: without it every request would share one bucket.
        async Task<HttpStatusCode> Attempt(string ip, int n)
        {
            var client = factory.NewClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
            return (await client.PostLogin("owner@alpha.example", "guess-" + n)).StatusCode;
        }

        for (var i = 0; i < LowLoginLimitFactory.Limit; i++)
            Assert.Equal(HttpStatusCode.OK, await Attempt("203.0.113.20", i));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Attempt("203.0.113.20", 99));

        Assert.Equal(HttpStatusCode.OK, await Attempt("203.0.113.21", 0));
    }
}
