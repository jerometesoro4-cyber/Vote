using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace voting.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Users()
        {
            return View();
        }

        public IActionResult Elections()
        {
            return View();
        }

        public IActionResult Candidates()
        {
            return View();
        }

        public IActionResult Positions()
        {
            return View();
        }

        public IActionResult Monitoring()
        {
            return View();
        }

        public IActionResult Results()
        {
            return View();
        }

        public IActionResult Settings()
        {
            return View();
        }
    }
}


