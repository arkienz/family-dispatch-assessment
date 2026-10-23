using System.Net;
using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Pages.Account;
using Microsoft.AspNetCore.Hosting;

namespace FamilyDispatch.Tests;

/// <summary>The app with 3 failed sign-ins allowed per account, so the lockout is reached quickly.</summary>
public class LowLockoutFactory : AppFactory
{
    public const int Limit = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:LoginFailuresPerAccount", Limit.ToString());
    }
}

/// <summary>
/// Failed sign-ins are limited per account, whatever IP or proxy the attempts come from. Each attempt below uses a
/// fresh client and a different X-Forwarded-For, as an attacker spreading guesses over many addresses would.
/// </summary>
public class AccountLockoutTests(LowLockoutFactory factory) : IClassFixture<LowLockoutFactory>
{
    private async Task<HttpResponseMessage> Attempt(string email, string password, int n)
    {
        var client = factory.NewClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"198.51.100.{n}");
        return await client.PostLogin(email, password);
    }

    [Fact]
    public async Task AfterTooManyFailures_AccountIsLocked_EvenWithTheRightPassword_FromAnyIp()
    {
        for (var i = 0; i < LowLockoutFactory.Limit; i++)
            Assert.Contains(LoginModel.FailedMessage, await (await Attempt("owner@beta.example", "wrong", i)).Content.ReadAsStringAsync());

        var locked = await Attempt("owner@beta.example", SeedData.DemoPassword, 99);

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Contains("Too many failed sign-ins for this account", await locked.Content.ReadAsStringAsync());
        Assert.False(locked.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("fd.auth=")));

        // Other accounts are not affected.
        Assert.Equal(HttpStatusCode.Redirect, (await Attempt("dispatch@beta.example", SeedData.DemoPassword, 100)).StatusCode);
    }

    [Fact]
    public async Task UnknownEmail_LocksTheSameWay_SoTheLockoutRevealsNoAccounts()
    {
        for (var i = 0; i < LowLockoutFactory.Limit; i++)
            await Attempt("nobody@beta.example", "wrong", i);

        var locked = await Attempt("nobody@beta.example", "wrong", 99);

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Contains("Too many failed sign-ins for this account", await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SuccessfulSignIn_ResetsTheCount()
    {
        for (var i = 0; i < LowLockoutFactory.Limit - 1; i++)
            await Attempt("driver1@beta.example", "wrong", i);
        Assert.Equal(HttpStatusCode.Redirect, (await Attempt("driver1@beta.example", SeedData.DemoPassword, 50)).StatusCode);

        for (var i = 0; i < LowLockoutFactory.Limit - 1; i++)
            await Attempt("driver1@beta.example", "wrong", 60 + i);

        Assert.Equal(HttpStatusCode.Redirect, (await Attempt("driver1@beta.example", SeedData.DemoPassword, 70)).StatusCode);
    }
}
