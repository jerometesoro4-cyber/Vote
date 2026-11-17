# ACMCL Voting Portal

A C# **ASP.NET Core MVC** application for student elections with Razor views and a **Microsoft SQL Server** database managed by Entity Framework Core. Follow steps 1–5 for a fresh Windows setup. If you already run the portal, start with [Update an existing installation](#update-an-existing-installation).

## 1. Install the tools and get the project

- Install the Windows **.NET 10 SDK**. Open a new PowerShell window and check that `dotnet --list-sdks` includes a `10.0` SDK. [Download .NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Install Git for Windows and have internet access for the first NuGet restore.
- Install **SQL Server Express** or use an existing SQL Server instance. The Database Engine runs the database. **SQL Server Management Studio (SSMS)** is an optional tool for connecting, backing up, and inspecting it. [SQL Server downloads](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) · [Install SSMS](https://learn.microsoft.com/en-us/ssms/install/install)

From the folder where you keep projects, clone once and restore packages.

```powershell
git clone https://github.com/jerometesoro4-cyber/Vote.git
cd Vote
dotnet restore
```

Run the remaining commands from the folder containing `voting.csproj` and `.env.example`.

## 2. Set up SQL Server and create `.env`

1. If installing SQL Server Express for the first time, install the Database Engine and note the instance name shown by Setup. Open **Services** (`services.msc`) and confirm the matching **SQL Server** service is running. For example, **SQL Server (SQLEXPRESS)** usually connects as `.\SQLEXPRESS`. A default instance appears as **SQL Server (MSSQLSERVER)** and connects as `localhost`.
2. If using SSMS, connect to that server using **Windows Authentication** with the same Windows account that will run the app. For a new local database, that account needs permission to create the database and apply migrations. For a managed instance, ask the database administrator to provision the target database and give you the permissions needed to apply the schema.
3. Create your local `.env` from the example. This command keeps an existing `.env`. Keep real SMTP keys, database passwords, and invitation codes in your private configuration. Git ignores `.env`.

   ```powershell
   if (-not (Test-Path -LiteralPath .env)) { Copy-Item .env.example .env }
   notepad .env
   ```

4. Set `ConnectionStrings__VotingDb` in `.env` to match your instance, removing the leading `#` from the example line. The app defaults to `localhost` and `ACMCLVoting` if you leave the line commented out and have no other connection override.

   | Local SQL Server instance | Line for `.env` |
   | --- | --- |
   | SQL Server Express named `SQLEXPRESS` | `ConnectionStrings__VotingDb=Server=.\SQLEXPRESS;Database=ACMCLVoting;Integrated Security=True;TrustServerCertificate=True` |
   | Default local instance | `ConnectionStrings__VotingDb=Server=localhost;Database=ACMCLVoting;Integrated Security=True;TrustServerCertificate=True` |
   | Existing Windows LocalDB instance named `MSSQLLocalDB` | `ConnectionStrings__VotingDb=Server=(localdb)\MSSQLLocalDB;Database=ACMCLVoting;Integrated Security=True;TrustServerCertificate=True` |

If you already have accounts or ballots, keep the exact `Server` and `Database` values for that installation. For an instance using SQL Server Authentication, use its assigned `User ID` and `Password` in your private connection string in place of `Integrated Security=True`. The certificate setting in the examples is for a local instance. Use a trusted server certificate for a hosted database. [SQL Server Express LocalDB](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb)

## 3. Set up Brevo SMTP

Brevo sends the six-digit codes for registration, first sign-in for unverified accounts, and password recovery. Set up the sender before registering the first account.

1. Sign in to Brevo and make sure **Transactional** email is enabled for your account. If Brevo shows `450 Your SMTP account is not yet activated`, request activation from Brevo Support. [Brevo SMTP troubleshooting](https://help.brevo.com/hc/en-us/articles/115000188150-Troubleshooting-Issues-with-Brevo-SMTP)
2. In **Settings → Senders, Domains, IPs → Senders**, choose **Add a sender**. Enter a display name and an address you control, then complete any verification Brevo requests. If you send from an organization’s domain, follow Brevo’s domain authentication setup. [Create a sender](https://help.brevo.com/hc/en-us/articles/208836149-Create-a-new-sender-From-name-and-From-email) · [Authenticate a sending domain](https://help.brevo.com/hc/en-us/articles/12163873383186-Authenticate-your-domain-with-Brevo-Brevo-code-DKIM-DMARC)
3. In **Settings → SMTP & API → SMTP**, copy the **SMTP Login** and generate a new **SMTP key**. Save the key when it is shown. [Brevo SMTP setup](https://help.brevo.com/hc/en-us/articles/7924908994450-Send-transactional-emails-using-Brevo-SMTP) · [Manage SMTP keys](https://help.brevo.com/hc/en-us/articles/7959631848850-Create-and-manage-your-SMTP-keys)
4. In `.env`, fill in `Email__SmtpUsername` with the **SMTP Login**, `Email__SmtpPassword` with the **SMTP key**, and `Email__FromAddress` with the **verified sender address**. Keep `Email__SmtpHost=smtp-relay.brevo.com`, `Email__SmtpPort=587`, and `Email__UseSsl=true` for STARTTLS. Leave `Email__PickupDirectory=` empty for Brevo delivery. `Email__FromName` sets the name recipients see and defaults to `ACMCL Vote`.

## 4. Load `.env` and apply database migrations

Keep `ASPNETCORE_ENVIRONMENT=Development` in `.env` for local setup. On a fresh database, set `AdminAccount__InvitationCode` to a long, random code from a password manager. You’ll enter it on the first administrator’s registration form. If you already have an administrator, leave this entry blank and sign in with that account.

In PowerShell, load `.env` and apply the schema to the database configured above.

```powershell
. .\scripts\import-dotenv.ps1 -Overwrite
dotnet run --no-launch-profile -- --migrate
```

The first run creates the configured database if needed and applies the migrations. Later runs apply pending migrations and return to the prompt. For an existing database, confirm the server and database name before running the command. In SSMS, refresh the database and check **Tables → dbo.__EFMigrationsHistory** for the applied migrations.

In Development, starting the app from PowerShell or the debugger also applies pending migrations to its configured database before serving pages. Use Development with a local development database. Outside Development, apply migrations explicitly before starting the app. If a migration is pending, startup stops with instructions for upgrading the database.

ASP.NET Core reads environment variables from the process, so run the loader in each new PowerShell window before you start or migrate the app. `-Overwrite` refreshes keys listed in `.env`. If you remove a key from `.env`, open a new terminal to clear values imported by the old terminal.

## 5. Start the portal and verify email

Run this in the same PowerShell window after the migration succeeds. Keep the terminal open while using the site.

```powershell
dotnet run --no-launch-profile -- --urls http://127.0.0.1:5189
```

1. Open the portal. On a fresh database, choose **Create an account → Administrator**, enter the invitation code from step 4, and use an inbox you can access. Enter the six-digit code from the email to verify the account and sign in. After the first administrator registers, use invitations from the **Account** page to add other administrators. [Open the local portal](http://127.0.0.1:5189)
2. Register a test **Student** account with an inbox you control and confirm that its verification code arrives. Use **Forgot password?** for the test account to check that the recovery code arrives and that you can sign in with the new password.
3. To change a setting, stop the app with **Ctrl+C**, edit `.env`, run `. .\scripts\import-dotenv.ps1 -Overwrite`, and start it again.

For Visual Studio, open `voting.csproj` with the ASP.NET workload and run the same commands in its PowerShell terminal. If you launch from the IDE’s debugger instead, configure equivalent settings in **Manage User Secrets**, using `:` in place of `__` in key names, and use the URL shown by the HTTP profile (`http://localhost:5072`).

## Update an existing installation

1. Stop the running app with **Ctrl+C**. In SSMS, connect to the database the app currently uses. Right-click it, choose **Tasks → Back Up**, and make a **Full** backup to a private location. Back up the candidate photo folder separately if it contains uploads. [Back up a SQL Server database](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/create-a-full-database-backup-sql-server)
2. In the existing clone on `main`, check `git status --short` and save any local edits before pulling.

   ```powershell
   git pull --ff-only origin main
   dotnet restore
   ```

3. Keep your existing database connection in `.env`, and copy any new keys you need from `.env.example`. If this is your first time using `.env`, follow steps 2–3. Configure Brevo before bringing the app back up so existing accounts can receive verification codes.
4. Run the loader and migration in step 4, then restart using step 5. The `AccountVerificationAndCandidatePhotos` migration adds schema to the same database. It preserves existing accounts, password hashes, and ballots. Users whose email addresses were unverified will need a verification code the next time they sign in.

When moving to a new computer, restore the SQL Server backup if you want to bring existing accounts and elections over. Copy the candidate photo folder and configure each machine’s `.env` separately. Git carries the source and migration history.

### Candidate photo storage

Candidate photos are stored under `App_Data/candidate-photos` by default. The database holds photo references. Set `CandidatePhotos__StoragePath` to a private directory outside `wwwroot` if you need a different location. Give the account running the app write access and keep the folder with your database backups.

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
6. Change the election from **Open** to **Closed** to publish results. On the closed election, open **Printable results report**, then choose **Print / Save PDF** in the report.

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

`tests/smoke.py` additionally requires `sqlcmd` and Windows Authentication to the default `localhost` SQL Server instance. Set `VOTE_TEST_DB` to the same disposable database that the test app uses. The test creates two room elections, checks the voting workflow, and removes its fixtures.

### Isolated verification and feature checks

The scripts below require the Debug build, Python with `requests`, `sqlcmd`, and permissions to create and drop temporary databases on the default `localhost` SQL Server instance. They start their own app instances and use generated fixtures, including local `.eml` files for verification where needed.

```powershell
python tests/brevo_dotenv_smoke.py
python tests/invitations_smoke.py
python tests/feature_pack_smoke.py
```

For manual offline testing with disposable accounts, use `ASPNETCORE_ENVIRONMENT=Development`, clear `Email__SmtpHost=`, and set `Email__PickupDirectory=App_Data/dev-emails`. Reload `.env` and restart to read the generated `.eml` files in that folder. Restore the Brevo host and clear the pickup directory setting before testing live delivery.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| `dotnet` is missing or SDK version is wrong | Install the .NET 10 SDK and reopen the terminal. |
| Cannot connect to SQL Server | Check that the server is running and that you can connect to the exact instance in SSMS. SQL Server Express commonly uses `.\SQLEXPRESS`. |
| Login failed for Windows user | Use a Windows account with access to that instance and database. For initial setup, use an account with permission to create the database or ask the DBA to provision it. |
| Tables or columns are missing, including `voting_group`, `PhotoContentType`, or `PhotoFileName` | Pull the current code, confirm the app points to the intended SQL Server database, and restart in Development or apply the migration explicitly as shown in step 4. |
| The first administrator cannot register | With no existing administrators, set `AdminAccount__InvitationCode` in `.env` (reload with `. .\scripts\import-dotenv.ps1 -Overwrite`) or `AdminAccount:InvitationCode` in user secrets, then restart the app. |
| An invitation is rejected | Check that it is unused, not revoked, and less than seven days old. Existing administrators can issue a replacement from Account. The bootstrap code works only before the first administrator exists. |
| Address/port already in use | Stop your previous run with Ctrl+C, or choose another port in `--urls`. |
| Browser cannot open localhost | Keep the `dotnet run` terminal open and use its printed URL. |
| Build cannot overwrite `voting.exe` | Stop the running app before rebuilding. |
| Changed settings have no effect | Restart the app; if you edited `.env`, reload with `. .\scripts\import-dotenv.ps1 -Overwrite`. |
| Brevo returns `535` | Recheck the **SMTP Login** and **SMTP key** in step 3, reload `.env`, and restart. |
| SMTP connection times out | From the app’s computer, run `Test-NetConnection smtp-relay.brevo.com -Port 587` to check access to the relay. |
| No verification or reset email arrives | Check the app terminal for SMTP errors, then check **Transactional → Logs** in Brevo and the recipient’s spam folder. After fixing SMTP, use **Resend code** on the verification page or request a new code from **Forgot password?**. [Brevo SMTP troubleshooting](https://help.brevo.com/hc/en-us/articles/115000188150-Troubleshooting-Issues-with-Brevo-SMTP) |
| A student cannot see **Start voting** | Check that the election is **Open**, the current Philippine time is within the configured voting period, and the voter is assigned to the election’s group if it requires one. |

## Current limits

Voting group eligibility relies on administrator assignments. Institutional privacy notice and terms pages are currently draft text for institutional review prior to formal deployment. QR generation and production hosting/load testing are not implemented. Ballots are linked to accounts in the database; this is not an anonymous-election system. The app has not been audited or load tested for use in a real election.

Local databases, invitations, test credentials, screenshots, and build outputs should not be committed. Previously exposed credentials in older Git history require rotation; removing them from current files does not remove historical copies.
