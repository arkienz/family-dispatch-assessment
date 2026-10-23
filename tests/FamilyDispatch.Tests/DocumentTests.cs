using System.Net;
using System.Text;
using FamilyDispatch.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Tests;

/// <summary>Delivery documents: what is accepted, and who can upload or download them.</summary>
public class DocumentTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static byte[] Pdf(string text = "fake POD") => Encoding.ASCII.GetBytes("%PDF-1.4\n" + text + "\n%%EOF");

    private int DocumentRows() => factory.Db(db => db.LoadDocuments.IgnoreQueryFilters().Count());

    private int FilesOnDisk() => Directory.Exists(factory.UploadRoot)
        ? Directory.GetFiles(factory.UploadRoot, "*", SearchOption.AllDirectories).Length
        : 0;

    private LoadDocument LatestDocument() =>
        factory.Db(db => db.LoadDocuments.IgnoreQueryFilters().AsNoTracking().OrderByDescending(d => d.Id).First());

    /// <summary>Uploads as <paramref name="email"/> and returns the stored document.</summary>
    private async Task<LoadDocument> Upload(string email, string loadRef, string fileName = "pod.pdf", byte[]? bytes = null)
    {
        var client = await factory.LoginAs(email);
        var response = await client.PostUpload(factory.LoadId(loadRef), fileName, bytes ?? Pdf());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return LatestDocument();
    }

    [Fact]
    public async Task Upload_ValidPdf_ThenDownload_SameCompany_ReturnsSameBytes()
    {
        var bytes = Pdf("signed by receiver");
        var doc = await Upload("driver1@alpha.example", "ALP-1001", @"..\..\receipt.pdf", bytes);

        // Stored under a random name in the company's folder; the uploaded name is display-only and stripped of folders.
        Assert.Equal("receipt.pdf", doc.OriginalFileName);
        Assert.NotEqual("receipt.pdf", doc.StoredFileName);
        Assert.True(File.Exists(Path.Combine(factory.UploadRoot, doc.CompanyId.ToString(), doc.StoredFileName)));

        var owner = await factory.LoginAs("owner@alpha.example");
        var response = await owner.GetAsync($"/Loads/{doc.LoadId}/Documents/{doc.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl?.NoStore, "delivery documents must not be cached");
        Assert.Contains("receipt.pdf", await owner.GetStringAsync($"/Loads/{doc.LoadId}"));
    }

    [Fact]
    public async Task AlphaDispatcher_UploadToBetaLoad_Returns404_NoFileWritten()
    {
        var client = await factory.LoginAs("dispatch@alpha.example");
        var rows = DocumentRows();
        var files = FilesOnDisk();

        var response = await client.PostUpload(factory.LoadId("BET-2001"), "pod.pdf", Pdf());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(rows, DocumentRows());
        Assert.Equal(files, FilesOnDisk());
    }

    [Fact]
    public async Task AlphaOwner_DownloadBetaDocument_Returns404_BothWays()
    {
        var betaDoc = await Upload("owner@beta.example", "BET-2001");
        var alpha = await factory.LoginAs("owner@alpha.example");

        // Via Beta's own load id, and via an Alpha load id the user can see paired with Beta's document id.
        var viaBetaLoad = await alpha.GetAsync($"/Loads/{betaDoc.LoadId}/Documents/{betaDoc.Id}");
        var viaAlphaLoad = await alpha.GetAsync($"/Loads/{factory.LoadId("ALP-1001")}/Documents/{betaDoc.Id}");

        Assert.Equal(HttpStatusCode.NotFound, viaBetaLoad.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaAlphaLoad.StatusCode);
        Assert.DoesNotContain("%PDF", await viaBetaLoad.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Download_DocumentPairedWithWrongLoadOfSameCompany_Returns404()
    {
        var doc = await Upload("owner@alpha.example", "ALP-1001");
        var owner = await factory.LoginAs("owner@alpha.example");

        var response = await owner.GetAsync($"/Loads/{factory.LoadId("ALP-1002")}/Documents/{doc.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Driver_Upload_DownloadByOtherDriverSameCompany_Returns404()
    {
        var doc = await Upload("driver1@alpha.example", "ALP-1001");
        var driver2 = await factory.LoginAs("driver2@alpha.example");

        var response = await driver2.GetAsync($"/Loads/{doc.LoadId}/Documents/{doc.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Driver_UploadToOtherDriversLoad_Returns404()
    {
        var client = await factory.LoginAs("driver2@alpha.example");
        var rows = DocumentRows();

        var response = await client.PostUpload(factory.LoadId("ALP-1001"), "pod.pdf", Pdf());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(rows, DocumentRows());
    }

    [Fact]
    public async Task Anonymous_Download_RedirectsToLogin()
    {
        var doc = await Upload("owner@alpha.example", "ALP-1002");

        var response = await factory.NewClient().GetAsync($"/Loads/{doc.LoadId}/Documents/{doc.Id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    public static TheoryData<string, byte[], string> RejectedFiles => new()
    {
        { "invoice.pdf", Encoding.ASCII.GetBytes("MZ\x90\0 this is an exe renamed to pdf"), "Only PDF, JPG or PNG files are accepted." },
        { "notes.txt", Encoding.ASCII.GetBytes("plain text"), "Only PDF, JPG or PNG files are accepted." },
        { "pod.pdf", [], "Choose a file first." },
        { "big.pdf", [.. Encoding.ASCII.GetBytes("%PDF-1.4\n"), .. new byte[5 * 1024 * 1024]], "File is larger than 5 MB." },
    };

    [Theory]
    [MemberData(nameof(RejectedFiles))]
    public async Task Upload_BadFile_Rejected_NothingStored(string fileName, byte[] bytes, string message)
    {
        var client = await factory.LoginAs("owner@alpha.example");
        var id = factory.LoadId("ALP-1003");
        var rows = DocumentRows();
        var files = FilesOnDisk();

        var response = await client.PostUpload(id, fileName, bytes);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(message, await client.GetStringAsync($"/Loads/{id}"));
        Assert.Equal(rows, DocumentRows());
        Assert.Equal(files, FilesOnDisk());
    }

    [Fact]
    public async Task Upload_OverTheRequestCap_Refused_NothingStored_NoCrashDetails()
    {
        // Above the 6 MB request cap the body is cut off before the handler runs (the page's script stops
        // this in a browser). The server must still refuse it cleanly.
        var client = await factory.LoginAs("owner@alpha.example");
        var rows = DocumentRows();
        var files = FilesOnDisk();

        var response = await client.PostUpload(factory.LoadId("ALP-1003"), "huge.pdf", [.. Pdf(), .. new byte[7 * 1024 * 1024]]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("That request wasn&#x27;t accepted", await response.Content.ReadAsStringAsync());
        Assert.Equal(rows, DocumentRows());
        Assert.Equal(files, FilesOnDisk());
    }

    [Theory]
    [InlineData("receipt.pdf", "receipt.pdf")]
    [InlineData("a\u202Efdp.exe", "afdp.exe")]          // right-to-left override removed
    [InlineData("a\r\nb.pdf", "ab.pdf")]               // control characters removed
    [InlineData(@"C:\fakepath\pod.pdf", "pod.pdf")]   // folders removed
    [InlineData("\u200B", "document")]                  // nothing visible left
    public void DisplayName_RemovesFoldersAndHiddenCharacters(string uploaded, string shown) =>
        Assert.Equal(shown, Web.Services.DocumentStorage.DisplayName(uploaded));

    [Fact]
    public async Task Upload_PngAndJpgWithRealHeaders_Accepted()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        byte[] jpg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

        Assert.Equal("image/png", (await Upload("owner@beta.example", "BET-2002", "photo.PNG", png)).ContentType);
        Assert.Equal("image/jpeg", (await Upload("owner@beta.example", "BET-2002", "photo.jpeg", jpg)).ContentType);
    }
}
