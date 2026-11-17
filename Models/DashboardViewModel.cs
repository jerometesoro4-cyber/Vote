using Microsoft.EntityFrameworkCore;
using voting.Data;

namespace voting.Models;

public class CandidateResult
{
    public int ElectionId { get; set; }
    public string ElectionName { get; set; } = "";
    public int PositionId { get; set; }
    public string PositionName { get; set; } = "";
    public int CandidateId { get; set; }
    public string CandidateName { get; set; } = "";
    public string Party { get; set; } = "";
    public string? PhotoFileName { get; set; }
    public int Votes { get; set; }
}

public class ElectionTurnoutSummary
{
    public int ElectionId { get; set; }
    public string ElectionName { get; set; } = "";
    public string Status { get; set; } = "";
    public string DisplayStatus { get; set; } = "";
    public string? RequiredGroup { get; set; }
    public int BallotCount { get; set; }
    public int EligibleVoterCount { get; set; }
    public int TurnoutPercent { get; set; }
    public DateTime StartsAtManila { get; set; }
    public DateTime EndsAtManila { get; set; }
}

public class StatusAggregateSummary
{
    public string Status { get; set; } = "";
    public int ElectionCount { get; set; }
    public int BallotCount { get; set; }
}

public sealed record PaginationModel(
    int CurrentPage,
    int TotalPages,
    int TotalItems,
    int PageSize,
    string Action,
    int? Id = null,
    string? Q = null,
    string? Status = null,
    string? Group = null,
    int? PositionId = null);

public class DashboardViewModel
{
    public List<Election> Elections { get; set; } = [];
    public List<Position> Positions { get; set; } = [];
    public List<Candidate> Candidates { get; set; } = [];
    public List<UserAccount> Users { get; set; } = [];
    public List<Ballot> Ballots { get; set; } = [];
    public List<CandidateResult> Results { get; set; } = [];
    public int StudentCount { get; set; }
    public int BallotCount { get; set; }
    public int? CurrentUserId { get; set; }
    public Election? SelectedElection { get; set; }
    public HashSet<int> SelectedCandidateIds { get; set; } = [];
    public string? BallotReviewToken { get; set; }

    public string? SearchQuery { get; set; }
    public string? StatusFilter { get; set; }
    public string? GroupFilter { get; set; }
    public int? PositionFilter { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalItems { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalItems / Math.Max(1, PageSize)));
    public List<Election>? PagedElections { get; set; }
    public List<Candidate>? PagedCandidates { get; set; }
    public List<UserAccount>? PagedVoters { get; set; }
    public List<string> AvailableGroups { get; set; } = [];
    public List<ElectionTurnoutSummary> ElectionTurnouts { get; set; } = [];
    public List<StatusAggregateSummary> StatusSummaries { get; set; } = [];

    public static async Task<DashboardViewModel> LoadAsync(VotingDbContext db, bool admin, int? userId = null)
    {
        var elections = await db.Elections.AsNoTracking().Include(e => e.Positions).ThenInclude(p => p.Candidates)
            .Where(e => admin || e.Status != "Draft").OrderByDescending(e => e.StartsAtUtc).ThenByDescending(e => e.Id).ToListAsync();
        if (userId.HasValue)
        {
            var student = await db.UserAccounts.AsNoTracking().Where(u => u.Id == userId && u.Role == "Student")
                .Select(u => new { u.VotingGroup }).SingleOrDefaultAsync();
            elections.RemoveAll(e => student == null || !e.AllowsVoterGroup(student.VotingGroup));
        }

        var ballots = await db.Ballots.AsNoTracking().Include(b => b.Election).Include(b => b.Selections)
            .Where(b => admin || b.UserAccountId == userId).ToListAsync();

        var users = admin ? await db.UserAccounts.AsNoTracking().Select(u => new UserAccount
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email,
            Role = u.Role,
            YearLevel = u.YearLevel,
            VotingGroup = u.VotingGroup,
            CreatedAtUtc = u.CreatedAtUtc
        }).ToListAsync() : [];

        var results = await db.Candidates.AsNoTracking()
            .Where(c => admin || c.Position.Election.Status == "Closed")
            .Select(c => new CandidateResult
            {
                ElectionId = c.Position.ElectionId,
                ElectionName = c.Position.Election.Name,
                PositionId = c.PositionId,
                PositionName = c.Position.Name,
                CandidateId = c.Id,
                CandidateName = c.Name,
                Party = c.Party,
                PhotoFileName = c.PhotoFileName,
                Votes = db.BallotSelections.Count(s => s.CandidateId == c.Id)
            }).ToListAsync();

        var turnouts = new List<ElectionTurnoutSummary>();
        var statusSummaries = new List<StatusAggregateSummary>();
        var availableGroups = new List<string>();
        var studentCount = 0;
        var ballotCount = ballots.Count;

        if (admin)
        {
            var now = DateTime.UtcNow;
            var ballotCountsByElection = await db.Ballots.AsNoTracking()
                .GroupBy(b => b.ElectionId)
                .Select(g => new { ElectionId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ElectionId, x => x.Count);

            studentCount = await db.UserAccounts.AsNoTracking().CountAsync(u => u.Role == "Student");
            ballotCount = await db.Ballots.AsNoTracking().CountAsync();

            var studentGroupCounts = await db.UserAccounts.AsNoTracking()
                .Where(u => u.Role == "Student" && u.VotingGroup != null)
                .GroupBy(u => u.VotingGroup!)
                .Select(g => new { Group = g.Key, Count = g.Count() })
                .ToListAsync();

            availableGroups = studentGroupCounts.Select(g => g.Group)
                .Concat(elections.Select(e => e.RequiredGroup).OfType<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var election in elections)
            {
                var count = ballotCountsByElection.GetValueOrDefault(election.Id, 0);
                var eligible = election.RequiredGroup == null
                    ? studentCount
                    : studentGroupCounts
                        .Where(g => string.Equals(g.Group, election.RequiredGroup, StringComparison.OrdinalIgnoreCase))
                        .Sum(g => g.Count);
                var denominator = Math.Max(eligible, count);
                var pct = denominator == 0 ? 0 : (int)Math.Round(100.0 * count / denominator);
                turnouts.Add(new ElectionTurnoutSummary
                {
                    ElectionId = election.Id,
                    ElectionName = election.Name,
                    Status = election.Status,
                    DisplayStatus = election.VotingStatusAt(now),
                    RequiredGroup = election.RequiredGroup,
                    BallotCount = count,
                    EligibleVoterCount = denominator,
                    TurnoutPercent = pct,
                    StartsAtManila = election.StartsAtManila,
                    EndsAtManila = election.EndsAtManila
                });
            }

            foreach (var statusName in new[] { "Open", "Draft", "Closed" })
            {
                var matching = turnouts.Where(t => t.Status == statusName).ToList();
                statusSummaries.Add(new StatusAggregateSummary
                {
                    Status = statusName,
                    ElectionCount = matching.Count,
                    BallotCount = matching.Sum(m => m.BallotCount)
                });
            }
        }

        return new DashboardViewModel
        {
            Elections = elections,
            Positions = elections.SelectMany(e => e.Positions).ToList(),
            Candidates = elections.SelectMany(e => e.Positions).SelectMany(p => p.Candidates).ToList(),
            Users = users,
            Ballots = ballots,
            Results = results,
            StudentCount = studentCount,
            BallotCount = ballotCount,
            CurrentUserId = userId,
            AvailableGroups = availableGroups,
            ElectionTurnouts = turnouts,
            StatusSummaries = statusSummaries
        };
    }
}
