using System.Net;
using System.Text.RegularExpressions;

namespace FamilyDispatch.Tests;

/// <summary>Browser hardening headers on every response, and no inline script the CSP would have to allow.</summary>
public class SecurityHeaderTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Theory]
    [InlineData("/Account/Login", HttpStatusCode.OK)]
    [InlineData("/no-such-page", HttpStatusCode.Redirect)]  // anonymous: sent to login
    [InlineData("/css/site.css", HttpStatusCode.OK)]
    public async Task EveryResponse_CarriesTheSecurityHeaders(string url, HttpStatusCode expected)
    {
        var response = await factory.NewClient().GetAsync(url);

        Assert.Equal(expected, response.StatusCode);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task ErrorPage_CarriesTheSecurityHeaders()
    {
        var client = await factory.LoginAs("owner@alpha.example");

        var response = await client.GetAsync($"/Loads/{factory.LoadId("BET-2001")}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Theory]
    [InlineData("/Account/Login", null)]
    [InlineData(null, "ALP-1001")]
    public async Task Pages_HaveNoInlineScripts_SoTheStrictCspDoesNotBreakThem(string? url, string? loadRef)
    {
        var client = url is null ? await factory.LoginAs("owner@alpha.example") : factory.NewClient();

        var html = await client.GetStringAsync(url ?? $"/Loads/{factory.LoadId(loadRef!)}");

        // Every <script> must load a file; an inline one would be blocked by script-src 'self'.
        Assert.DoesNotMatch(new Regex("<script(?![^>]*\\bsrc=)[^>]*>"), html);
        Assert.Matches(new Regex("src=\"/js/app\\.[a-z0-9]+\\.js\""), html); // fingerprinted by MapStaticAssets
    }
}
