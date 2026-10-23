using System.Net;

namespace FamilyDispatch.Tests;

/// <summary>Inside one company, a driver sees only the loads assigned to them.</summary>
public class DriverIsolationTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task AlphaDriver1_GetDriver2Load_Returns404()
    {
        var client = await factory.LoginAs("driver1@alpha.example");

        var response = await client.GetAsync($"/Loads/{factory.LoadId("ALP-1003")}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AlphaDriver1_GetOwnLoad_Returns200()
    {
        var client = await factory.LoginAs("driver1@alpha.example");

        var response = await client.GetAsync($"/Loads/{factory.LoadId("ALP-1001")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AlphaDriver1_LoadsList_ShowsOnlyOwnLoads()
    {
        var client = await factory.LoginAs("driver1@alpha.example");

        var html = await client.GetStringAsync("/Loads");

        Assert.Contains("ALP-1001", html);
        Assert.Contains("ALP-1002", html);
        Assert.DoesNotContain("ALP-1003", html);
        Assert.DoesNotContain("BET-", html);
    }
}
