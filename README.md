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

## 2. Configure SQL Server and Brevo SMTP

### SQL Server connection

The checked-in `appsettings.json` defaults to:

```text
Server=localhost;Database=ACMCLVoting;Integrated Security=True;TrustServerCertificate=True
```

This uses your Windows account against the default local SQL Server instance. No SQL username or password is included in the repository.

If you use SQL Server Express or custom connection settings, uncomment `ConnectionStrings__VotingDb` in your private `.env` file or set it with **user secrets**, rather than committing machine-specific settings:

```powershell
dotnet user-secrets set "ConnectionStrings:VotingDb" "Server=.\SQLEXPRESS;Database=ACMCLVoting;Integrated Security=True;TrustServerCertificate=True"
```

Use the server/instance name shown in your own SQL Server installation. If SQL Server authentication is required, store the connection string in your private `.env` (which is gitignored), user secrets, or the deployment environment, never in a committed file. `TrustServerCertificate=True` is intended for local development; use a trusted certificate for hosting.

### How outbound email works

`Services/EmailService.cs` uses `System.Net.Mail.SmtpClient` to send 6-digit email verification codes (on registration and first sign-in for existing unverified accounts) and single-use password recovery codes (`ForgotPassword`). It connects directly over SMTP (`smtp-relay.brevo.com:587` with `EnableSsl = true`, i.e., STARTTLS), so no Brevo SDK or REST API key is required.

- Missing SMTP credentials or a missing verified `Email__FromAddress` cause `EmailService` to fail fast with an actionable configuration error before attempting live SMTP delivery.
- Automated and offline local tests can instead use a separate explicit test sink (`Email__PickupDirectory`) in `Development` only.
- Live Brevo delivery awaits configuring your own Brevo account credentials and verified sender in `.env` (no real Brevo credentials are included in this repository).

### Set up Brevo SMTP and `.env`

