using System.Net;
using FamilyDispatch.Web.Domain;

namespace FamilyDispatch.Tests;

/// <summary>Load status moves over HTTP: who may move a load, and which moves the server accepts.</summary>
public class StatusTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task AlphaDispatcher_PostStatusToBetaLoad_Returns404_BetaUnchanged()
    {
        var client = await factory.LoginAs("dispatch@alpha.example");
        var beta = factory.Load("BET-2001");

        var response = await client.PostStatus(beta.Id, nameof(LoadStatus.PickedUp));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var after = factory.Load("BET-2001");
        Assert.Equal(LoadStatus.Assigned, after.Status);
        Assert.Null(after.PickedUpAtUtc);
    }

    [Fact]
    public async Task AlphaDriver1_PostStatusToDriver2Load_Returns404_Unchanged()
    {
        var client = await factory.LoginAs("driver1@alpha.example");
        var before = factory.Load("ALP-1003");

        var response = await client.PostStatus(before.Id, nameof(LoadStatus.PickedUp));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before.Status, factory.Load("ALP-1003").Status);
    }

    [Fact]
    public async Task Status_AssignedToDelivered_Rejected()
    {
        var client = await factory.LoginAs("owner@alpha.example");
        var load = factory.Load("ALP-1001");
        Assert.Equal(LoadStatus.Assigned, load.Status);

        var response = await client.PostStatus(load.Id, nameof(LoadStatus.Delivered));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("That request wasn&#x27;t accepted", await response.Content.ReadAsStringAsync());
        Assert.Equal(LoadStatus.Assigned, factory.Load("ALP-1001").Status);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("7")]
    [InlineData("")]
    public async Task Status_GarbageTarget_Rejected(string target)
    {
        var client = await factory.LoginAs("owner@alpha.example");

        var response = await client.PostStatus(factory.LoadId("ALP-1001"), target);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LoadStatus.Assigned, factory.Load("ALP-1001").Status);
    }

    [Fact]
    public async Task Status_DeliveredToPickedUp_Rejected()
    {
        var client = await factory.LoginAs("driver1@beta.example");
        var id = factory.LoadId("BET-2002");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostStatus(id, nameof(LoadStatus.Delivered))).StatusCode);

        var response = await client.PostStatus(id, nameof(LoadStatus.PickedUp));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LoadStatus.Delivered, factory.Load("BET-2002").Status);
    }

    [Fact]
    public async Task Driver_AdvancesOwnLoad_ThroughFullWorkflow_StampsTimes()
    {
        var client = await factory.LoginAs("driver2@alpha.example");
        var id = factory.LoadId("ALP-1003");
        var start = DateTime.UtcNow.AddSeconds(-1);

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostStatus(id, nameof(LoadStatus.PickedUp))).StatusCode);
        var pickedUp = factory.Load("ALP-1003");
        Assert.Equal(LoadStatus.PickedUp, pickedUp.Status);
        Assert.True(pickedUp.PickedUpAtUtc >= start);
        Assert.Null(pickedUp.DeliveredAtUtc);

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostStatus(id, nameof(LoadStatus.Delivered))).StatusCode);
        var delivered = factory.Load("ALP-1003");
        Assert.Equal(LoadStatus.Delivered, delivered.Status);
        Assert.True(delivered.DeliveredAtUtc >= delivered.PickedUpAtUtc);

        var page = await client.GetStringAsync($"/Loads/{id}");
        Assert.Contains("No further steps.", page);
        Assert.DoesNotContain("Mark as", page);
    }

    [Fact]
    public async Task StaleTab_SecondClickOnTheSameStep_Returns409_NotADoubleMove()
    {
        // Driver and dispatcher both have the load open as Assigned; the driver clicks first.
        var id = factory.AddLoad("Alpha Transport", "ALP-3001", "driver1@alpha.example");
        var driver = await factory.LoginAs("driver1@alpha.example");
        var dispatcher = await factory.LoginAs("dispatch@alpha.example");
        Assert.Equal(HttpStatusCode.Redirect,
            (await driver.PostStatus(id, nameof(LoadStatus.PickedUp), from: nameof(LoadStatus.Assigned))).StatusCode);
        var pickedUpAt = factory.Load("ALP-3001").PickedUpAtUtc;

        var late = await dispatcher.PostStatus(id, nameof(LoadStatus.PickedUp), from: nameof(LoadStatus.Assigned));

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Contains("This load changed first", await late.Content.ReadAsStringAsync());
        Assert.Equal(pickedUpAt, factory.Load("ALP-3001").PickedUpAtUtc);
    }

    [Fact]
    public async Task DetailsPage_StatusForm_SendsTheStatusItShowed()
    {
        var client = await factory.LoginAs("owner@beta.example");

        var html = await client.GetStringAsync($"/Loads/{factory.LoadId("BET-2001")}");

        Assert.Contains("name=\"from\" value=\"Assigned\"", html);
    }

    [Fact]
    public async Task Dispatcher_MovesSameCompanyLoad()
    {
        var client = await factory.LoginAs("dispatch@alpha.example");
        var id = factory.LoadId("ALP-1002");

        var response = await client.PostStatus(id, nameof(LoadStatus.Delivered));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(LoadStatus.Delivered, factory.Load("ALP-1002").Status);
    }

    [Fact]
    public async Task Status_WithoutAntiforgeryToken_Rejected()
    {
        var client = await factory.LoginAs("owner@beta.example");
        var id = factory.LoadId("BET-2001");

        var response = await client.PostAsync($"/Loads/{id}?handler=Status",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["target"] = nameof(LoadStatus.PickedUp) }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LoadStatus.Assigned, factory.Load("BET-2001").Status);
    }
}
