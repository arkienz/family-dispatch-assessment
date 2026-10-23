using System.Net;

namespace FamilyDispatch.Tests;

/// <summary>A user of one company can never see another company's data, whatever URL they request.</summary>
public class TenantIsolationTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Theory]
    [InlineData("owner@alpha.example", "BET-2001")]
    [InlineData("dispatch@alpha.example", "BET-2002")]
    [InlineData("driver1@alpha.example", "BET-2001")]
    [InlineData("owner@beta.example", "ALP-1001")]
    public async Task GetOtherCompanysLoadById_Returns404(string email, string otherCompanyLoad)
    {
        var client = await factory.LoginAs(email);

        var response = await client.GetAsync($"/Loads/{factory.LoadId(otherCompanyLoad)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(otherCompanyLoad, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AlphaOwner_OtherCompanysLoad_LooksExactlyLikeAMissingId()
    {
        var client = await factory.LoginAs("owner@alpha.example");

        var betaLoad = await client.GetAsync($"/Loads/{factory.LoadId("BET-2001")}");
        var missing = await client.GetAsync("/Loads/99999");

        Assert.Equal(missing.StatusCode, betaLoad.StatusCode);
        Assert.Equal(WithoutTokens(await missing.Content.ReadAsStringAsync()),
                     WithoutTokens(await betaLoad.Content.ReadAsStringAsync()));
    }

    // The sign-out form's antiforgery token differs on every response; everything else must match.
    private static string WithoutTokens(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "__RequestVerificationToken\" type=\"hidden\" value=\"[^\"]+\"", "");

    [Theory]
    [InlineData("owner@alpha.example", "BET-", "B-201", "Leo Park", "Beta Logistics")]
    [InlineData("dispatch@beta.example", "ALP-", "A-101", "Dan Rivera", "Alpha Transport")]
    public async Task OfficePages_ContainNoOtherCompanyData(string email, params string[] otherCompanyMarkers)
    {
        var client = await factory.LoginAs(email);

        foreach (var page in new[] { "/Loads", "/Dashboard", "/Drivers", "/Trucks" })
        {
            var response = await client.GetAsync(page);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            foreach (var marker in otherCompanyMarkers)
                Assert.False(html.Contains(marker), $"{page} as {email} leaked '{marker}'");
        }
    }

    [Fact]
    public async Task AlphaOwner_SeesOwnCompanysLoads()
    {
        var client = await factory.LoginAs("owner@alpha.example");

        var html = await client.GetStringAsync("/Loads");

        Assert.Contains("ALP-1001", html);
        Assert.Contains("ALP-1002", html);
        Assert.Contains("ALP-1003", html);
    }
}