1. In your Brevo account, create and verify a sender email address (or authenticate your sending domain) under **Senders, Domains & Dedicated IPs**. You will enter this verified email as `Email__FromAddress`.
2. Open **SMTP & API** -> **SMTP** in Brevo, copy the **SMTP Login** field (`Email__SmtpUsername`), and generate a new **SMTP key** (`Email__SmtpPassword`). Do not use your Brevo account login password or a REST API key, and do not substitute the `@smtp-brevo.com` SMTP Login as `Email__FromAddress`.
   - Official Brevo references:
     - [Send transactional emails using Brevo SMTP](https://help.brevo.com/hc/en-us/articles/7924908994450-Send-transactional-emails-using-Brevo-SMTP)
     - [Create and manage your SMTP keys](https://help.brevo.com/hc/en-us/articles/7959631848850-Create-and-manage-your-SMTP-keys)
     - [Which SMTP port should I use (Port 587, 465, or 2525)?](https://help.brevo.com/hc/en-us/articles/10905415650322-Which-SMTP-port-should-I-use-Port-587-465-or-2525)
3. Copy `.env.example` to `.env` and enter your private Brevo values (`.env` is gitignored):

```powershell
Copy-Item .env.example .env
notepad .env
```

Fill in at minimum:
- `Email__SmtpUsername` = your Brevo **SMTP Login**
- `Email__SmtpPassword` = your generated Brevo **SMTP key**
- `Email__FromAddress` = your verified Brevo sender address (leave `Email__SmtpHost=smtp-relay.brevo.com`, `Email__SmtpPort=587`, `Email__UseSsl=true`, and keep `Email__PickupDirectory=` empty)

4. Load `.env` into your PowerShell session. ASP.NET Core does **not** load `.env` files automatically; use the included loader script (existing process environment variables take precedence by default, or pass `-Overwrite` after editing `.env` in the same terminal):

```powershell
. .\scripts\import-dotenv.ps1
```

## 3. Apply the SQL Server migration

With `.env` loaded (or `$env:ASPNETCORE_ENVIRONMENT = "Development"` set), apply pending Entity Framework Core migrations:

```powershell
dotnet run --no-launch-profile -- --migrate
```

This creates `ACMCLVoting` if needed and applies pending migrations, then exits. It does not start the website. Re-running the command applies only pending migrations. The schema includes accounts, elections, positions, candidates, ballots, ballot selections, and email verification codes.

Run this step when upgrading an existing installation to add email ownership verification, password recovery, session version tracking, and candidate photo metadata. Existing accounts retain their password hashes and will be prompted to verify control of their email address with a 6-digit code at their next sign-in. Existing positions keep their recorded ballots and selection limits.

### Upgrading an existing installation

Before running this version against an existing database and installation:

1. **Back up the SQL Server database** before applying the additive migration (`AccountVerificationAndCandidatePhotos`).
2. **Configure Brevo SMTP in `.env` (or environment variables / user secrets)** before users register, verify existing accounts, or reset passwords:
   - `Email__SmtpHost=smtp-relay.brevo.com`
   - `Email__SmtpPort=587`
   - `Email__UseSsl=true` (STARTTLS)
   - `Email__SmtpUsername` (Brevo SMTP Login) and `Email__SmtpPassword` (Brevo SMTP key)
   - `Email__FromAddress` (verified sender address in Brevo) and `Email__FromName` (defaults to `ACMCL Vote`)
   - Keep `Email__PickupDirectory` empty in `.env` and unset in production so live verification/recovery messages are sent rather than saved locally.
3. **Ensure write permissions** for `App_Data/candidate-photos/` (or configure `CandidatePhotos__StoragePath` to a private directory outside `wwwroot`) so administrators can upload and replace candidate photographs.
4. **Load `.env` and apply pending migrations**: `. .\scripts\import-dotenv.ps1; dotnet run --no-launch-profile -- --migrate` (or configure the target environment's connection string).

Back up an existing database before applying future migrations. Do not remove or roll back an applied migration to troubleshoot a build. Existing PostgreSQL/Supabase records are not imported into this SQL Server database.

## 4. Create access for the first administrator

On a fresh database with no administrators, set `AdminAccount__InvitationCode` in your `.env` (and reload with `. .\scripts\import-dotenv.ps1 -Overwrite`) or store it with user secrets:

```powershell
dotnet user-secrets set "AdminAccount:InvitationCode" "REPLACE-WITH-YOUR-OWN-LONG-RANDOM-CODE"
```

Use this code once to register the first administrator. After an administrator exists, this configured bootstrap code is no longer accepted. Additional administrators must use invitations generated from the Account page. User secrets and `.env` are local to your machine and are not included when the repository is cloned on another computer.

## 5. Start the app and test verification / recovery

In the same PowerShell window where you ran `. .\scripts\import-dotenv.ps1`:

```powershell
dotnet run --no-launch-profile -- --urls http://127.0.0.1:5189
```

Open [http://127.0.0.1:5189](http://127.0.0.1:5189). Keep the terminal running while using the website.

To verify Brevo SMTP delivery once your credentials and verified sender are configured:
- Register a test account (or sign in as an existing unverified account) to trigger a 6-digit verification code, or use **Forgot password?** on the sign-in page to request a 6-digit password recovery code.
- If you edit `.env` while the app is running, stop the app with **Ctrl+C**, reload `. .\scripts\import-dotenv.ps1 -Overwrite`, and start the app again.

Alternatively, `dotnet run --launch-profile http` uses the profile in `Properties/launchSettings.json`, which defaults to `http://localhost:5072`. Use the URL printed by the terminal.

To stop localhost, press **Ctrl+C** in the terminal running the app. Stopping the app preserves the source and database. Start it again with the same run command.

### Local `.eml` test sink (automated / offline testing only)

For automated tests or offline local testing in `Development` without sending live Brevo mail, you can route messages to local `.eml` files instead:

1. In `.env`, clear `Email__SmtpHost=` and set `Email__PickupDirectory=App_Data/dev-emails`.
2. Reload `. .\scripts\import-dotenv.ps1 -Overwrite` and restart the app.
3. Request a verification or password recovery code, then open the newest `.eml` file under `App_Data/dev-emails/` in Notepad, VS Code, or a mail viewer to inspect the 6-digit code.
4. **Before switching back to live Brevo delivery**, restore `Email__SmtpHost=smtp-relay.brevo.com`, clear `Email__PickupDirectory=` (leaving it empty), delete any temporary `.eml` files under `App_Data/dev-emails/` so old test codes are not left on disk, run `. .\scripts\import-dotenv.ps1 -Overwrite`, and restart the app. Having both `Email__SmtpHost` and `Email__PickupDirectory` set—or setting `Email__PickupDirectory` in `Production`—is rejected with a setup error.

### Visual Studio

Open `voting.csproj` in a Visual Studio installation that supports .NET 10 and has the ASP.NET/web development workload. Configure the SQL Server connection and administrator invitation as above, apply migrations, then run the HTTP profile. Visual Studio Code is also usable with the .NET SDK and its terminal.

## Accounts

There is **one sign-in page** for both roles. On successful sign-in, the account's stored role determines its dashboard.

1. Click **Create an account**.
2. Choose **Student** or **Administrator** at the top of registration.
3. Enter your name, email, and matching passwords of 12–256 characters.
4. Students may optionally enter a year level. Administrators must supply a valid invitation code (the bootstrap code only for the first administrator, or a code generated by an existing administrator).
5. Submit the form and enter the 6-digit email verification code sent to your email address before your authenticated session begins. Existing unverified accounts verify their email once at their next sign-in.
6. Use **Forgot password?** on the sign-in page to request a single-use, expiring 6-digit recovery code and reset your password (which also invalidates older sessions).

Fresh installations contain no seeded accounts or default passwords. Demo accounts created on a particular developer's machine are not included in Git.

## Invite another administrator

1. Sign in as an administrator and open **Account** in the sidebar.
2. Under **Invite an administrator**, click **Generate invitation code**.
3. Copy the code immediately; it is shown only once. Share it privately with the intended person. The app does not email administrator invitation codes; outbound email is used for 6-digit account verification and password recovery codes.
4. The recipient opens **Create an account**, selects **Administrator**, and enters the code along with their own account details.
5. Successful registration consumes the code. It cannot be used for another account.

Codes expire after **seven days**. The Account page lists who created each invitation and whether it is Active, Used, Expired, or Revoked. An administrator can click **Revoke invitation** to cancel an unused active code. Revocation prevents registration with that code; it does not remove an administrator who has already registered.

Invitation hashes are stored in SQL Server; the original code cannot be recovered from the Account page. Generate a new invitation if a code is lost. Each code grants administrator access, so do not publish it or include it in a Git commit. No server restart is needed to generate or revoke invitations.

## Election workflow

As an administrator:

1. Create a **draft election** for the room, such as **Room 101 Election**, with start and end times in Philippine time.
2. New drafts include President, Vice President, Secretary, Treasurer, and Auditor with one choice each. Open **Positions** to add a custom office with its own minimum and maximum choices. Leave the maximum blank if students must choose exactly the minimum.
3. Open **Candidates** and add at least as many candidates as each position's minimum. Saving a position or candidate keeps the selected election open.
4. Return to the selected election and click **Open voting**. Students can vote within the configured period.
5. Monitor ballot counts and tallies from the dashboard.
6. Change the election from **Open** to **Closed** to publish results.

For an election limited to a group, assign the students' group under **Voters** and select that group under **Voter access** on the election page. Choose **All students** for an unrestricted election. Voter access is editable until the first ballot or closing.

As a student, select your room's election when it shows **Start voting**. Use **Read profile** to check a candidate's platform on the ballot. Select **Review ballot** to check the selected names, then **Submit final ballot** to record your vote. **My ballots** shows the submission time.

On **Results**, open an election card to see each candidate's count and share of selections for their position.

Positions and candidates are locked after opening. Use **Edit schedule** on the election page to correct its Philippine-time start and end before the first ballot or closing. Schedule times are stored in UTC. Public and student results display only closed elections.

## Project structure

| Folder / file | Purpose |
| --- | --- |
| `Controllers/` | MVC request handlers, authentication, election management and voting |
| `Models/` | Account, election and ballot entities plus form/dashboard models |
| `Services/` | Brevo/SMTP email delivery (`EmailService.cs`) and candidate photo storage |
| `Views/` | Razor pages rendered by MVC, shared layouts and partials |
| `Data/VotingDbContext.cs` | Entity Framework Core SQL Server model and constraints |
| `Migrations/` | Database migration history |
| `scripts/` | PowerShell `.env` environment loader (`import-dotenv.ps1`) |
| `wwwroot/` | CSS, local scripts, images and bundled browser libraries |
| `Program.cs` | Services, cookies, authorization, routing and migration command |
| `appsettings.json` | Default local SQL Server configuration |
| `.env.example` | Brevo SMTP environment template (copy to `.env`, never commit `.env`) |
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

`tests/smoke.py` additionally requires SQLCMD and local Windows authentication. If the test app uses a database other than `ACMCLVoting`, set `VOTE_TEST_DB` to match. The test creates two uniquely named room elections, checks the voting workflow, and removes those fixtures. Run it on a local development database.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| `dotnet` is missing or SDK version is wrong | Install the .NET 10 SDK and reopen the terminal. |
| Cannot connect to SQL Server | Confirm the SQL Server service is running and the instance name is correct. SQL Server Express commonly uses `.\SQLEXPRESS`. |
| Login failed for Windows user | Grant that Windows account access through SQL Server; initial setup also needs database-creation permission. |
| Tables are missing | Apply migrations using the same Development environment and connection settings as the website. |
| The first administrator cannot register | With no existing administrators, set `AdminAccount__InvitationCode` in `.env` (reload with `. .\scripts\import-dotenv.ps1 -Overwrite`) or `AdminAccount:InvitationCode` in user secrets, then restart the app. |
| An invitation is rejected | Check that it is unused, not revoked, and less than seven days old. Existing administrators can issue a replacement from Account. The bootstrap code works only before the first administrator exists. |
| Address/port already in use | Stop your previous run with Ctrl+C, or choose another port in `--urls`. |
| Browser cannot open localhost | Keep the `dotnet run` terminal open and use its printed URL. |
| Build cannot overwrite `voting.exe` | Stop the running app before rebuilding. |
| Changed settings have no effect | Restart the app; if you edited `.env`, reload with `. .\scripts\import-dotenv.ps1 -Overwrite`. |
| Verification or recovery email fails to send | Confirm `.env` was loaded into the same PowerShell session, `Email__SmtpHost=smtp-relay.brevo.com`, `Email__SmtpPort=587`, `Email__UseSsl=true`, `Email__SmtpUsername` is your Brevo SMTP Login, `Email__SmtpPassword` is a generated Brevo SMTP key, `Email__FromAddress` is a verified Brevo sender (not `@smtp-brevo.com`), and `Email__PickupDirectory` is empty. |

### Isolated invitation, Brevo config, and feature-pack checks

`python tests/brevo_dotenv_smoke.py`, `python tests/invitations_smoke.py`, and `python tests/feature_pack_smoke.py` require SQLCMD and a SQL Server Windows login with permission to create and drop a temporary database. Each script creates its own randomly named `ACMCLVoting_AgyTest_...` database, applies migrations, starts a temporary app instance using isolated settings (and a temporary email pickup directory / photo folder where applicable), runs all assertions, and stops its app and deletes only its own temporary database and temporary files in `finally`. Do not point them at a real application database.

## Current limits

Voting group eligibility relies on administrator assignments. Institutional privacy notice and terms pages are currently draft text for institutional review prior to formal deployment. QR generation and production hosting/load testing are not implemented. Ballots are linked to accounts in the database; this is not an anonymous-election system. The app has not been audited or load tested for use in a real election.

Local databases, invitations, test credentials, screenshots, and build outputs should not be committed. Previously exposed credentials in older Git history require rotation; removing them from current files does not remove historical copies.
