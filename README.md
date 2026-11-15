# ACMCL Voting Portal

A C# **ASP.NET Core MVC** application for student elections. The UI uses server-rendered Razor views, HTML, CSS, and a little JavaScript. Data is stored in **Microsoft SQL Server** through Entity Framework Core. Node.js is not required to run the application.

## Requirements

- Windows with the **.NET 10 SDK** (`dotnet --version` should start with `10.`).
- A running **SQL Server** instance. SQL Server Express, Developer, or an existing compatible SQL Server instance can be used. SQL Server Management Studio is optional.
- Git to clone the repository, and internet access for the first NuGet package restore.
- A Windows account permitted to connect to SQL Server and create the application database for the first migration.

Open PowerShell in the project directory, the folder containing `voting.csproj`.

## 1. Get the source

```powershell
git clone https://github.com/jerometesoro4-cyber/Vote.git
cd Vote
dotnet restore
dotnet build
```

## 2. Configure SQL Server

The checked-in `appsettings.json` defaults to:

```text
Server=localhost;Database=ACMCLVoting;Integrated Security=True;TrustServerCertificate=True
```

This uses your Windows account against the default local SQL Server instance. No SQL username or password is included in the repository.

If you use SQL Server Express, override the connection using **user secrets**, rather than committing machine-specific settings:

```powershell
dotnet user-secrets set "ConnectionStrings:VotingDb" "Server=.\SQLEXPRESS;Database=ACMCLVoting;Integrated Security=True;TrustServerCertificate=True"
```

Use the server/instance name shown in your own SQL Server installation. If SQL Server authentication is required, store the connection string in user secrets or the deployment environment, never in a committed file. `TrustServerCertificate=True` is intended for local development; use a trusted certificate for hosting.

## 3. Create the database

Set Development mode so the app reads your local user secrets, then apply its migrations:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile -- --migrate
```

This creates `ACMCLVoting` if needed and applies pending migrations, then exits. It does not start the website. Re-running the command applies only pending migrations. The schema includes accounts, elections, positions, candidates, ballots, and ballot selections.

Back up an existing database before applying future migrations. Do not remove or roll back an applied migration to troubleshoot a build. Existing PostgreSQL/Supabase records are not imported into this SQL Server database.

## 4. Configure administrator registration

Choose a private invitation code and replace the placeholder below:

```powershell
dotnet user-secrets set "AdminAccount:InvitationCode" "REPLACE-WITH-YOUR-OWN-LONG-RANDOM-CODE"
```

Administrator registration is unavailable until this setting is configured. Share the code only with intended administrators. User secrets are local to your Windows account and this project; they are not included when the repository is cloned on another computer.

## 5. Start the app

In the same PowerShell window:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile -- --urls http://127.0.0.1:5189
```

