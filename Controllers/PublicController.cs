using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using voting.Data;
using voting.Models;
using voting.Services;

namespace ACMCLVotingMVC.Controllers;

[AllowAnonymous]
public class PublicPortalController(VotingDbContext db, CandidatePhotoStorage photos) : Controller
{
    public async Task<IActionResult> Schedule() => View(await DashboardViewModel.LoadAsync(db, false));
    public async Task<IActionResult> Candidates() => View(await DashboardViewModel.LoadAsync(db, false));
    public IActionResult Guidelines() => View();
    public async Task<IActionResult> Results() => View(await DashboardViewModel.LoadAsync(db, false));
    public IActionResult Announcements() => View();

    [HttpGet("/CandidatePhoto/{id:int}")]
    public async Task<IActionResult> CandidatePhoto(int id)
    {
        var candidate = await db.Candidates.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.PhotoFileName,
                c.PhotoContentType,
                ElectionStatus = c.Position.Election.Status
            })
            .SingleOrDefaultAsync();

        if (candidate == null || string.IsNullOrEmpty(candidate.PhotoFileName))
        {
            return NotFound();
        }

        if (candidate.ElectionStatus == "Draft")
        {
            var isAdmin = User.IsInRole("Admin")
                && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)
                && await db.UserAccounts.AsNoTracking()
                    .AnyAsync(u => u.Id == adminId && u.Role == "Admin" && u.EmailVerifiedAtUtc != null);
            if (!isAdmin)
            {
                return NotFound();
            }
        }

        var resolved = photos.TryResolveFile(candidate.PhotoFileName, candidate.PhotoContentType);
        if (resolved == null)
        {
            return NotFound();
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        Response.Headers["Cache-Control"] = "private, no-cache";
        return PhysicalFile(resolved.Value.FullPath, resolved.Value.ContentType);
    }
}
