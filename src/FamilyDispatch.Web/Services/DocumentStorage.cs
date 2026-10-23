using System.Globalization;

namespace FamilyDispatch.Web.Services;

/// <summary>
/// Delivery documents on disk. Files live outside wwwroot under {Storage:Root}/{companyId}/{guid}.{ext}, are never
/// served as static files, and are only opened after the caller has found the document through the tenant filter.
/// </summary>
public class DocumentStorage(IConfiguration config, IWebHostEnvironment env)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, (string ContentType, byte[] Magic)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("application/pdf", "%PDF"u8.ToArray()),
        [".jpg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        [".jpeg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        [".png"] = ("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
    };

    // A relative root is taken from the app's content root, not the process's working directory.
    public string Root => Path.Combine(env.ContentRootPath, config["Storage:Root"] ?? Path.Combine("App_Data", "uploads"));

    public record Checked(string? Error, string Extension = "", string ContentType = "");

    /// <summary>Checks presence, size, extension and the file's first bytes (a renamed .exe is not a PDF).</summary>
    public static async Task<Checked> ValidateAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return new("Choose a file first.");
        if (file.Length > MaxBytes) return new("File is larger than 5 MB.");

        var extension = Path.GetExtension(file.FileName);
        if (!Allowed.TryGetValue(extension, out var type)) return new("Only PDF, JPG or PNG files are accepted.");

        var header = new byte[type.Magic.Length];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        if (read < header.Length || !header.AsSpan().SequenceEqual(type.Magic))
            return new("Only PDF, JPG or PNG files are accepted.");

        return new(null, extension.ToLowerInvariant(), type.ContentType);
    }

    /// <summary>Saves under a new random name and returns that name. The uploader's file name is never used on disk.</summary>
    public async Task<string> SaveAsync(int companyId, IFormFile file, string extension)
    {
        var storedName = Guid.NewGuid().ToString("N") + extension;
        var folder = Path.Combine(Root, companyId.ToString());
        Directory.CreateDirectory(folder);
        await using var target = new FileStream(Path.Combine(folder, storedName), FileMode.CreateNew);
        await file.CopyToAsync(target);
        return storedName;
    }

    public Stream? Open(int companyId, string storedName)
    {
        var path = Path.Combine(Root, companyId.ToString(), storedName);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void Delete(int companyId, string storedName) =>
        File.Delete(Path.Combine(Root, companyId.ToString(), storedName));

    /// <summary>
    /// The name shown to users: no folders (either slash style), no control characters, no invisible formatting
    /// characters (a right-to-left override can make "x‮fdp.exe" display as "xexe.pdf"), at most 200 characters.
    /// </summary>
    public static string DisplayName(string uploadedName)
    {
        var visible = new string(uploadedName
            .Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray());
        var name = Path.GetFileName(visible.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name)) name = "document";
        return name.Length <= 200 ? name : name[..200];
    }
}