Open [http://127.0.0.1:5189](http://127.0.0.1:5189). Keep the terminal running while using the website.

Alternatively, `dotnet run --launch-profile http` uses the profile in `Properties/launchSettings.json`, which defaults to `http://localhost:5072`. Use the URL printed by the terminal.

To stop localhost, press **Ctrl+C** in the terminal running the app. Stopping the app preserves the source and database. Start it again with the same run command.

### Visual Studio

Open `voting.csproj` in a Visual Studio installation that supports .NET 10 and has the ASP.NET/web development workload. Configure the SQL Server connection and administrator invitation as above, apply migrations, then run the HTTP profile. Visual Studio Code is also usable with the .NET SDK and its terminal.

## Accounts

There is **one sign-in page** for both roles. On successful sign-in, the account's stored role determines its dashboard.

1. Click **Create an account**.
2. Choose **Student** or **Administrator** at the top of registration.
3. Enter your name, email, and matching passwords of 12–256 characters.
4. Students may optionally enter a year level. Administrators must supply the configured invitation code.
5. Submit the form. Successful registration signs you in.

Fresh installations contain no seeded accounts or default passwords. Demo accounts created on a particular developer's machine are not included in Git.

## Election workflow

As an administrator:

1. Create a **draft election** with a name and UTC start/end times.
2. Add its **positions**.
3. Add at least one **candidate** for each position.
4. Change the election from **Draft** to **Open**. Voting is available only within the configured period.
5. Monitor ballot counts and tallies from the dashboard.
6. Change the election from **Open** to **Closed** to publish results.

As a student, open an available election, review the candidates, choose one candidate for every position, and submit the final ballot. Each student account can submit **one ballot per election**. The history page shows successful submissions.

Election content is locked after opening. Closed elections cannot be reopened. Public/student results display only closed elections. All schedule inputs and timestamps are **UTC**; Philippine time is UTC+8.

## Project structure

| Folder / file | Purpose |
| --- | --- |
| `Controllers/` | MVC request handlers, authentication, election management and voting |
| `Models/` | Account, election and ballot entities plus form/dashboard models |
| `Views/` | Razor pages rendered by MVC, shared layouts and partials |
| `Data/VotingDbContext.cs` | Entity Framework Core SQL Server model and constraints |
| `Migrations/` | Database migration history |
| `wwwroot/` | CSS, local scripts, images and bundled browser libraries |
| `Program.cs` | Services, cookies, authorization, routing and migration command |
| `appsettings.json` | Default local SQL Server configuration |
| `tests/` | Optional local HTTP smoke checks |

## Checks

```powershell
dotnet build
```

The build compiles C# and Razor. Optional HTTP checks require Python and the `requests` package; they are not required to run the C# app:

```powershell
python -m pip install requests
$env:VOTE_TEST_URL = "http://127.0.0.1:5189"
$env:VOTE_TEST_ADMIN_EMAIL = "your-admin-email"
$env:VOTE_TEST_ADMIN_PASSWORD = "your-admin-password"
$env:VOTE_TEST_STUDENT_EMAIL = "your-student-email"
$env:VOTE_TEST_STUDENT_PASSWORD = "your-student-password"
python tests/auth_smoke.py
python tests/registration_smoke.py
```

`auth_smoke.py` uses existing accounts to check both roles, invalid login, CSRF, logout, and redirects. `registration_smoke.py` checks the shared registration form and rejects invalid submissions without creating accounts.

`tests/smoke.py` additionally requires SQLCMD and local Windows authentication to the default `ACMCLVoting` database. It creates a uniquely named test election, exercises the voting workflow, and removes only its fixture. Run it only on a local development database; it does not automatically follow an overridden connection string.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| `dotnet` is missing or SDK version is wrong | Install the .NET 10 SDK and reopen the terminal. |
| Cannot connect to SQL Server | Confirm the SQL Server service is running and the instance name is correct. SQL Server Express commonly uses `.\SQLEXPRESS`. |
| Login failed for Windows user | Grant that Windows account access through SQL Server; initial setup also needs database-creation permission. |
| Tables are missing | Apply migrations using the same Development environment and connection settings as the website. |
| Administrator registration is not configured | Set `AdminAccount:InvitationCode` in this project's user secrets, then restart the app in Development mode. |
| Address/port already in use | Stop your previous run with Ctrl+C, or choose another port in `--urls`. |
| Browser cannot open localhost | Keep the `dotnet run` terminal open and use its printed URL. |
| Build cannot overwrite `voting.exe` | Stop the running app before rebuilding. |
| Changed settings have no effect | Restart the app; confirm it is using the intended environment and user-secret settings. |

## Current limits

School email ownership and voter eligibility are not verified. Password recovery, election editing/deletion, candidate photographs, QR generation, and production hosting are not implemented. Ballots are linked to accounts in the database; this is not an anonymous-election system. The app has not been audited or load tested for use in a real election.

Local databases, invitations, test credentials, screenshots, and build outputs should not be committed. Previously exposed credentials in older Git history require rotation; removing them from current files does not remove historical copies.
