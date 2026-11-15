using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using voting.Data;
using voting.Models;

namespace ACMCLVotingMVC.Controllers;

[AllowAnonymous]
public class PublicPortalController(VotingDbContext db) : Controller
{
    public async Task<IActionResult> Schedule() => View(await DashboardViewModel.LoadAsync(db, false));
    public async Task<IActionResult> Candidates() => View(await DashboardViewModel.LoadAsync(db, false));
    public IActionResult Guidelines() => View();
    public async Task<IActionResult> Results() => View(await DashboardViewModel.LoadAsync(db, false));
    public IActionResult Announcements() => View();
}
