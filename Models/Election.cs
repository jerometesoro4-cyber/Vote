using System.ComponentModel.DataAnnotations;

namespace voting.Models;

public class Election
{
    public static IReadOnlyList<string> StandardPositions { get; } =
        ["President", "Vice President", "Secretary", "Treasurer", "Auditor"];

    public int Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Draft";
    public List<Position> Positions { get; set; } = [];

    public bool HasCompleteBallot() => Positions.Count > 0 && Positions.All(p => p.Candidates.Count > 0);

    public bool IsVotingOpenAt(DateTime now) => Status == "Open" && StartsAtUtc <= now && now < EndsAtUtc;

    public string VotingStatusAt(DateTime now) => Status switch
    {
        "Open" when now < StartsAtUtc => "Scheduled",
        "Open" when now >= EndsAtUtc => "Voting ended",
        "Open" => "Voting open",
        _ => Status
    };
}

public class Position
{
    public int Id { get; set; }
    public int ElectionId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public Election Election { get; set; } = null!;
    public List<Candidate> Candidates { get; set; } = [];
}

public class Ballot
{
    public int Id { get; set; }
    public int UserAccountId { get; set; }
    public int ElectionId { get; set; }
    public DateTime CastAtUtc { get; set; }
    public UserAccount UserAccount { get; set; } = null!;
    public Election Election { get; set; } = null!;
    public List<BallotSelection> Selections { get; set; } = [];
}

public class BallotSelection
{
    public int Id { get; set; }
    public int BallotId { get; set; }
    public int PositionId { get; set; }
    public int CandidateId { get; set; }
    public Ballot Ballot { get; set; } = null!;
    public Position Position { get; set; } = null!;
    public Candidate Candidate { get; set; } = null!;
}
