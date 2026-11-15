using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.RegularExpressions;
using voting.Data;
using voting.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var connectionStringCandidates = new (string Key, string? Value)[]
{
    ("ConnectionStrings:VotingDb", builder.Configuration.GetConnectionString("VotingDb")),
    ("ConnectionStrings:Voting and Tally", builder.Configuration.GetConnectionString("Voting and Tally")),
    ("ConnectionStringL:DefaultConnection", builder.Configuration["ConnectionStringL:DefaultConnection"])
};
var invalidConnectionStrings = new List<(string Key, string Reason)>();
string? connectionString = null;
string? connectionStringKey = null;

foreach (var candidate in connectionStringCandidates)
{
    if (string.IsNullOrWhiteSpace(candidate.Value))
    {
        continue;
    }

    try
    {
        connectionString = NormalizeConnectionString(candidate.Value);
        connectionStringKey = candidate.Key;
        break;
    }
    catch (InvalidOperationException exception)
    {
        invalidConnectionStrings.Add((candidate.Key, exception.Message));
    }
}

if (connectionString is null || connectionStringKey is null)
{
    var invalidSettings = string.Join(
        Environment.NewLine,
        invalidConnectionStrings.Select(setting => $"{setting.Key}: {setting.Reason}"));
    throw new InvalidOperationException(
        "No valid PostgreSQL connection string is configured. Add a valid value to 'ConnectionStrings:VotingDb'." +
        (invalidSettings.Length == 0 ? string.Empty : $"{Environment.NewLine}{invalidSettings}"));
}

var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;

builder.Services.AddDbContext<VotingDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddSingleton<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();

builder.Services
.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/ChooseRole";
        options.AccessDeniedPath = "/Account/ChooseRole";

        options.Cookie.Name = "ACMCL.Admin.Auth";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;
    });

// Require login for all pages by default
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

foreach (var invalidSetting in invalidConnectionStrings)
{
    app.Logger.LogWarning(
        "Skipping invalid PostgreSQL connection setting {ConnectionStringKey}: {Reason}",
        invalidSetting.Key,
        invalidSetting.Reason);
}

if (invalidConnectionStrings.Count > 0)
{
    app.Logger.LogWarning(
        "Using PostgreSQL connection setting {ConnectionStringKey} after skipping invalid settings.",
        connectionStringKey);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();




app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=ChooseRole}/{id?}")
    .WithStaticAssets();


app.Run();

static string NormalizeConnectionString(string configuredConnectionString)
{
    var value = configuredConnectionString.Trim();
    var isPostgresUri = value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    if (isPostgresUri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var databaseUri) ||
            (databaseUri.Scheme != "postgres" && databaseUri.Scheme != "postgresql"))
        {
            throw new InvalidOperationException(
                "The PostgreSQL URL is invalid. Use postgresql://username:password@host:port/database?sslmode=require, and URL-encode special characters in the username or password.");
        }

        return ConvertPostgresUri(databaseUri);
    }

    value = Regex.Replace(
        value,
        @"(?i)(^|;)\s*trust\s+server\s*certificate\s*=",
        "$1Trust Server Certificate=");

    try
    {
        return new NpgsqlConnectionStringBuilder(value).ConnectionString;
    }
    catch (ArgumentException exception)
    {
        throw new InvalidOperationException(
            "The PostgreSQL connection setting is not a valid key/value connection string or PostgreSQL URL. For example: Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=your-password.",
            exception);
    }
}

static string ConvertPostgresUri(Uri databaseUri)
{
    var credentials = databaseUri.UserInfo.Split(':', 2);
    if (credentials.Length != 2)
    {
        throw new InvalidOperationException(
            "The PostgreSQL URI must include a username and password.");
    }

    var database = Uri.UnescapeDataString(databaseUri.AbsolutePath.Trim('/'));
    if (string.IsNullOrWhiteSpace(database))
    {
        throw new InvalidOperationException(
            "The PostgreSQL URI must include a database name.");
    }

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = databaseUri.Host,
        Port = databaseUri.IsDefaultPort ? 5432 : databaseUri.Port,
        Database = database,
        Username = Uri.UnescapeDataString(credentials[0]),
        Password = Uri.UnescapeDataString(credentials[1]),
        SslMode = SslMode.Require
    };

    foreach (var option in databaseUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var keyValue = option.Split('=', 2);
        var key = Uri.UnescapeDataString(keyValue[0]);
        var value = keyValue.Length == 2 ? Uri.UnescapeDataString(keyValue[1]) : string.Empty;

        if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
        {
            var mode = value.Replace("-", string.Empty, StringComparison.Ordinal);
            if (!Enum.TryParse<SslMode>(mode, true, out var sslMode))
            {
                throw new InvalidOperationException("The PostgreSQL URI contains an invalid sslmode.");
            }

            builder.SslMode = sslMode;
        }
        else
        {
            throw new InvalidOperationException(
                $"The PostgreSQL URI contains an unsupported query option '{key}'.");
        }
    }

    return builder.ConnectionString;
}
