using System.Net;
using FamilyDispatch.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Tests;

/// <summary>Create load, change driver/truck, add truck: role rules and ids from forms that point at another company.</summary>
public class ManagementTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private Dictionary<string, string> NewLoadForm(string reference, string driverEmail, string? truckUnit = null) => new()
    {
        ["Input.ReferenceNo"] = reference,
        ["Input.Origin"] = "Tulsa, OK",
        ["Input.Destination"] = "Wichita, KS",
        ["Input.PickupDateUtc"] = "2026-10-25T09:30",
        ["Input.DriverId"] = factory.UserId(driverEmail).ToString(),
        ["Input.TruckId"] = truckUnit is null ? "" : factory.TruckId(truckUnit).ToString(),
    };

    private bool LoadExists(string reference) =>
        factory.Db(db => db.Loads.IgnoreQueryFilters().Any(l => l.ReferenceNo == reference));

    private Task<HttpResponseMessage> PostCreateLoad(HttpClient client, Dictionary<string, string> form) =>
        client.PostForm("/Loads", "/Loads/Create", form);

    private Task<HttpResponseMessage> PostAssign(HttpClient client, int loadId, string driverEmail, string? truckUnit = null) =>
        client.PostForm("/Loads", $"/Loads/{loadId}/Assign", new Dictionary<string, string>
        {
            ["DriverId"] = factory.UserId(driverEmail).ToString(),
            ["TruckId"] = truckUnit is null ? "" : factory.TruckId(truckUnit).ToString(),
        });

    // --- Create load ---

    [Fact]
    public async Task CreateLoad_Valid_StoredUnderOwnCompany_VisibleToAssignedDriver()
    {
        var client = await factory.LoginAs("dispatch@alpha.example");

        var response = await PostCreateLoad(client, NewLoadForm("ALP-2100", "driver2@alpha.example", "A-102"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var load = factory.Load("ALP-2100");
        Assert.Equal(factory.CompanyId("Alpha Transport"), load.CompanyId);
        Assert.Equal(LoadStatus.Assigned, load.Status);
        Assert.Equal(new DateTime(2026, 10, 25, 9, 30, 0), load.PickupDateUtc);
        Assert.Contains("ALP-2100", await (await factory.LoginAs("driver2@alpha.example")).GetStringAsync("/Loads"));
        Assert.DoesNotContain("ALP-2100", await (await factory.LoginAs("driver1@alpha.example")).GetStringAsync("/Loads"));
    }

    [Fact]
    public async Task CreateLoad_WithSpoofedCompanyIdField_StoredUnderOwnCompany()
    {
        var client = await factory.LoginAs("owner@alpha.example");
        var form = NewLoadForm("ALP-2101", "driver1@alpha.example");
        var beta = factory.CompanyId("Beta Logistics").ToString();
        form["CompanyId"] = beta;
        form["Input.CompanyId"] = beta;

        var response = await PostCreateLoad(client, form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(factory.CompanyId("Alpha Transport"), factory.Load("ALP-2101").CompanyId);
    }

    [Theory]
    [InlineData("ALP-2901", "driver1@beta.example", null)]        // Beta driver on an Alpha load
    [InlineData("ALP-2902", "driver1@alpha.example", "B-201")]   // Beta truck on an Alpha load
    [InlineData("ALP-2903", "dispatch@alpha.example", null)]      // same company, but not a driver
    public async Task CreateLoad_WithDriverOrTruckOutsideCompany_Rejected_NothingCreated(string reference, string driverEmail, string? truckUnit)
    {
        var client = await factory.LoginAs("dispatch@alpha.example");

        var response = await PostCreateLoad(client, NewLoadForm(reference, driverEmail, truckUnit));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form shown again with the error
        Assert.Contains("from your company", await response.Content.ReadAsStringAsync());
        Assert.False(LoadExists(reference));
    }

    [Fact]
    public async Task CreateLoad_EmptyForm_ShowsValidation_NotA500()
    {
        var client = await factory.LoginAs("dispatch@alpha.example");
        var empty = new Dictionary<string, string>
        {
            ["Input.ReferenceNo"] = "", ["Input.Origin"] = "", ["Input.Destination"] = "",
            ["Input.PickupDateUtc"] = "", ["Input.DriverId"] = "", ["Input.TruckId"] = "",
        };

        var response = await PostCreateLoad(client, empty);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("The Reference field is required.", html);
        Assert.Contains("Choose a driver from your company.", html);
    }

    [Fact]
    public async Task CreateLoad_DuplicateReference_Rejected()
    {
        var client = await factory.LoginAs("owner@alpha.example");

        var response = await PostCreateLoad(client, NewLoadForm("ALP-1001", "driver1@alpha.example"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already used in your company", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, factory.Db(db => db.Loads.IgnoreQueryFilters().Count(l => l.ReferenceNo == "ALP-1001")));
    }

    [Fact]
    public async Task CreateLoad_SameReferenceAsOtherCompany_Allowed_AndRevealsNothing()
    {
        // Uniqueness is per company: Beta may reuse an Alpha reference, so the check can't be used to probe Alpha's data.
        var client = await factory.LoginAs("owner@beta.example");

        var response = await PostCreateLoad(client, NewLoadForm("ALP-1003", "driver1@beta.example"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(2, factory.Db(db => db.Loads.IgnoreQueryFilters().Count(l => l.ReferenceNo == "ALP-1003")));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Driver_CreateLoad_Returns403(string method)
    {
        var client = await factory.LoginAs("driver1@alpha.example");

        var response = method == "GET"
            ? await client.GetAsync("/Loads/Create")
            : await PostCreateLoad(client, NewLoadForm("ALP-2199", "driver1@alpha.example"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(LoadExists("ALP-2199"));
    }

    // --- Change driver/truck ---

    [Fact]
    public async Task AlphaDispatcher_AssignBetaDriverOrTruckToAlphaLoad_Rejected_LoadUnchanged()
    {
        var client = await factory.LoginAs("dispatch@alpha.example");
        var before = factory.Load("ALP-1001");

        var betaDriver = await PostAssign(client, before.Id, "driver1@beta.example");
        var betaTruck = await PostAssign(client, before.Id, "driver1@alpha.example", "B-201");

        Assert.Equal(HttpStatusCode.OK, betaDriver.StatusCode);
        Assert.Equal(HttpStatusCode.OK, betaTruck.StatusCode);
        var after = factory.Load("ALP-1001");
        Assert.Equal(before.DriverId, after.DriverId);
        Assert.Equal(before.TruckId, after.TruckId);
    }

    [Fact]
    public async Task AlphaDispatcher_AssignOnBetaLoad_Returns404_Unchanged()
    {
        var client = await factory.LoginAs("dispatch@alpha.example");
        var before = factory.Load("BET-2001");

        var get = await client.GetAsync($"/Loads/{before.Id}/Assign");
        var post = await PostAssign(client, before.Id, "driver1@alpha.example", "A-101");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(before.DriverId, factory.Load("BET-2001").DriverId);
    }

    [Fact]
    public async Task Assign_Valid_MovesLoadToTheNewDriver()
    {
        var client = await factory.LoginAs("owner@alpha.example");
        await PostCreateLoad(client, NewLoadForm("ALP-2150", "driver1@alpha.example"));
        var id = factory.LoadId("ALP-2150");

        var response = await PostAssign(client, id, "driver2@alpha.example", "A-102");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var load = factory.Load("ALP-2150");
        Assert.Equal(factory.UserId("driver2@alpha.example"), load.DriverId);
        Assert.Equal(factory.TruckId("A-102"), load.TruckId);
        var driver1 = await factory.LoginAs("driver1@alpha.example");
        Assert.Equal(HttpStatusCode.NotFound, (await driver1.GetAsync($"/Loads/{id}")).StatusCode);
    }

    [Fact]
    public async Task Assign_AfterPickedUp_Rejected_LoadUnchanged()
    {
        var client = await factory.LoginAs("dispatch@alpha.example");
        var before = factory.Load("ALP-1002");
        Assert.Equal(LoadStatus.PickedUp, before.Status);

        var response = await PostAssign(client, before.Id, "driver2@alpha.example");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("can only change while Assigned", await client.GetStringAsync($"/Loads/{before.Id}"));
        Assert.Equal(before.DriverId, factory.Load("ALP-1002").DriverId);
    }

    [Fact]
    public async Task Driver_Assign_Returns403_Unchanged()
    {
        var client = await factory.LoginAs("driver1@alpha.example");
        var before = factory.Load("ALP-1001");

        var response = await PostAssign(client, before.Id, "driver1@alpha.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before.DriverId, factory.Load("ALP-1001").DriverId);
    }

    // --- Add truck (Owner only) ---

    private Task<HttpResponseMessage> PostCreateTruck(HttpClient client, string unit) =>
        client.PostForm("/Trucks", "/Trucks/Create",
            new Dictionary<string, string> { ["UnitNumber"] = unit, ["Plate"] = "TX 7KD-219" });

    private bool TruckExists(string unit) =>
        factory.Db(db => db.Trucks.IgnoreQueryFilters().Any(t => t.UnitNumber == unit));

    [Fact]
    public async Task Owner_PostCreateTruck_Succeeds_UnderOwnCompany()
    {
        var client = await factory.LoginAs("owner@beta.example");

        var response = await PostCreateTruck(client, "B-202");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(factory.CompanyId("Beta Logistics"),
            factory.Db(db => db.Trucks.IgnoreQueryFilters().Single(t => t.UnitNumber == "B-202").CompanyId));
    }

    [Fact]
    public async Task Owner_CreateTruck_EmptyForm_ShowsValidation_NotA500()
    {
        var client = await factory.LoginAs("owner@alpha.example");

        var response = await client.PostForm("/Trucks", "/Trucks/Create",
            new Dictionary<string, string> { ["UnitNumber"] = "  ", ["Plate"] = "" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The Unit number field is required.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dispatcher_CreateTruck_Returns403_NothingCreated()
    {
        var client = await factory.LoginAs("dispatch@alpha.example");

        var get = await client.GetAsync("/Trucks/Create");
        var post = await PostCreateTruck(client, "A-199");

        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.False(TruckExists("A-199"));
    }

    [Fact]
    public async Task Driver_CreateTruck_Returns403()
    {
        var client = await factory.LoginAs("driver1@beta.example");

        var response = await client.GetAsync("/Trucks/Create");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
