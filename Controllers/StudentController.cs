using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace ACMCLVotingMVC.Controllers
{
    [Authorize(Roles = "Student")]
    public class StudentController : Controller
    {
        public IActionResult Index() => View();

        public IActionResult ActiveElections() => View();

        public IActionResult Vote() => View();

        public IActionResult History() => View();

        public IActionResult Candidates() => View();

        public IActionResult Results() => View();

        public IActionResult QrAccess() => View();
    }
}