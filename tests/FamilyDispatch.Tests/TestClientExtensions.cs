using System.Net;
using System.Text.RegularExpressions;
using FamilyDispatch.Web.Data;

namespace FamilyDispatch.Tests;

public static partial class TestClientExtensions
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    /// <summary>GETs a page and returns the antiforgery token from its form (also sets the antiforgery cookie).</summary>
    public static async Task<string> GetAntiforgeryToken(this HttpClient client, string pageUrl)
    {
        var html = await client.GetStringAsync(pageUrl);
        var match = TokenRegex().Match(html);
        Assert.True(match.Success, $"No antiforgery token on {pageUrl}");
        return match.Groups[1].Value;
    }

    /// <summary>Posts a form the way a browser would: token taken from <paramref name="formPageUrl"/>.</summary>
    public static async Task<HttpResponseMessage> PostForm(this HttpClient client, string formPageUrl, string postUrl,
        IDictionary<string, string> fields)
    {
        var token = await client.GetAntiforgeryToken(formPageUrl);
        var body = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(body));
    }

    public static Task<HttpResponseMessage> PostLogin(this HttpClient client, string email, string password) =>
        client.PostForm("/Account/Login", "/Account/Login",
            new Dictionary<string, string> { ["Email"] = email, ["Password"] = password });

    /// <summary>
    /// Posts a status move straight to the handler, as a hand-crafted request would. The token comes from the
    /// user's own /Loads page, so a load the user can't see (no form to copy) can still be targeted.
    /// </summary>
    public static Task<HttpResponseMessage> PostStatus(this HttpClient client, int loadId, string target, string? from = null)
    {
        var fields = new Dictionary<string, string> { ["target"] = target };
        if (from is not null) fields["from"] = from;
        return client.PostForm("/Loads", $"/Loads/{loadId}?handler=Status", fields);
    }

    /// <summary>Uploads a file straight to the handler, with the token from the user's own /Loads page.</summary>
    public static async Task<HttpResponseMessage> PostUpload(this HttpClient client, int loadId, string fileName, byte[] bytes)
    {
        var token = await client.GetAntiforgeryToken("/Loads");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new ByteArrayContent(bytes), "file", fileName },
        };
        return await client.PostAsync($"/Loads/{loadId}?handler=Upload", form);
    }

    /// <summary>A fresh client signed in as one of the seeded demo users.</summary>
    public static async Task<HttpClient> LoginAs(this AppFactory factory, string email)
    {
        var client = factory.NewClient();
        var response = await client.PostLogin(email, SeedData.DemoPassword);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }
}
