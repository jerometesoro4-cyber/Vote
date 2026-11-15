using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using voting.Data;
using voting.Models;

namespace ACMCLVotingMVC.Controllers;

[Authorize(Roles = "Student")]
public class StudentController(VotingDbContext db, IDataProtectionProvider protection) : Controller
{
    private sealed record ReviewedBallot(int StudentId, int ElectionId, int[] CandidateIds);
    private readonly IDataProtector reviewProtector = protection.CreateProtector("ACMCLVote.BallotReview.v1");

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
    public async Task<IActionResult> Vote(int electionId, int[]? candidateIds, [Bind(Prefix = "choices")] Dictionary<int, int[]>? choices,
        string? step, string? reviewToken)
    {
        if (step is "Confirm" or "Edit")
        {
            var reviewed = ReadReview(reviewToken);
            if (reviewed == null || reviewed.StudentId != UserId || reviewed.ElectionId != electionId)
            {
                TempData["Error"] = "Review your choices before submitting.";
                return RedirectToAction(nameof(Vote), new { id = electionId });
            }
            candidateIds = reviewed.CandidateIds;
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var voter = await db.UserAccounts.AsNoTracking().Where(u => u.Id == UserId && u.Role == "Student")
                .Select(u => new { u.VotingGroup }).SingleOrDefaultAsync();
            var election = await db.Elections.Include(e => e.Positions).ThenInclude(p => p.Candidates).SingleOrDefaultAsync(e => e.Id == electionId);
            if (voter == null || election == null || !election.AllowsVoterGroup(voter.VotingGroup))
            {
                TempData["Error"] = "This election is unavailable for your voting group.";
                return RedirectToAction(nameof(ActiveElections));
            }

            var now = DateTime.UtcNow;
            var usesCandidateIds = candidateIds is { Length: > 0 };
            var chosen = usesCandidateIds ? candidateIds! : choices?.Values.SelectMany(ids => ids).ToArray() ?? [];
            var chosenIds = chosen.ToHashSet();
            var candidates = election.Positions.SelectMany(p => p.Candidates).Where(c => chosenIds.Contains(c.Id)).ToList();
            var selections = candidates.ToLookup(c => c.PositionId);
            if (!election.IsVotingOpenAt(now))
                TempData["Error"] = "This election is not currently open for voting.";
            else if (await db.Ballots.AnyAsync(b => b.ElectionId == electionId && b.UserAccountId == UserId))
                TempData["Error"] = "You have already submitted a ballot for this election.";
            else if (!election.HasCompleteBallot())
                TempData["Error"] = "The ballot is unavailable.";
            else if (!ModelState.IsValid || candidates.Count != chosen.Length
                || (!usesCandidateIds && candidates.Any(c => choices?.GetValueOrDefault(c.PositionId)?.Contains(c.Id) != true)))
                TempData["Error"] = "Select valid candidates for each position in this election.";
            else if (election.Positions.FirstOrDefault(p => !p.AcceptsSelectionCount(selections[p.Id].Count())) is { } invalidPosition)
                TempData["Error"] = $"Select {invalidPosition.DescribeSelections(invalidPosition.AvailableMaxSelections)} for {invalidPosition.Name}.";
            else if (step != "Confirm")
            {
                return View(new DashboardViewModel
                {
                    SelectedElection = election,
                    SelectedCandidateIds = chosenIds,
                    BallotReviewToken = step == "Edit" ? null
                        : reviewProtector.Protect(JsonSerializer.Serialize(new ReviewedBallot(UserId, electionId, chosen)))
                });
            }
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

    private ReviewedBallot? ReadReview(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            return JsonSerializer.Deserialize<ReviewedBallot>(reviewProtector.Unprotect(token));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
