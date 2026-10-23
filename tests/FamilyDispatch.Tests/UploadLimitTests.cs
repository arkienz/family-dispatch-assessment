using System.Net;
using System.Text;
using FamilyDispatch.Web.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Tests;

/// <summary>The app with an upload limit of 2 per user per hour, so the limit is reached quickly.</summary>
public class LowUploadLimitFactory : AppFactory
{
    public const int Limit = 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:UploadsPerHour", Limit.ToString());
    }
}

public class UploadLimitTests(LowUploadLimitFactory factory) : IClassFixture<LowUploadLimitFactory>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\nfake POD\n%%EOF");

    private int DocumentRows() => factory.Db(db => db.LoadDocuments.IgnoreQueryFilters().Count());

    [Fact]
    public async Task Uploads_OverThePerUserLimit_Get429_NothingStored_OtherUsersAndActionsUnaffected()
    {
        var driver = await factory.LoginAs("driver1@alpha.example");
        var loadId = factory.LoadId("ALP-1002");
        for (var i = 0; i < LowUploadLimitFactory.Limit; i++)
            Assert.Equal(HttpStatusCode.Redirect, (await driver.PostUpload(loadId, $"pod{i}.pdf", Pdf)).StatusCode);
        var rows = DocumentRows();

        var blocked = await driver.PostUpload(loadId, "one-too-many.pdf", Pdf);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Too many attempts", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(rows, DocumentRows());

        // The limit counts uploads only, per user: the same driver can still move the load and view it,
        // and another user of the same company can still upload.
        Assert.Equal(HttpStatusCode.Redirect, (await driver.PostStatus(loadId, nameof(LoadStatus.Delivered))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await driver.GetAsync($"/Loads/{loadId}")).StatusCode);
        var owner = await factory.LoginAs("owner@alpha.example");
        Assert.Equal(HttpStatusCode.Redirect, (await owner.PostUpload(loadId, "owner.pdf", Pdf)).StatusCode);
    }
}
