using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using voting.Data;
using voting.Models;

namespace voting.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController(VotingDbContext db) : Controller
{
    public async Task<IActionResult> Index() => View(await DashboardViewModel.LoadAsync(db, true));
    public async Task<IActionResult> Users() => View(await DashboardViewModel.LoadAsync(db, true));
    public Task<IActionResult> Elections(int? id) => ElectionPage(id);
    public Task<IActionResult> Candidates(int? id) => ElectionPage(id);
    public Task<IActionResult> Positions(int? id) => ElectionPage(id);
    public async Task<IActionResult> Monitoring() => View(await DashboardViewModel.LoadAsync(db, true));
    public async Task<IActionResult> Results() => View(await DashboardViewModel.LoadAsync(db, true));

    private async Task<IActionResult> ElectionPage(int? id)
    {
        var model = await DashboardViewModel.LoadAsync(db, true);
        model.SelectedElection = model.Elections.SingleOrDefault(e => e.Id == id);
        return id.HasValue && model.SelectedElection == null ? NotFound() : View(model);
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Settings()
    {
        if (!await IsCurrentAdmin()) return Forbid();
        var invitations = await db.AdminInvitations.AsNoTracking().OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new AdminInvitation { Id = i.Id, CreatedAtUtc = i.CreatedAtUtc, ExpiresAtUtc = i.ExpiresAtUtc,
                UsedAtUtc = i.UsedAtUtc, RevokedAtUtc = i.RevokedAtUtc, Creator = new UserAccount { FullName = i.Creator.FullName } })
            .ToListAsync();
        return View(new AdminInvitationsViewModel { Invitations = invitations });
    }

    private async Task<bool> IsCurrentAdmin() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        && await db.UserAccounts.AnyAsync(u => u.Id == id && u.Role == "Admin");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateInvitation()
    {
        if (!await IsCurrentAdmin()) return Forbid();
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        db.AdminInvitations.Add(new AdminInvitation { CreatorId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            CodeHash = AdminInvitation.HashCode(code), CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7) });
        await db.SaveChangesAsync();
        TempData["NewInvitationCode"] = code;
        TempData["Success"] = "Invitation created.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeInvitation(int id)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        var now = DateTime.UtcNow;
        var changed = await db.AdminInvitations.Where(i => i.Id == id && i.UsedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(updates => updates.SetProperty(i => i.RevokedAtUtc, now));
        TempData[changed == 1 ? "Success" : "Error"] = changed == 1 ? "Invitation revoked." : "Invitation is no longer active.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateElection(string? name, DateTime startsAtUtc, DateTime endsAtUtc, bool includeStandardPositions = true)
    {
        name = name?.Trim();
        if (!ModelState.IsValid || string.IsNullOrEmpty(name) || name.Length > 150 || startsAtUtc == default || endsAtUtc <= startsAtUtc)
        {
            TempData["Error"] = "Enter an election name and a valid UTC start and end time.";
            return RedirectToAction(nameof(Elections));
        }

        var election = new Election { Name = name, StartsAtUtc = startsAtUtc, EndsAtUtc = endsAtUtc };
        if (includeStandardPositions)
            election.Positions.AddRange(Election.StandardPositions.Select(name => new Position { Name = name }));
        db.Elections.Add(election);
        await db.SaveChangesAsync();
        TempData["Success"] = "Election created as a draft.";
        return RedirectToAction(nameof(Elections), new { id = election.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, string? status)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var election = await db.Elections.Include(e => e.Positions).ThenInclude(p => p.Candidates).SingleOrDefaultAsync(e => e.Id == id);
        if (election == null || !((election.Status == "Draft" && status == "Open") || (election.Status == "Open" && status == "Closed")))
            TempData["Error"] = "Only draft elections can be opened, and only open elections can be closed.";
        else if (status == "Open" && (election.EndsAtUtc <= DateTime.UtcNow || !election.HasCompleteBallot()))
            TempData["Error"] = "An election needs a future end time and at least one candidate for each position before it can open.";
        else
        {
            election.Status = status!;
            await db.SaveChangesAsync();
            TempData["Success"] = "Election status updated.";
        }
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Elections), new { id = election?.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPosition(int electionId, string? name, bool addStandard = false)
    {
        name = name?.Trim() ?? "";
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var election = await db.Elections.Include(e => e.Positions).SingleOrDefaultAsync(e => e.Id == electionId);
        if (!ModelState.IsValid || election?.Status != "Draft")
            TempData["Error"] = "Positions can only be added to a draft election.";
        else if (!addStandard && (name.Length == 0 || name.Length > 100))
            TempData["Error"] = "Enter a position name between 1 and 100 characters.";
        else
        {
            var names = addStandard ? Election.StandardPositions : [name];
            var additions = names.Except(election.Positions.Select(p => p.Name), StringComparer.OrdinalIgnoreCase)
                .Select(n => new Position { Name = n }).ToList();
            if (additions.Count == 0)
                TempData["Error"] = addStandard ? "The standard positions already exist in this election." : "That position already exists in this election.";
            else
            {
                election.Positions.AddRange(additions);
                await db.SaveChangesAsync();
                TempData["Success"] = addStandard ? "Standard positions added." : "Position added.";
            }
        }
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Positions), new { id = election?.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCandidate(int positionId, string? name, string? party, string? platform, int? electionId)
    {
        name = name?.Trim(); party = party?.Trim() ?? ""; platform = platform?.Trim() ?? "";
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var position = await db.Positions.Include(p => p.Election).SingleOrDefaultAsync(p => p.Id == positionId);
        if (!ModelState.IsValid || position?.Election.Status != "Draft" || (electionId.HasValue && position.ElectionId != electionId)
            || string.IsNullOrEmpty(name) || name.Length > 100 || party.Length > 100 || platform.Length > 2000)
            TempData["Error"] = "Enter a valid candidate and choose a position in the selected draft election.";
        else
        {
            db.Candidates.Add(new Candidate { PositionId = positionId, Name = name, Party = party, Platform = platform });
            await db.SaveChangesAsync();
            TempData["Success"] = "Candidate added.";
        }
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Candidates), new { id = electionId ?? position?.ElectionId });
    }
}
