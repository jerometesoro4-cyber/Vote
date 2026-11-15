using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using voting.Data;
using voting.Models;

namespace ACMCLVotingMVC.Controllers;

[Authorize(Roles = "Student")]
public class StudentController(VotingDbContext db) : Controller
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private Task<DashboardViewModel> Model() => DashboardViewModel.LoadAsync(db, false, UserId);
    public async Task<IActionResult> Index() => View(await Model());
    public async Task<IActionResult> ActiveElections() => View(await Model());
    public async Task<IActionResult> History() => View(await Model());
    public async Task<IActionResult> Candidates(int? id)
    {
        var model = await Model();
        var now = DateTime.UtcNow;
        model.Elections.RemoveAll(e => !e.IsVotingOpenAt(now));
        model.SelectedElection = model.Elections.SingleOrDefault(e => e.Id == id);
        if (id.HasValue && model.SelectedElection == null)
        {
            TempData["Error"] = "This candidate list is available during the election's voting period.";
            return RedirectToAction(nameof(Candidates), new { id = (int?)null });
        }
        return View(model);
    }
    public async Task<IActionResult> Results() => View(await Model());
    public IActionResult QrAccess() => View("Qr");

    [HttpGet]
    public async Task<IActionResult> Vote(int id)
    {
        var model = await Model();
        model.SelectedElection = model.Elections.SingleOrDefault(e => e.Id == id);
        if (model.SelectedElection == null) return NotFound();
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Vote(int electionId, int[]? candidateIds, Dictionary<int, int>? choices)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var election = await db.Elections.Include(e => e.Positions).ThenInclude(p => p.Candidates).SingleOrDefaultAsync(e => e.Id == electionId);
            var now = DateTime.UtcNow;
            var usesCandidateIds = candidateIds is { Length: > 0 };
            var chosen = usesCandidateIds ? candidateIds! : choices?.Values.ToArray() ?? [];
            var candidates = election?.Positions.SelectMany(p => p.Candidates).Where(c => chosen.Contains(c.Id)).ToList() ?? [];
            if (UserId == 0 || election == null || !election.IsVotingOpenAt(now))
                TempData["Error"] = "This election is not currently open for voting.";
            else if (await db.Ballots.AnyAsync(b => b.ElectionId == electionId && b.UserAccountId == UserId))
                TempData["Error"] = "You have already submitted a ballot for this election.";
            else if (!ModelState.IsValid || !election.HasCompleteBallot() || chosen.Length != election.Positions.Count
                || candidates.Count != chosen.Length || candidates.Select(c => c.PositionId).Distinct().Count() != election.Positions.Count
                || (!usesCandidateIds && candidates.Any(c => choices?.GetValueOrDefault(c.PositionId) != c.Id)))
                TempData["Error"] = "Select one valid candidate for each position.";
            else
            {
                db.Ballots.Add(new Ballot { ElectionId = electionId, UserAccountId = UserId, CastAtUtc = now,
                    Selections = candidates.Select(c => new BallotSelection { PositionId = c.PositionId, CandidateId = c.Id }).ToList() });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                TempData["Success"] = "Your ballot has been submitted.";
                return RedirectToAction(nameof(History));
            }
            await transaction.RollbackAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            TempData["Error"] = "You have already submitted a ballot for this election.";
        }
        catch (Exception ex) when (ex is SqlException { Number: 1205 } || ex is DbUpdateException { InnerException: SqlException { Number: 1205 } })
        {
            TempData["Error"] = "Another request was processed at the same time. Check your voting history before retrying.";
        }
        return RedirectToAction(nameof(Vote), new { id = electionId });
    }
}
