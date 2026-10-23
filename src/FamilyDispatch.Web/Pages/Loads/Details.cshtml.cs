using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Domain;
using FamilyDispatch.Web.Security;
using FamilyDispatch.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Web.Pages.Loads;

// Caps the whole request a little above the 5 MB file limit; ValidateAsync gives the friendly message below that.
[RequestSizeLimit(DocumentStorage.MaxBytes + 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = DocumentStorage.MaxBytes + 1024 * 1024)]
[EnableRateLimiting(RateLimits.Upload)] // the policy only counts Upload POSTs
public class DetailsModel(AppDbContext db, DocumentStorage storage, ICurrentUser currentUser) : PageModel
{
    public Load Load { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Another company's load, or another driver's load, is filtered out and gets the same 404 as a missing id.
        var load = await db.Loads.AsNoTracking()
            .Include(l => l.Driver)
            .Include(l => l.Truck)
            .Include(l => l.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (load is null) return NotFound();
        Load = load;
        return Page();
    }

    /// <summary>
    /// Any user who can see the load may move it (a driver only sees their own). The form sends the target
    /// status, and LoadWorkflow decides; the button on the page is only a convenience.
    /// </summary>
    public async Task<IActionResult> OnPostStatusAsync(int id, LoadStatus target, LoadStatus? from)
    {
        var load = await db.Loads.FirstOrDefaultAsync(l => l.Id == id);
        if (load is null) return NotFound();
        if (!ModelState.IsValid) return BadRequest(); // a status value that doesn't exist
        // The page showed an older status: someone moved the load since. Say so instead of "not allowed".
        if (from is not null && from != load.Status) return StatusCode(StatusCodes.Status409Conflict);
        if (!LoadWorkflow.CanMove(load.Status, target)) return BadRequest();

        LoadWorkflow.Move(load, target, DateTime.UtcNow);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(StatusCodes.Status409Conflict);
        }

        TempData["Ok"] = $"{load.ReferenceNo} marked as {Ui.StatusLabel(target)}.";
        return RedirectToPage(new { id });
    }

    /// <summary>Attaches a delivery document. Same rule as status: only a user who can see the load.</summary>
    public async Task<IActionResult> OnPostUploadAsync(int id, IFormFile? file)
    {
        var load = await db.Loads.FirstOrDefaultAsync(l => l.Id == id);
        if (load is null) return NotFound();

        var check = await DocumentStorage.ValidateAsync(file);
        if (check.Error is not null)
        {
            TempData["Error"] = check.Error;
            return RedirectToPage(new { id });
        }

        var storedName = await storage.SaveAsync(load.CompanyId, file!, check.Extension);
        db.LoadDocuments.Add(new LoadDocument
        {
            LoadId = load.Id,
            OriginalFileName = DocumentStorage.DisplayName(file!.FileName),
            StoredFileName = storedName,
            ContentType = check.ContentType,
            SizeBytes = file.Length,
            UploadedByUserId = currentUser.UserId!.Value,
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            // No orphan file when the row can't be saved. A failed cleanup must not hide the original error.
            try { storage.Delete(load.CompanyId, storedName); } catch (IOException) { }
            throw;
        }

        TempData["Ok"] = $"Document attached to {load.ReferenceNo}.";
        return RedirectToPage(new { id });
    }
}
