using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using voting.Data;
using voting.Models;
using voting.Services;

namespace voting.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController(VotingDbContext db, CandidatePhotoStorage photos) : Controller
{
    private const int DefaultPageSize = 10;

    public async Task<IActionResult> Index()
    {
        if (!await IsCurrentAdmin()) return Forbid();
        return View(await DashboardViewModel.LoadAsync(db, true));
    }

    public async Task<IActionResult> Users(string? q = null, string? group = null, int page = 1)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        var model = await DashboardViewModel.LoadAsync(db, true);

        var trimmedQuery = CleanFilter(q);
        var trimmedGroup = CleanFilter(group);

        var query = db.UserAccounts.AsNoTracking().Where(u => u.Role == "Student");
        if (!string.IsNullOrEmpty(trimmedQuery))
        {
            query = query.Where(u =>
                u.FullName.Contains(trimmedQuery)
                || u.Email.Contains(trimmedQuery)
                || (u.YearLevel != null && u.YearLevel.Contains(trimmedQuery))
                || (u.VotingGroup != null && u.VotingGroup.Contains(trimmedQuery)));
        }

        if (string.Equals(trimmedGroup, "__none__", StringComparison.Ordinal))
        {
            query = query.Where(u => u.VotingGroup == null);
        }
        else if (!string.IsNullOrEmpty(trimmedGroup))
        {
            query = query.Where(u => u.VotingGroup == trimmedGroup);
        }

