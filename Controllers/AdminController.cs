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
    public async Task<IActionResult> Elections() => View(await DashboardViewModel.LoadAsync(db, true));
    public async Task<IActionResult> Candidates() => View(await DashboardViewModel.LoadAsync(db, true));
    public async Task<IActionResult> Positions() => View(await DashboardViewModel.LoadAsync(db, true));
    public async Task<IActionResult> Monitoring() => View(await DashboardViewModel.LoadAsync(db, true));
    public async Task<IActionResult> Results() => View(await DashboardViewModel.LoadAsync(db, true));
    public async Task<IActionResult> Settings() => View(await DashboardViewModel.LoadAsync(db, true));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateElection(string? name, DateTime startsAtUtc, DateTime endsAtUtc)
    {
        name = name?.Trim();
        if (!ModelState.IsValid || string.IsNullOrEmpty(name) || name.Length > 150 || startsAtUtc == default || endsAtUtc <= startsAtUtc)
            TempData["Error"] = "Enter an election name and a valid UTC start and end time.";
        else
        {
            db.Elections.Add(new Election { Name = name, StartsAtUtc = startsAtUtc, EndsAtUtc = endsAtUtc });
            await db.SaveChangesAsync();
            TempData["Success"] = "Election created as a draft.";
        }
        return RedirectToAction(nameof(Elections));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, string? status)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var election = await db.Elections.Include(e => e.Positions).ThenInclude(p => p.Candidates).SingleOrDefaultAsync(e => e.Id == id);
        if (election == null || !((election.Status == "Draft" && status == "Open") || (election.Status == "Open" && status == "Closed")))
            TempData["Error"] = "Only draft elections can be opened, and only open elections can be closed.";
        else if (status == "Open" && (election.EndsAtUtc <= DateTime.UtcNow || election.Positions.Count == 0 || election.Positions.Any(p => p.Candidates.Count == 0)))
            TempData["Error"] = "An election needs a future end time and at least one candidate for each position before it can open.";
        else
        {
            election.Status = status!;
            await db.SaveChangesAsync();
            TempData["Success"] = "Election status updated.";
        }
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Elections));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPosition(int electionId, string? name)
    {
        name = name?.Trim();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var election = await db.Elections.FindAsync(electionId);
        if (election?.Status != "Draft" || string.IsNullOrEmpty(name) || name.Length > 100)
            TempData["Error"] = "Enter a position name for a draft election.";
        else
        {
            db.Positions.Add(new Position { ElectionId = electionId, Name = name });
            await db.SaveChangesAsync();
            TempData["Success"] = "Position added.";
        }
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Positions));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCandidate(int positionId, string? name, string? party, string? platform)
    {
        name = name?.Trim(); party = party?.Trim() ?? ""; platform = platform?.Trim() ?? "";
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var position = await db.Positions.Include(p => p.Election).SingleOrDefaultAsync(p => p.Id == positionId);
        if (position?.Election.Status != "Draft" || string.IsNullOrEmpty(name) || name.Length > 100 || party.Length > 100 || platform.Length > 2000)
            TempData["Error"] = "Enter a valid candidate for a position in a draft election.";
        else
        {
            db.Candidates.Add(new Candidate { PositionId = positionId, Name = name, Party = party, Platform = platform });
            await db.SaveChangesAsync();
            TempData["Success"] = "Candidate added.";
        }
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Candidates));
    }
}
