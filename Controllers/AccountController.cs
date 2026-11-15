using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    public IActionResult ChooseRole()
    {
        return View();
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AdminLogin(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdminLogin(
        string username,
        string password,
        string? returnUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(username) &&
            !string.IsNullOrEmpty(password) &&
            password.Length <= 256)
        {
            try
            {
                var normalizedEmail = NormalizeEmail(username);
                var account = await _dbContext.UserAccounts.SingleOrDefaultAsync(
                    user => user.NormalizedEmail == normalizedEmail && user.Role == "Admin");

                if (account is not null)
                {
                    var result = _passwordHasher.VerifyHashedPassword(
                        account,
                        account.PasswordHash,
                        password);

                    if (result != PasswordVerificationResult.Failed)
                    {
                        if (result == PasswordVerificationResult.SuccessRehashNeeded)
                        {
                            account.PasswordHash = _passwordHasher.HashPassword(account, password);
                            await _dbContext.SaveChangesAsync();
                        }

                        await SignInAsync(account);
                        return RedirectAfterSignIn(returnUrl, "Admin", "Index");
                    }
                }
            }
            catch (Exception exception) when (IsDatabaseException(exception))
            {
                // Database is down: log it and fall through to the configured admin account.
                _logger.LogError(exception, "Admin login could not query the user accounts table.");
            }

            var configuredUsername = _configuration["AdminAccount:Username"];
            var configuredPassword = _configuration["AdminAccount:Password"];

            if (!string.IsNullOrEmpty(configuredUsername) &&
                !string.IsNullOrEmpty(configuredPassword) &&
                username == configuredUsername &&
                password == configuredPassword)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, username),
                    new(ClaimTypes.Role, "Admin")
                };
                var principal = new ClaimsPrincipal(
                    new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal);

                return RedirectAfterSignIn(returnUrl, "Admin", "Index");
            }
        }

        ViewBag.Error = "Invalid administrator email or password.";
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult StudentLogin(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StudentLogin(
        string email,
        string password,
        string? returnUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(email) &&
            email.Length <= 254 &&
            !string.IsNullOrEmpty(password) &&
            password.Length <= 256)
        {
            try
            {
                var normalizedEmail = NormalizeEmail(email);
                var account = await _dbContext.UserAccounts.SingleOrDefaultAsync(
                    user => user.NormalizedEmail == normalizedEmail && user.Role == "Student");

                if (account is not null)
                {
                    var result = _passwordHasher.VerifyHashedPassword(
                        account,
                        account.PasswordHash,
                        password);

                    if (result != PasswordVerificationResult.Failed)
                    {
                        if (result == PasswordVerificationResult.SuccessRehashNeeded)
                        {
                            account.PasswordHash = _passwordHasher.HashPassword(account, password);
                            await _dbContext.SaveChangesAsync();
                        }

                        await SignInAsync(account);
                        return RedirectAfterSignIn(returnUrl, "Student", "Index");
                    }
                }
            }
            catch (Exception exception) when (IsDatabaseException(exception))
            {
                _logger.LogError(exception, "Student login could not query the user accounts table.");
                ViewBag.Error = DbUnavailableMessage;
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }
        }

        ViewBag.Error = "Invalid student email or password.";
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    
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
        return View(new AdminRegistrationViewModel());
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
        return RedirectToAction(nameof(ChooseRole));
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
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
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
        return exception is NpgsqlException
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