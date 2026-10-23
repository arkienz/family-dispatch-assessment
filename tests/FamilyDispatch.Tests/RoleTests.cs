using System.Net;
using FamilyDispatch.Web.Pages.Account;

namespace FamilyDispatch.Tests;

/// <summary>Sign-in and role rules, checked on the server (not by hidden buttons).</summary>
public class RoleTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Theory]
    [InlineData("/Loads")]
    [InlineData("/Dashboard")]
    [InlineData("/Loads/1")]
    public async Task Anonymous_IsRedirectedToLogin(string url)
    {
        var response = await factory.NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Theory]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css")]
    [InlineData("/css/site.css")]
    public async Task Anonymous_CanLoadStaticAssets_SoLoginPageIsStyled(string url)
    {
        var response = await factory.NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_AndUnknownEmail_ShowSameError()
    {
        var wrongPassword = await factory.NewClient().PostLogin("owner@alpha.example", "not-the-password");
        var unknownEmail = await factory.NewClient().PostLogin("nobody@alpha.example", "Demo-Pass-2026!");

        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownEmail.StatusCode);
        Assert.Contains(LoginModel.FailedMessage, await wrongPassword.Content.ReadAsStringAsync());
        Assert.Contains(LoginModel.FailedMessage, await unknownEmail.Content.ReadAsStringAsync());
        Assert.Null(wrongPassword.Headers.Location);
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryToken_Rejected()
    {
        var response = await factory.NewClient().PostAsync("/Account/Login", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Email"] = "owner@alpha.example", ["Password"] = "Demo-Pass-2026!" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("driver1@alpha.example", "/Dashboard")]
    [InlineData("driver1@alpha.example", "/Drivers")]
    [InlineData("driver1@alpha.example", "/Trucks")]
    [InlineData("driver1@beta.example", "/Dashboard")]
    public async Task Driver_OfficePages_Return403(string email, string url)
    {
        var client = await factory.LoginAs(email);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("owner@alpha.example")]
    [InlineData("dispatch@alpha.example")]
    public async Task OfficeStaff_CanOpenDashboard(string email)
    {
        var client = await factory.LoginAs(email);

        var response = await client.GetAsync("/Dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ErrorPages_ShowNoExceptionDetails()
    {
        var client = await factory.LoginAs("driver1@alpha.example");

        var html = await (await client.GetAsync("/Dashboard")).Content.ReadAsStringAsync();

        Assert.Contains("Your role can&#x27;t do this", html);
        Assert.DoesNotContain("Exception", html);
        Assert.DoesNotContain(".cs:line", html);
    }

    [Fact]
    public async Task AuthCookie_IsHttpOnlySecureSameSite()
    {
        var response = await factory.NewClient().PostLogin("owner@alpha.example", "Demo-Pass-2026!");

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("fd.auth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
