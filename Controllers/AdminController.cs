using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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

    private static string? CleanVotingGroup(string? group) => string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    private IQueryable<Election> BeforeFirstBallot(int id) => db.Elections
        .Where(e => e.Id == id && (e.Status == "Draft" || e.Status == "Open")
            && !db.Ballots.Any(b => b.ElectionId == e.Id));

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
    public async Task<IActionResult> CreateElection(string? name, DateTime startsAtUtc, DateTime endsAtUtc,
        bool includeStandardPositions = true, DateTime? startsAtManila = null, DateTime? endsAtManila = null,
        string? requiredGroup = null)
    {
        name = name?.Trim();
        if (startsAtManila.HasValue || endsAtManila.HasValue)
        {
            startsAtUtc = Election.ManilaToUtc(startsAtManila);
            endsAtUtc = Election.ManilaToUtc(endsAtManila);
        }

        if (!ModelState.IsValid || string.IsNullOrEmpty(name) || name.Length > 150 || startsAtUtc == default || endsAtUtc <= startsAtUtc)
        {
            TempData["Error"] = "Enter an election name and an end time after the start time.";
            return RedirectToAction(nameof(Elections));
        }

        requiredGroup = CleanVotingGroup(requiredGroup);
        if (requiredGroup is { Length: > 100 })
        {
            TempData["Error"] = "Use a voting group name of 100 characters or fewer.";
            return RedirectToAction(nameof(Elections));
        }

        var election = new Election { Name = name, StartsAtUtc = startsAtUtc, EndsAtUtc = endsAtUtc, RequiredGroup = requiredGroup };
        if (includeStandardPositions)
            election.Positions.AddRange(Election.StandardPositions.Select(name => new Position { Name = name }));
        db.Elections.Add(election);
        await db.SaveChangesAsync();
        TempData["Success"] = "Election created as a draft.";
        return RedirectToAction(nameof(Elections), new { id = election.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSchedule(int id, DateTime? startsAtManila, DateTime? endsAtManila)
    {
        var startsAtUtc = Election.ManilaToUtc(startsAtManila);
        var endsAtUtc = Election.ManilaToUtc(endsAtManila);
        if (!ModelState.IsValid || startsAtUtc == default || endsAtUtc <= startsAtUtc || endsAtUtc <= DateTime.UtcNow)
        {
            TempData["Error"] = "Enter a valid Philippine-time voting period with an end time in the future.";
            return RedirectToAction(nameof(Elections), new { id });
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var changed = await BeforeFirstBallot(id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.StartsAtUtc, startsAtUtc)
                    .SetProperty(e => e.EndsAtUtc, endsAtUtc));
            await transaction.CommitAsync();
            TempData[changed == 1 ? "Success" : "Error"] = changed == 1
                ? "Voting schedule updated."
                : "You can edit the schedule until the first ballot is submitted or the election closes.";
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            TempData["Error"] = "Another request was processed at the same time. Review the schedule before retrying.";
        }

        return RedirectToAction(nameof(Elections), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignVotingGroup(int id, string? votingGroup)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        votingGroup = CleanVotingGroup(votingGroup);
        if (!ModelState.IsValid || votingGroup is { Length: > 100 })
            TempData["Error"] = "Use a voting group name of 100 characters or fewer.";
        else
        {
            var changed = await db.UserAccounts.Where(u => u.Id == id && u.Role == "Student")
                .ExecuteUpdateAsync(updates => updates.SetProperty(u => u.VotingGroup, votingGroup));
            TempData[changed == 1 ? "Success" : "Error"] = changed == 1 ? "Student group saved." : "Choose a registered student.";
        }
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetElectionGroup(int id, string? requiredGroup)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        requiredGroup = CleanVotingGroup(requiredGroup);
        if (!ModelState.IsValid || requiredGroup is { Length: > 100 })
        {
            TempData["Error"] = "Use a voting group name of 100 characters or fewer.";
            return RedirectToAction(nameof(Elections), new { id });
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var changed = await BeforeFirstBallot(id)
                .ExecuteUpdateAsync(updates => updates.SetProperty(e => e.RequiredGroup, requiredGroup));
            await transaction.CommitAsync();
            TempData[changed == 1 ? "Success" : "Error"] = changed == 1
                ? "Voter access saved."
                : "You can edit voter access until the first ballot is submitted or the election closes.";
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            TempData["Error"] = "Another request was processed at the same time. Review voter access before retrying.";
        }

        return RedirectToAction(nameof(Elections), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, string? status)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var election = await db.Elections.Include(e => e.Positions).ThenInclude(p => p.Candidates).SingleOrDefaultAsync(e => e.Id == id);
        if (election == null || !((election.Status == "Draft" && status == "Open") || (election.Status == "Open" && status == "Closed")))
            TempData["Error"] = "Only draft elections can be opened, and only open elections can be closed.";
        else if (status == "Open" && (election.EndsAtUtc <= DateTime.UtcNow || !election.HasCompleteBallot()))
            TempData["Error"] = "An election needs a future end time, at least one position, and enough candidates to meet each position's minimum before it can open.";
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
    public async Task<IActionResult> AddPosition(int electionId, string? name, bool addStandard = false,
        int minSelections = 1, int? maxSelections = null)
    {
        name = name?.Trim() ?? "";
        var maximum = maxSelections ?? minSelections;
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var election = await db.Elections.Include(e => e.Positions).SingleOrDefaultAsync(e => e.Id == electionId);
        if (!ModelState.IsValid || election?.Status != "Draft")
            TempData["Error"] = "Positions can only be added to a draft election.";
        else if (!addStandard && (name.Length == 0 || name.Length > 100))
            TempData["Error"] = "Enter a position name between 1 and 100 characters.";
        else if (!addStandard && (minSelections < 1 || maximum < minSelections))
            TempData["Error"] = "The minimum must be at least 1 and the maximum must be at least the minimum.";
        else
        {
            var names = addStandard ? Election.StandardPositions : [name];
            var additions = names.Except(election.Positions.Select(p => p.Name), StringComparer.OrdinalIgnoreCase)
                .Select(n => new Position
                {
                    Name = n,
                    MinSelections = addStandard ? 1 : minSelections,
                    MaxSelections = addStandard ? 1 : maximum
                }).ToList();
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
