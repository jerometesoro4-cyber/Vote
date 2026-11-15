using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using voting.Data;
using voting.Models;

namespace voting.Controllers;

public class AccountController : Controller
{
    private const string DbUnavailableMessage =
        "The service is temporarily unavailable. Please try again later or contact the system administrator.";

    private readonly IConfiguration _configuration;
    private readonly VotingDbContext _dbContext;
    private readonly IPasswordHasher<UserAccount> _passwordHasher;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IConfiguration configuration,
        VotingDbContext dbContext,
        IPasswordHasher<UserAccount> passwordHasher,
        ILogger<AccountController> logger)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string? email, string? password, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.Email = email;
        if (!string.IsNullOrWhiteSpace(email) && email.Length <= 254 &&
            !string.IsNullOrEmpty(password) && password.Length <= 256)
        {
            try
            {
                var normalizedEmail = NormalizeEmail(email);
                var account = await _dbContext.UserAccounts.SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail);
                if (account is not null && (account.Role == "Admin" || account.Role == "Student"))
                {
                    var result = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
                    if (result != PasswordVerificationResult.Failed)
                    {
                        if (result == PasswordVerificationResult.SuccessRehashNeeded)
                        {
                            account.PasswordHash = _passwordHasher.HashPassword(account, password);
                            await _dbContext.SaveChangesAsync();
                        }
                        await SignInAsync(account);
                        return RedirectAfterSignIn(returnUrl, account.Role, "Index");
                    }
                }
            }
            catch (Exception exception) when (IsDatabaseException(exception))
            {
                _logger.LogError(exception, "Login could not query the user accounts table.");
                ViewBag.Error = DbUnavailableMessage;
                return View("Login");
            }
        }
        ViewBag.Error = "Invalid email or password.";
        return View("Login");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult ChooseRole(string? returnUrl = null) => RedirectToAction(nameof(Login), new { returnUrl });

    [AllowAnonymous, HttpGet]
    public IActionResult AdminLogin(string? returnUrl = null) => RedirectToAction(nameof(Login), new { returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> AdminLogin(string? username, string? password, string? returnUrl = null) => Login(username, password, returnUrl);

    [AllowAnonymous, HttpGet]
    public IActionResult StudentLogin(string? returnUrl = null) => RedirectToAction(nameof(Login), new { returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> StudentLogin(string? email, string? password, string? returnUrl = null) => Login(email, password, returnUrl);

    [AllowAnonymous, HttpGet]
    public IActionResult Register(string? role = null) => View(new RegistrationViewModel
    {
        Role = role == "Admin" ? "Admin" : "Student"
    });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegistrationViewModel model)
    {
        if (model.Role != "Student" && model.Role != "Admin")
            ModelState.AddModelError(nameof(model.Role), "Choose a valid account type.");
        if (model.Role == "Admin")
        {
            var expectedCode = _configuration["AdminAccount:InvitationCode"];
            if (string.IsNullOrWhiteSpace(expectedCode))
                ModelState.AddModelError(string.Empty, "Administrator registration is not configured. Contact the system owner.");
            else if (!InvitationCodeMatches(model.InvitationCode ?? string.Empty, expectedCode))
                ModelState.AddModelError(nameof(model.InvitationCode), "The invitation code is invalid.");
        }
        if (!ModelState.IsValid) return View("Register", model);

        var account = CreateAccount(model.FullName, model.Email, model.Password, model.Role,
            model.Role == "Student" ? model.YearLevel : null);
        var result = await SaveAccountAsync(account);
        if (result == SaveResult.Duplicate)
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
        else if (result == SaveResult.Error)
            ModelState.AddModelError(string.Empty, DbUnavailableMessage);
        if (!ModelState.IsValid) return View("Register", model);

        await SignInAsync(account);
        return RedirectToAction("Index", account.Role);
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult StudentSignup() => RedirectToAction(nameof(Register), new { role = "Student" });

   [AllowAnonymous]
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> StudentSignup(StudentRegistrationViewModel model)
{
    if (!ModelState.IsValid)
    {
        return View(model);
    }

    var account = CreateAccount(
        model.FullName,
        model.Email,
        model.Password,
        "Student",
        model.YearLevel);

    var saveResult = await SaveAccountAsync(account);
    if (saveResult == SaveResult.Duplicate)
    {
        ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
        return View(model);
    }

    if (saveResult == SaveResult.Error)
    {
        ModelState.AddModelError(string.Empty, DbUnavailableMessage);
        return View(model);
    }

    await SignInAsync(account);
    return RedirectToAction("Index", "Student");
}
    [AllowAnonymous]
    [HttpGet]
    public IActionResult AdminSignup()
    {
        return RedirectToAction(nameof(Register), new { role = "Admin" });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdminSignup(AdminRegistrationViewModel model)
    {
        var expectedInvitationCode = _configuration["AdminAccount:InvitationCode"];
        if (string.IsNullOrWhiteSpace(expectedInvitationCode))
        {
            ModelState.AddModelError(
                string.Empty,
                "Administrator registration is not configured. Contact the system owner.");
        }
        else if (!InvitationCodeMatches(model.InvitationCode ?? string.Empty, expectedInvitationCode))
        {
            ModelState.AddModelError(nameof(model.InvitationCode), "The invitation code is invalid.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var account = CreateAccount(
            model.FullName,
            model.Email,
            model.Password,
            "Admin",
            yearLevel: null);

        var saveResult = await SaveAccountAsync(account);
        if (saveResult == SaveResult.Duplicate)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
            return View(model);
        }

        if (saveResult == SaveResult.Error)
        {
            ModelState.AddModelError(string.Empty, DbUnavailableMessage);
            return View(model);
        }

        await SignInAsync(account);
        return RedirectToAction("Index", "Admin");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private UserAccount CreateAccount(
        string fullName,
        string email,
        string password,
        string role,
        string? yearLevel)
    {
        var account = new UserAccount
        {
            FullName = fullName.Trim(),
            Email = email.Trim(),
            NormalizedEmail = NormalizeEmail(email),
            Role = role,
            YearLevel = string.IsNullOrWhiteSpace(yearLevel) ? null : yearLevel.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        account.PasswordHash = _passwordHasher.HashPassword(account, password);
        return account;
    }

    private enum SaveResult
    {
        Success,
        Duplicate,
        Error
    }

    private async Task<SaveResult> SaveAccountAsync(UserAccount account)
    {
        _dbContext.UserAccounts.Add(account);

        try
        {
            await _dbContext.SaveChangesAsync();
            return SaveResult.Success;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _dbContext.Entry(account).State = EntityState.Detached;
            return SaveResult.Duplicate;
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Could not save the new user account.");
            _dbContext.Entry(account).State = EntityState.Detached;
            return SaveResult.Error;
        }
    }

    private static bool IsDatabaseException(Exception exception)
    {
        return exception is SqlException
            or DbUpdateException
            or InvalidOperationException
            or TimeoutException
            or System.Net.Sockets.SocketException
            or System.IO.IOException;
    }

    private async Task SignInAsync(UserAccount account)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.FullName),
            new(ClaimTypes.Email, account.Email),
            new(ClaimTypes.Role, account.Role)
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal);
    }

    private IActionResult RedirectAfterSignIn(
        string? returnUrl,
        string controller,
        string action)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(action, controller);
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }

    private static bool InvitationCodeMatches(string supplied, string expected)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private sealed record EligibleStudent(string FullName, string Email, string YearLevel);
}