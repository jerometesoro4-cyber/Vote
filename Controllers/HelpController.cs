using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ACMCLVotingMVC.Controllers
{
    [AllowAnonymous]
    public class HelpController : Controller
    {
        public IActionResult About() => View();

        public IActionResult HowToVote() => View();

        public IActionResult Privacy() => View();
    }
}
