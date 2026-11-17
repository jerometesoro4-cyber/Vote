using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using voting.Data;
using voting.Models;
using voting.Services;

namespace voting.Controllers;

public class AccountController : Controller
{
    private const string DbUnavailableMessage =
        "The service is temporarily unavailable. Please try again later or contact the system administrator.";

    private const string GenericRecoveryMessage =
        "If an account matches that email address, a password recovery code has been sent.";

    private const string GenericVerificationResendMessage =
        "If that account requires verification, a new verification code has been sent.";

    private const string ThrottledRequestMessage =
        "Please wait before requesting another code.";

    private static readonly ConcurrentDictionary<string, List<DateTime>> PublicRequestWindows = new();

    private readonly IConfiguration _configuration;
    private readonly VotingDbContext _dbContext;
    private readonly IPasswordHasher<UserAccount> _passwordHasher;
    private readonly IEmailService _emailService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IConfiguration configuration,
        VotingDbContext dbContext,
        IPasswordHasher<UserAccount> passwordHasher,
        IEmailService emailService,
        ILogger<AccountController> logger)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _logger = logger;
    }

    private int ResendCooldownSeconds => Math.Max(0, _configuration.GetValue<int?>("EmailVerification:ResendCooldownSeconds") ?? 30);
    private int MaxSendsPerWindow => Math.Max(1, _configuration.GetValue<int?>("EmailVerification:MaxSendsPerWindow") ?? 5);
    private int WindowMinutes => Math.Max(1, _configuration.GetValue<int?>("EmailVerification:WindowMinutes") ?? 15);
    private int CodeExpiryMinutes => Math.Max(1, _configuration.GetValue<int?>("EmailVerification:CodeExpiryMinutes") ?? 15);
    private int MaxCodeAttempts => Math.Max(1, _configuration.GetValue<int?>("EmailVerification:MaxAttempts") ?? 5);

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = CleanReturnUrl(returnUrl);
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string? email, string? password, string? returnUrl = null)
    {
        var safeReturnUrl = CleanReturnUrl(returnUrl);
        ViewBag.ReturnUrl = safeReturnUrl;
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

                        if (account.EmailVerifiedAtUtc == null)
                        {
                            var issue = await IssueAndSendCodeAsync(account, EmailVerificationCode.PurposeVerifyEmail);
                            if (issue == CodeIssueResult.DeliveryFailed)
                            {
                                TempData["Error"] = "Verify your email before signing in. We could not send the verification email right now; check mail settings or use Resend code.";
                            }
                            else if (issue == CodeIssueResult.Throttled)
                            {
                                TempData["Success"] = "Please verify your email address using the code previously sent, or wait briefly before requesting another.";
                            }
                            else
                            {
                                TempData["Success"] = "Please verify your email address with the 6-digit code sent to your inbox before signing in.";
                            }

                            return RedirectToAction(nameof(VerifyEmail), new { email = account.Email, returnUrl = safeReturnUrl });
                        }

                        await SignInAsync(account);
                        return RedirectAfterSignIn(safeReturnUrl, account.Role, "Index");
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
    public IActionResult ChooseRole(string? returnUrl = null) => RedirectToAction(nameof(Login), new { returnUrl = CleanReturnUrl(returnUrl) });

    [AllowAnonymous, HttpGet]
    public IActionResult AdminLogin(string? returnUrl = null) => RedirectToAction(nameof(Login), new { returnUrl = CleanReturnUrl(returnUrl) });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> AdminLogin(string? username, string? password, string? returnUrl = null) => Login(username, password, returnUrl);

    [AllowAnonymous, HttpGet]
    public IActionResult StudentLogin(string? returnUrl = null) => RedirectToAction(nameof(Login), new { returnUrl = CleanReturnUrl(returnUrl) });

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
        if (!ModelState.IsValid) return View("Register", model);

        var account = CreateAccount(model.FullName, model.Email, model.Password, model.Role,
            model.Role == "Student" ? model.YearLevel : null);
        var result = model.Role == "Admin"
            ? await SaveAdminAccountAsync(account, model.InvitationCode) : await SaveAccountAsync(account);
        if (result == SaveResult.InvalidInvitation)
            ModelState.AddModelError(nameof(model.InvitationCode), "The invitation code is invalid, expired, used, or revoked.");
        else if (result == SaveResult.Duplicate)
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
        else if (result == SaveResult.Error)
            ModelState.AddModelError(string.Empty, DbUnavailableMessage);
        if (!ModelState.IsValid) return View("Register", model);

        var issue = await IssueAndSendCodeAsync(account, EmailVerificationCode.PurposeVerifyEmail);
        if (issue == CodeIssueResult.DeliveryFailed)
        {
            TempData["Error"] = "Your account was created, but we could not send the verification email. Check the email settings or use Resend code below.";
        }
        else
        {
            TempData["Success"] = "Account created. Enter the 6-digit verification code sent to your email to sign in.";
        }

        return RedirectToAction(nameof(VerifyEmail), new { email = account.Email });
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

        var issue = await IssueAndSendCodeAsync(account, EmailVerificationCode.PurposeVerifyEmail);
        if (issue == CodeIssueResult.DeliveryFailed)
        {
            TempData["Error"] = "Your account was created, but we could not send the verification email. Check the email settings or use Resend code below.";
        }
        else
        {
            TempData["Success"] = "Account created. Enter the 6-digit verification code sent to your email to sign in.";
        }

        return RedirectToAction(nameof(VerifyEmail), new { email = account.Email });
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

        var saveResult = await SaveAdminAccountAsync(account, model.InvitationCode);
        if (saveResult == SaveResult.InvalidInvitation)
        {
            ModelState.AddModelError(nameof(model.InvitationCode), "The invitation code is invalid, expired, used, or revoked.");
            return View(model);
        }
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

        var issue = await IssueAndSendCodeAsync(account, EmailVerificationCode.PurposeVerifyEmail);
        if (issue == CodeIssueResult.DeliveryFailed)
        {
            TempData["Error"] = "Your administrator account was created, but we could not send the verification email. Check the email settings or use Resend code below.";
        }
        else
        {
            TempData["Success"] = "Administrator account created. Enter the 6-digit verification code sent to your email to sign in.";
        }

        return RedirectToAction(nameof(VerifyEmail), new { email = account.Email });
    }

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult VerifyEmail(string? email = null, string? returnUrl = null)
    {
        return View(new VerifyEmailViewModel
        {
            Email = email?.Trim() ?? "",
            ReturnUrl = CleanReturnUrl(returnUrl)
        });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model)
    {
        model.ReturnUrl = CleanReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var now = DateTime.UtcNow;
            var normalizedEmail = NormalizeEmail(model.Email);
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var account = await _dbContext.UserAccounts.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
            if (account == null || (account.Role != "Admin" && account.Role != "Student"))
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError(string.Empty, "The verification code is invalid or has expired.");
                return View(model);
            }

            if (account.EmailVerifiedAtUtc != null)
            {
                await transaction.RollbackAsync();
                TempData["Success"] = "Your email address is already verified. Sign in with your password.";
                return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl });
            }

            var activeCode = await _dbContext.EmailVerificationCodes
                .Where(v => v.UserAccountId == account.Id
                    && v.Purpose == EmailVerificationCode.PurposeVerifyEmail
                    && v.ConsumedAtUtc == null
                    && v.RevokedAtUtc == null
                    && v.ExpiresAtUtc > now)
                .OrderByDescending(v => v.CreatedAtUtc)
                .ThenByDescending(v => v.Id)
                .FirstOrDefaultAsync();

            if (activeCode == null || activeCode.AttemptCount >= activeCode.MaxAttempts)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError(string.Empty, "The verification code is invalid or has expired.");
                return View(model);
            }

            if (!EmailVerificationCode.VerifyCode(account.Id, EmailVerificationCode.PurposeVerifyEmail, model.Code, activeCode.CodeHash))
            {
                var nextAttempts = activeCode.AttemptCount + 1;
                activeCode.AttemptCount = nextAttempts;
                if (nextAttempts >= activeCode.MaxAttempts)
                {
                    activeCode.RevokedAtUtc = now;
                }
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                ModelState.AddModelError(string.Empty, "The verification code is invalid or has expired.");
                return View(model);
            }

            var consumed = await _dbContext.EmailVerificationCodes
                .Where(v => v.Id == activeCode.Id
                    && v.ConsumedAtUtc == null
                    && v.RevokedAtUtc == null
                    && v.AttemptCount < v.MaxAttempts
                    && v.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(updates => updates.SetProperty(v => v.ConsumedAtUtc, now));

            if (consumed != 1)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError(string.Empty, "The verification code is invalid or has expired.");
                return View(model);
            }

            await _dbContext.EmailVerificationCodes
                .Where(v => v.UserAccountId == account.Id
                    && v.Purpose == EmailVerificationCode.PurposeVerifyEmail
                    && v.Id != activeCode.Id
                    && v.ConsumedAtUtc == null
                    && v.RevokedAtUtc == null)
                .ExecuteUpdateAsync(updates => updates.SetProperty(v => v.RevokedAtUtc, now));

            account.EmailVerifiedAtUtc = now;
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            await SignInAsync(account);
            TempData["Success"] = "Your email has been verified.";
            return RedirectAfterSignIn(model.ReturnUrl, account.Role, "Index");
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Could not complete email verification.");
            ModelState.AddModelError(string.Empty, "The verification code could not be processed. Please try again.");
            return View(model);
        }
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResendVerification(string? email, string? returnUrl = null)
    {
        var safeReturnUrl = CleanReturnUrl(returnUrl);
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 254)
        {
            TempData["Error"] = "Enter a valid email address.";
            return RedirectToAction(nameof(VerifyEmail), new { email, returnUrl = safeReturnUrl });
        }

        var normalizedEmail = NormalizeEmail(email);
        if (!TryAcquirePublicRequestSlot(EmailVerificationCode.PurposeVerifyEmail, normalizedEmail))
        {
            TempData["Error"] = ThrottledRequestMessage;
            return RedirectToAction(nameof(VerifyEmail), new { email = email.Trim(), returnUrl = safeReturnUrl });
        }

        try
        {
            var account = await _dbContext.UserAccounts.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
            if (account != null && account.EmailVerifiedAtUtc == null)
            {
                var issue = await IssueAndSendCodeAsync(account, EmailVerificationCode.PurposeVerifyEmail);
                if (issue == CodeIssueResult.Throttled)
                {
                    TempData["Error"] = ThrottledRequestMessage;
                    return RedirectToAction(nameof(VerifyEmail), new { email = email.Trim(), returnUrl = safeReturnUrl });
                }

                if (issue == CodeIssueResult.DeliveryFailed)
                {
                    TempData["Error"] = "We could not send the verification email. Check the mail settings and try again.";
                    return RedirectToAction(nameof(VerifyEmail), new { email = email.Trim(), returnUrl = safeReturnUrl });
                }
            }

            TempData["Success"] = GenericVerificationResendMessage;
            return RedirectToAction(nameof(VerifyEmail), new { email = email.Trim(), returnUrl = safeReturnUrl });
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Could not resend verification code.");
            TempData["Error"] = DbUnavailableMessage;
            return RedirectToAction(nameof(VerifyEmail), new { email = email.Trim(), returnUrl = safeReturnUrl });
        }
    }

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ForgotPassword(string? email = null, string? returnUrl = null)
    {
        ViewBag.Email = email?.Trim();
        ViewBag.ReturnUrl = CleanReturnUrl(returnUrl);
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ForgotPassword(string? email, string? returnUrl = null, bool stayOnReset = false)
    {
        var safeReturnUrl = CleanReturnUrl(returnUrl);
        ViewBag.Email = email?.Trim();
        ViewBag.ReturnUrl = safeReturnUrl;

        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 254)
        {
            ViewBag.Error = "Enter your registered email address.";
            return stayOnReset
                ? RedirectToAction(nameof(ResetPassword), new { email, returnUrl = safeReturnUrl })
                : View("ForgotPassword");
        }

        var trimmedEmail = email.Trim();
        var normalizedEmail = NormalizeEmail(trimmedEmail);
        if (!TryAcquirePublicRequestSlot(EmailVerificationCode.PurposeResetPassword, normalizedEmail))
        {
            TempData["Error"] = ThrottledRequestMessage;
            return RedirectToAction(nameof(ResetPassword), new { email = trimmedEmail, returnUrl = safeReturnUrl });
        }

        try
        {
            var account = await _dbContext.UserAccounts.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
            if (account != null)
            {
                var issue = await IssueAndSendCodeAsync(account, EmailVerificationCode.PurposeResetPassword);
                if (issue == CodeIssueResult.DeliveryFailed)
                {
                    if (stayOnReset)
                    {
                        TempData["Error"] = "We could not send the recovery email right now. Check the mail settings and try again.";
                        return RedirectToAction(nameof(ResetPassword), new { email = trimmedEmail, returnUrl = safeReturnUrl });
                    }

                    ViewBag.Error = "We could not send the recovery email right now. Check the mail configuration or try again.";
                    return View("ForgotPassword");
                }

                if (issue == CodeIssueResult.Throttled)
                {
                    TempData["Error"] = ThrottledRequestMessage;
                    return RedirectToAction(nameof(ResetPassword), new { email = trimmedEmail, returnUrl = safeReturnUrl });
                }
            }

            TempData["Success"] = GenericRecoveryMessage;
            return RedirectToAction(nameof(ResetPassword), new { email = trimmedEmail, returnUrl = safeReturnUrl });
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Could not process password recovery request.");
            ViewBag.Error = DbUnavailableMessage;
            return View("ForgotPassword");
        }
    }

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ResetPassword(string? email = null, string? returnUrl = null)
    {
        return View(new ResetPasswordViewModel
        {
            Email = email?.Trim() ?? "",
            ReturnUrl = CleanReturnUrl(returnUrl)
        });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        model.ReturnUrl = CleanReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var now = DateTime.UtcNow;
            var normalizedEmail = NormalizeEmail(model.Email);
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var account = await _dbContext.UserAccounts.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
            if (account == null || (account.Role != "Admin" && account.Role != "Student"))
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError(string.Empty, "The recovery code is invalid or has expired.");
                return View(model);
            }

            var activeCode = await _dbContext.EmailVerificationCodes
                .Where(v => v.UserAccountId == account.Id
                    && v.Purpose == EmailVerificationCode.PurposeResetPassword
                    && v.ConsumedAtUtc == null
                    && v.RevokedAtUtc == null
                    && v.ExpiresAtUtc > now)
                .OrderByDescending(v => v.CreatedAtUtc)
                .ThenByDescending(v => v.Id)
                .FirstOrDefaultAsync();

            if (activeCode == null || activeCode.AttemptCount >= activeCode.MaxAttempts)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError(string.Empty, "The recovery code is invalid or has expired.");
                return View(model);
            }

            if (!EmailVerificationCode.VerifyCode(account.Id, EmailVerificationCode.PurposeResetPassword, model.Code, activeCode.CodeHash))
            {
                var nextAttempts = activeCode.AttemptCount + 1;
                activeCode.AttemptCount = nextAttempts;
                if (nextAttempts >= activeCode.MaxAttempts)
                {
                    activeCode.RevokedAtUtc = now;
                }
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                ModelState.AddModelError(string.Empty, "The recovery code is invalid or has expired.");
                return View(model);
            }

            var consumed = await _dbContext.EmailVerificationCodes
                .Where(v => v.Id == activeCode.Id
                    && v.ConsumedAtUtc == null
                    && v.RevokedAtUtc == null
                    && v.AttemptCount < v.MaxAttempts
                    && v.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(updates => updates.SetProperty(v => v.ConsumedAtUtc, now));

            if (consumed != 1)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError(string.Empty, "The recovery code is invalid or has expired.");
                return View(model);
            }

            await _dbContext.EmailVerificationCodes
                .Where(v => v.UserAccountId == account.Id
                    && v.Purpose == EmailVerificationCode.PurposeResetPassword
                    && v.Id != activeCode.Id
                    && v.ConsumedAtUtc == null
                    && v.RevokedAtUtc == null)
                .ExecuteUpdateAsync(updates => updates.SetProperty(v => v.RevokedAtUtc, now));

            account.PasswordHash = _passwordHasher.HashPassword(account, model.Password);
            account.SessionVersion += 1;
            account.EmailVerifiedAtUtc ??= now;
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Success"] = "Your password has been updated. Sign in with your new password.";
            return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl });
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Could not complete password reset.");
            ModelState.AddModelError(string.Empty, "The recovery code could not be processed. Please try again.");
            return View(model);
        }
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private enum CodeIssueResult
    {
        Sent,
        Throttled,
        DeliveryFailed
    }

    private bool TryAcquirePublicRequestSlot(string purpose, string normalizedEmail)
    {
        var now = DateTime.UtcNow;
        var cooldown = ResendCooldownSeconds;
        var maxSends = MaxSendsPerWindow;
        var windowStart = now.AddMinutes(-WindowMinutes);
        var key = $"{purpose}:{normalizedEmail}";
        var timestamps = PublicRequestWindows.GetOrAdd(key, _ => []);

        lock (timestamps)
        {
            timestamps.RemoveAll(t => t < windowStart);
            if (cooldown > 0 && timestamps.Count > 0 && (now - timestamps[^1]).TotalSeconds < cooldown)
            {
                return false;
            }
            if (timestamps.Count >= maxSends)
            {
                return false;
            }
            timestamps.Add(now);
            return true;
        }
    }

    private async Task<CodeIssueResult> IssueAndSendCodeAsync(UserAccount account, string purpose)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-WindowMinutes);

        var recentTimestamps = await _dbContext.EmailVerificationCodes
            .AsNoTracking()
            .Where(v => v.UserAccountId == account.Id && v.Purpose == purpose && v.CreatedAtUtc >= windowStart)
            .OrderByDescending(v => v.CreatedAtUtc)
            .Select(v => v.CreatedAtUtc)
            .ToListAsync();

        if (recentTimestamps.Count >= MaxSendsPerWindow)
        {
            return CodeIssueResult.Throttled;
        }

        if (ResendCooldownSeconds > 0 && recentTimestamps.Count > 0
            && (now - recentTimestamps[0]).TotalSeconds < ResendCooldownSeconds)
        {
            return CodeIssueResult.Throttled;
        }

        await _dbContext.EmailVerificationCodes
            .Where(v => v.UserAccountId == account.Id
                && v.Purpose == purpose
                && v.ConsumedAtUtc == null
                && v.RevokedAtUtc == null)
            .ExecuteUpdateAsync(updates => updates.SetProperty(v => v.RevokedAtUtc, now));

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var record = new EmailVerificationCode
        {
            UserAccountId = account.Id,
            Purpose = purpose,
            CodeHash = EmailVerificationCode.HashCode(account.Id, purpose, code),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(CodeExpiryMinutes),
            MaxAttempts = MaxCodeAttempts
        };

        _dbContext.EmailVerificationCodes.Add(record);
        await _dbContext.SaveChangesAsync();

        try
        {
            await _emailService.SendVerificationCodeAsync(account.Email, account.FullName, code, purpose);
            return CodeIssueResult.Sent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not deliver {Purpose} email.", purpose);
            _dbContext.EmailVerificationCodes.Remove(record);
            await _dbContext.SaveChangesAsync();
            return CodeIssueResult.DeliveryFailed;
        }
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
            CreatedAtUtc = DateTime.UtcNow,
            EmailVerifiedAtUtc = null,
            SessionVersion = 1
        };
        account.PasswordHash = _passwordHasher.HashPassword(account, password);
        return account;
    }

    private enum SaveResult
    {
        Success,
        InvalidInvitation,
        Duplicate,
        Error
    }

    private async Task<SaveResult> SaveAdminAccountAsync(UserAccount account, string? code)
    {
        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var hasAdmin = await _dbContext.UserAccounts.AnyAsync(u => u.Role == "Admin");
            var bootstrapCode = _configuration["AdminAccount:InvitationCode"];
            var bootstrap = !hasAdmin && !string.IsNullOrWhiteSpace(bootstrapCode)
                && InvitationCodeMatches(code ?? string.Empty, bootstrapCode);
            if (!bootstrap)
            {
                var now = DateTime.UtcNow;
                var hash = AdminInvitation.HashCode(code ?? string.Empty);
                var consumed = await _dbContext.AdminInvitations
                    .Where(i => i.CodeHash == hash && i.UsedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now)
                    .ExecuteUpdateAsync(updates => updates.SetProperty(i => i.UsedAtUtc, now));
                if (consumed != 1) return SaveResult.InvalidInvitation;
            }
            var result = await SaveAccountAsync(account);
            if (result != SaveResult.Success) return result;
            await transaction.CommitAsync();
            return SaveResult.Success;
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Could not register an administrator.");
            return SaveResult.Error;
        }
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
            new(ClaimTypes.Role, account.Role),
            new("session_version", account.SessionVersion.ToString())
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal);
    }

    private string? CleanReturnUrl(string? returnUrl)
    {
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
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
}