        var ordered = query.OrderBy(u => u.FullName).ThenBy(u => u.Id);
        var total = await ordered.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)total / DefaultPageSize));
        var currentPage = Math.Clamp(page, 1, totalPages);

        var pagedVoters = await ordered
            .Skip((currentPage - 1) * DefaultPageSize)
            .Take(DefaultPageSize)
            .Select(u => new UserAccount
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                Role = u.Role,
                YearLevel = u.YearLevel,
                VotingGroup = u.VotingGroup,
                CreatedAtUtc = u.CreatedAtUtc,
                EmailVerifiedAtUtc = u.EmailVerifiedAtUtc
            })
            .ToListAsync();

        model.SearchQuery = trimmedQuery;
        model.GroupFilter = trimmedGroup;
        model.CurrentPage = currentPage;
        model.PageSize = DefaultPageSize;
        model.TotalItems = total;
        model.PagedVoters = pagedVoters;

        return View(model);
    }

    public async Task<IActionResult> Elections(int? id, string? q = null, string? status = null, string? group = null, int page = 1)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        var model = await DashboardViewModel.LoadAsync(db, true);
        var trimmedQuery = CleanFilter(q);
        var trimmedStatus = CleanStatus(status);
        var trimmedGroup = CleanFilter(group);

        model.SearchQuery = trimmedQuery;
        model.StatusFilter = trimmedStatus;
        model.GroupFilter = trimmedGroup;
        model.PageSize = DefaultPageSize;

        if (id.HasValue)
        {
            model.SelectedElection = model.Elections.SingleOrDefault(e => e.Id == id.Value);
            model.CurrentPage = Math.Max(1, page);
            return model.SelectedElection == null ? NotFound() : View(model);
        }

        var query = db.Elections.AsNoTracking()
            .Include(e => e.Positions)
            .ThenInclude(p => p.Candidates)
            .AsQueryable();

        if (!string.IsNullOrEmpty(trimmedQuery))
        {
            query = query.Where(e =>
                e.Name.Contains(trimmedQuery)
                || (e.RequiredGroup != null && e.RequiredGroup.Contains(trimmedQuery)));
        }

        if (!string.IsNullOrEmpty(trimmedStatus))
        {
            query = query.Where(e => e.Status == trimmedStatus);
        }

        if (string.Equals(trimmedGroup, "__none__", StringComparison.Ordinal))
        {
            query = query.Where(e => e.RequiredGroup == null);
        }
        else if (!string.IsNullOrEmpty(trimmedGroup))
        {
            query = query.Where(e => e.RequiredGroup == trimmedGroup);
        }

        var ordered = query.OrderByDescending(e => e.StartsAtUtc).ThenByDescending(e => e.Id);
        var total = await ordered.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)total / DefaultPageSize));
        var currentPage = Math.Clamp(page, 1, totalPages);

        model.PagedElections = await ordered
            .Skip((currentPage - 1) * DefaultPageSize)
            .Take(DefaultPageSize)
            .ToListAsync();
        model.TotalItems = total;
        model.CurrentPage = currentPage;

        return View(model);
    }

    public async Task<IActionResult> Candidates(int? id, string? q = null, int? positionId = null, string? status = null, int page = 1)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        var model = await DashboardViewModel.LoadAsync(db, true);
        var trimmedQuery = CleanFilter(q);
        var trimmedStatus = CleanStatus(status);

        model.SearchQuery = trimmedQuery;
        model.PositionFilter = positionId;
        model.StatusFilter = trimmedStatus;
        model.PageSize = DefaultPageSize;

        if (id.HasValue)
        {
            model.SelectedElection = model.Elections.SingleOrDefault(e => e.Id == id.Value);
            if (model.SelectedElection == null) return NotFound();

            var query = db.Candidates.AsNoTracking()
                .Include(c => c.Position)
                .ThenInclude(p => p.Election)
                .Where(c => c.Position.ElectionId == id.Value);

            if (!string.IsNullOrEmpty(trimmedQuery))
            {
                query = query.Where(c =>
                    c.Name.Contains(trimmedQuery)
                    || c.Party.Contains(trimmedQuery)
                    || c.Platform.Contains(trimmedQuery)
                    || c.Position.Name.Contains(trimmedQuery));
            }

            if (positionId.HasValue)
            {
                query = query.Where(c => c.PositionId == positionId.Value);
            }

            var ordered = query.OrderBy(c => c.PositionId).ThenBy(c => c.Id);
            var total = await ordered.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling((double)total / DefaultPageSize));
            var currentPage = Math.Clamp(page, 1, totalPages);

            model.PagedCandidates = await ordered
                .Skip((currentPage - 1) * DefaultPageSize)
                .Take(DefaultPageSize)
                .ToListAsync();
            model.TotalItems = total;
            model.CurrentPage = currentPage;
            return View(model);
        }

        var allQuery = db.Candidates.AsNoTracking()
            .Include(c => c.Position)
            .ThenInclude(p => p.Election)
            .AsQueryable();

        if (!string.IsNullOrEmpty(trimmedQuery))
        {
            allQuery = allQuery.Where(c =>
                c.Name.Contains(trimmedQuery)
                || c.Party.Contains(trimmedQuery)
                || c.Platform.Contains(trimmedQuery)
                || c.Position.Name.Contains(trimmedQuery)
                || c.Position.Election.Name.Contains(trimmedQuery));
        }

        if (!string.IsNullOrEmpty(trimmedStatus))
        {
            allQuery = allQuery.Where(c => c.Position.Election.Status == trimmedStatus);
        }

        var orderedAll = allQuery
            .OrderByDescending(c => c.Position.ElectionId)
            .ThenBy(c => c.PositionId)
            .ThenBy(c => c.Id);
        var totalAll = await orderedAll.CountAsync();
        var totalPagesAll = Math.Max(1, (int)Math.Ceiling((double)totalAll / DefaultPageSize));
        var currentPageAll = Math.Clamp(page, 1, totalPagesAll);

        model.PagedCandidates = await orderedAll
            .Skip((currentPageAll - 1) * DefaultPageSize)
            .Take(DefaultPageSize)
            .ToListAsync();
        model.TotalItems = totalAll;
        model.CurrentPage = currentPageAll;

        return View(model);
    }

    public async Task<IActionResult> Positions(int? id)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        return await ElectionPage(id);
    }

    public async Task<IActionResult> Monitoring()
    {
        if (!await IsCurrentAdmin()) return Forbid();
        return View(await DashboardViewModel.LoadAsync(db, true));
    }

    public async Task<IActionResult> Results()
    {
        if (!await IsCurrentAdmin()) return Forbid();
        return View(await DashboardViewModel.LoadAsync(db, true));
    }

    [HttpGet]
    public async Task<IActionResult> Report(int id)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        var model = await DashboardViewModel.LoadAsync(db, true);
        model.SelectedElection = model.Elections.SingleOrDefault(e => e.Id == id);
        if (model.SelectedElection == null) return NotFound();
        return View(model);
    }

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
        && await db.UserAccounts.AsNoTracking().AnyAsync(u => u.Id == id && u.Role == "Admin" && u.EmailVerifiedAtUtc != null);

    private static string? CleanVotingGroup(string? group) => string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    private static string? CleanFilter(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? CleanStatus(string? status) => status is "Draft" or "Open" or "Closed" ? status : null;

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
        if (!await IsCurrentAdmin()) return Forbid();
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
            election.Positions.AddRange(Election.StandardPositions.Select(posName => new Position { Name = posName }));
        db.Elections.Add(election);
        await db.SaveChangesAsync();
        TempData["Success"] = "Election created as a draft.";
        return RedirectToAction(nameof(Elections), new { id = election.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSchedule(int id, DateTime? startsAtManila, DateTime? endsAtManila)
    {
        if (!await IsCurrentAdmin()) return Forbid();
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
    public async Task<IActionResult> AssignVotingGroup(int id, string? votingGroup, string? q = null, string? group = null, int? page = null)
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
        return RedirectToAction(nameof(Users), new
        {
            q = CleanFilter(q),
            group = CleanFilter(group),
            page = page is > 1 ? page : null
        });
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
        if (!await IsCurrentAdmin()) return Forbid();
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
    public async Task<IActionResult> DeleteElection(int id, string? q = null, string? status = null, string? group = null, int? page = null)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var photoFiles = await db.Candidates.AsNoTracking()
                .Where(c => c.Position.ElectionId == id && c.PhotoFileName != null)
                .Select(c => c.PhotoFileName!)
                .ToListAsync();

            var deleted = await db.Elections
                .Where(e => e.Id == id
                    && e.Status == "Draft"
                    && !db.Ballots.Any(b => b.ElectionId == e.Id)
                    && !db.BallotSelections.Any(s => s.Position.ElectionId == e.Id))
                .ExecuteDeleteAsync();

            if (deleted != 1)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "Only draft elections with no submitted ballots can be deleted.";
                return RedirectToAction(nameof(Elections), new { id });
            }

            await transaction.CommitAsync();
            foreach (var fileName in photoFiles)
            {
                photos.TryDeleteFile(fileName);
            }

            TempData["Success"] = "Draft election deleted.";
            return RedirectToAction(nameof(Elections), new
            {
                q = CleanFilter(q),
                status = CleanStatus(status),
                group = CleanFilter(group),
                page = page is > 1 ? page : null
            });
        }
        catch (SqlException ex) when (ex.Number is 547 or 1205)
        {
            TempData["Error"] = "Only draft elections with no submitted ballots can be deleted.";
            return RedirectToAction(nameof(Elections), new { id });
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPosition(int electionId, string? name, bool addStandard = false,
        int minSelections = 1, int? maxSelections = null)
    {
        if (!await IsCurrentAdmin()) return Forbid();
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
    public async Task<IActionResult> AddCandidate(int positionId, string? name, string? party, string? platform,
        int? electionId, IFormFile? photo = null, string? q = null, int? filterPositionId = null, int? page = null)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        name = name?.Trim(); party = party?.Trim() ?? ""; platform = platform?.Trim() ?? "";

        string? uploadedFileName = null;
        string? uploadedContentType = null;
        if (HasUploadedFile(photo))
        {
            var upload = await photos.ValidateAndSaveAsync(photo!);
            if (!upload.Success)
            {
                TempData["Error"] = upload.ErrorMessage;
                return RedirectToAction(nameof(Candidates), new
                {
                    id = electionId,
                    q = CleanFilter(q),
                    positionId = filterPositionId,
                    page = page is > 1 ? page : null
                });
            }
            uploadedFileName = upload.FileName;
            uploadedContentType = upload.ContentType;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var position = await db.Positions.Include(p => p.Election).SingleOrDefaultAsync(p => p.Id == positionId);
        if (!ModelState.IsValid || position?.Election.Status != "Draft" || (electionId.HasValue && position.ElectionId != electionId)
            || string.IsNullOrEmpty(name) || name.Length > 100 || party.Length > 100 || platform.Length > 2000)
        {
            await transaction.RollbackAsync();
            photos.TryDeleteFile(uploadedFileName);
            TempData["Error"] = "Enter a valid candidate and choose a position in the selected draft election.";
        }
        else
        {
            db.Candidates.Add(new Candidate
            {
                PositionId = positionId,
                Name = name,
                Party = party,
                Platform = platform,
                PhotoFileName = uploadedFileName,
                PhotoContentType = uploadedContentType
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["Success"] = "Candidate added.";
        }

        return RedirectToAction(nameof(Candidates), new
        {
            id = electionId ?? position?.ElectionId,
            q = CleanFilter(q),
            positionId = filterPositionId,
            page = page is > 1 ? page : null
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCandidate(int id, int electionId, int positionId, string? name,
        string? party, string? platform, IFormFile? photo = null, bool removePhoto = false,
        string? q = null, int? filterPositionId = null, int? page = null)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        name = name?.Trim(); party = party?.Trim() ?? ""; platform = platform?.Trim() ?? "";

        if (!ModelState.IsValid || string.IsNullOrEmpty(name) || name.Length > 100 || party.Length > 100 || platform.Length > 2000)
        {
            TempData["Error"] = "Enter a valid candidate name, affiliation, and platform.";
            return RedirectToAction(nameof(Candidates), new
            {
                id = electionId,
                q = CleanFilter(q),
                positionId = filterPositionId,
                page = page is > 1 ? page : null
            });
        }

        string? newFileName = null;
        string? newContentType = null;
        if (HasUploadedFile(photo))
        {
            var upload = await photos.ValidateAndSaveAsync(photo!);
            if (!upload.Success)
            {
                TempData["Error"] = upload.ErrorMessage;
                return RedirectToAction(nameof(Candidates), new
                {
                    id = electionId,
                    q = CleanFilter(q),
                    positionId = filterPositionId,
                    page = page is > 1 ? page : null
                });
            }
            newFileName = upload.FileName;
            newContentType = upload.ContentType;
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var existing = await db.Candidates.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new
                {
                    c.Id,
                    c.PositionId,
                    c.PhotoFileName,
                    c.PhotoContentType,
                    ElectionId = c.Position.ElectionId,
                    ElectionStatus = c.Position.Election.Status
                })
                .SingleOrDefaultAsync();

            var validTargetPosition = await db.Positions.AsNoTracking()
                .AnyAsync(p => p.Id == positionId && p.ElectionId == electionId);

            if (existing == null
                || existing.ElectionId != electionId
                || existing.ElectionStatus != "Draft"
                || !validTargetPosition
                || await db.Ballots.AnyAsync(b => b.ElectionId == electionId)
                || await db.BallotSelections.AnyAsync(s => s.CandidateId == id))
            {
                await transaction.RollbackAsync();
                photos.TryDeleteFile(newFileName);
                TempData["Error"] = "Candidates can only be edited or moved in a draft election with no ballots.";
                return RedirectToAction(nameof(Candidates), new
                {
                    id = electionId,
                    q = CleanFilter(q),
                    positionId = filterPositionId,
                    page = page is > 1 ? page : null
                });
            }

            var finalFileName = newFileName ?? (removePhoto ? null : existing.PhotoFileName);
            var finalContentType = newContentType ?? (removePhoto ? null : existing.PhotoContentType);

            // Candidate.PositionId participates in the composite alternate key (Id, PositionId) used by BallotSelections.
            // Update directly in SQL after verifying draft/no-ballot invariants in a Serializable transaction.
            var updated = await db.Candidates
                .Where(c => c.Id == id
                    && c.Position.ElectionId == electionId
                    && c.Position.Election.Status == "Draft"
                    && !db.Ballots.Any(b => b.ElectionId == electionId)
                    && !db.BallotSelections.Any(s => s.CandidateId == c.Id))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.PositionId, positionId)
                    .SetProperty(c => c.Name, name)
                    .SetProperty(c => c.Party, party)
                    .SetProperty(c => c.Platform, platform)
                    .SetProperty(c => c.PhotoFileName, finalFileName)
                    .SetProperty(c => c.PhotoContentType, finalContentType));

            if (updated != 1)
            {
                await transaction.RollbackAsync();
                photos.TryDeleteFile(newFileName);
                TempData["Error"] = "Candidates can only be edited or moved in a draft election with no ballots.";
            }
            else
            {
                await transaction.CommitAsync();
                if ((newFileName != null || removePhoto) && !string.IsNullOrEmpty(existing.PhotoFileName))
                {
                    photos.TryDeleteFile(existing.PhotoFileName);
                }
                TempData["Success"] = "Candidate updated.";
            }
        }
        catch (SqlException ex) when (ex.Number is 547 or 1205)
        {
            photos.TryDeleteFile(newFileName);
            TempData["Error"] = "Candidates can only be edited or moved in a draft election with no ballots.";
        }

        return RedirectToAction(nameof(Candidates), new
        {
            id = electionId,
            q = CleanFilter(q),
            positionId = filterPositionId,
            page = page is > 1 ? page : null
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCandidate(int id, int electionId,
        string? q = null, int? filterPositionId = null, int? page = null)
    {
        if (!await IsCurrentAdmin()) return Forbid();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var existingPhoto = await db.Candidates.AsNoTracking()
                .Where(c => c.Id == id && c.Position.ElectionId == electionId)
                .Select(c => c.PhotoFileName)
                .SingleOrDefaultAsync();

            var deleted = await db.Candidates
                .Where(c => c.Id == id
                    && c.Position.ElectionId == electionId
                    && c.Position.Election.Status == "Draft"
                    && !db.Ballots.Any(b => b.ElectionId == electionId)
                    && !db.BallotSelections.Any(s => s.CandidateId == c.Id))
                .ExecuteDeleteAsync();

            if (deleted != 1)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "Only candidates in a draft election with no ballots can be deleted.";
            }
            else
            {
                await transaction.CommitAsync();
                photos.TryDeleteFile(existingPhoto);
                TempData["Success"] = "Candidate deleted.";
            }
        }
        catch (SqlException ex) when (ex.Number is 547 or 1205)
        {
            TempData["Error"] = "Only candidates in a draft election with no ballots can be deleted.";
        }

        return RedirectToAction(nameof(Candidates), new
        {
            id = electionId,
            q = CleanFilter(q),
            positionId = filterPositionId,
            page = page is > 1 ? page : null
        });
    }

    private static bool HasUploadedFile(IFormFile? file) =>
        file != null && (file.Length > 0 || !string.IsNullOrEmpty(file.FileName));
}
