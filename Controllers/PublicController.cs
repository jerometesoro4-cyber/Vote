using Microsoft.AspNetCore.Mvc;

namespace ACMCLVotingMVC.Controllers
{
    public class PublicPortalController : Controller
    {
        public IActionResult Schedule() => View();

        public IActionResult Candidates() => View();

        public IActionResult Guidelines() => View();

        public IActionResult Results() => View();

        public IActionResult Announcements() => View();
    }
}