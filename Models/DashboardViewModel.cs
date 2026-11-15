using Microsoft.EntityFrameworkCore;
using voting.Data;

namespace voting.Models;

public class CandidateResult
{
    public int ElectionId { get; set; }
    public string ElectionName { get; set; } = "";
    public string PositionName { get; set; } = "";
    public string CandidateName { get; set; } = "";
    public string Party { get; set; } = "";
    public int Votes { get; set; }
}

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

    public static async Task<DashboardViewModel> LoadAsync(VotingDbContext db, bool admin, int? userId = null)
    {
        var elections = await db.Elections.AsNoTracking().Include(e => e.Positions).ThenInclude(p => p.Candidates)
            .Where(e => admin || e.Status != "Draft").OrderByDescending(e => e.StartsAtUtc).ToListAsync();
        var ballots = await db.Ballots.AsNoTracking().Include(b => b.Election).Include(b => b.Selections)
            .Where(b => admin || b.UserAccountId == userId).ToListAsync();
        var users = admin ? await db.UserAccounts.AsNoTracking().Select(u => new UserAccount
        {
            Id = u.Id, FullName = u.FullName, Email = u.Email, Role = u.Role,
            YearLevel = u.YearLevel, CreatedAtUtc = u.CreatedAtUtc
        }).ToListAsync() : [];
        var results = await db.Candidates.AsNoTracking()
            .Where(c => admin || c.Position.Election.Status == "Closed")
            .Select(c => new CandidateResult
            {
                ElectionId = c.Position.ElectionId, ElectionName = c.Position.Election.Name,
                PositionName = c.Position.Name, CandidateName = c.Name, Party = c.Party,
                Votes = db.BallotSelections.Count(s => s.CandidateId == c.Id)
            }).ToListAsync();
        return new DashboardViewModel
        {
            Elections = elections, Positions = elections.SelectMany(e => e.Positions).ToList(),
            Candidates = elections.SelectMany(e => e.Positions).SelectMany(p => p.Candidates).ToList(),
            Users = users, Ballots = ballots, Results = results, StudentCount = admin ? users.Count(u => u.Role == "Student") : 0,
            BallotCount = ballots.Count, CurrentUserId = userId
        };
    }
}
