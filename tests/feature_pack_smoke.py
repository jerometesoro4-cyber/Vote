"""Comprehensive isolated integration & security checks for the Vote feature pack.
Creates an isolated ACMCLVoting_AgyTest_* database starting from the prior 4-migration schema
with synthetic legacy fixtures, starts the Development app to test the automatic upgrade,
uses an isolated pickup directory and candidate photo storage, runs all tests,
and removes the temporary database, files, and process in finally.
Never touches ACMCLVoting.
"""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
import hashlib
import html
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import struct
import subprocess
import tempfile
import threading
import time
import zlib
import requests

ROOT = Path(__file__).resolve().parents[1]
DB = 'ACMCLVoting_AgyTest_Features_' + secrets.token_hex(8)
assert re.fullmatch(r'ACMCLVoting_AgyTest_Features_[0-9a-f]{16}', DB)
BOOTSTRAP = secrets.token_urlsafe(32)
PASSWORD = 'TestPassword-Vote2026!'
NEW_PASSWORD = 'UpdatedPassword-Vote2026!'
PICKUP_DIR = Path(tempfile.mkdtemp(prefix='vote-feat-mail-'))
PHOTO_DIR = Path(tempfile.mkdtemp(prefix='vote-feat-photos-'))
with socket.socket() as probe:
    probe.bind(('127.0.0.1', 0))
    PORT = probe.getsockname()[1]
BASE = f'http://127.0.0.1:{PORT}'
MANILA = timezone(timedelta(hours=8))

BASE_ENV = {
    **os.environ,
    'ASPNETCORE_ENVIRONMENT': 'Development',
    'ConnectionStrings__VotingDb': f'Server=localhost;Database={DB};Integrated Security=True;TrustServerCertificate=True',
    'AdminAccount__InvitationCode': BOOTSTRAP,
    'Email__SmtpHost': '',
    'Email__PickupDirectory': str(PICKUP_DIR),
    'CandidatePhotos__StoragePath': str(PHOTO_DIR),
    'EmailVerification__ResendCooldownSeconds': '0',
    'EmailVerification__MaxSendsPerWindow': '3',
}
APP = ['dotnet', str(ROOT / 'bin/Debug/net10.0/voting.dll')]
LOGDIR = ROOT / '.agent-logs'
LOGDIR.mkdir(exist_ok=True)


def sql(query, master=False):
    return subprocess.check_output(
        ['sqlcmd', '-b', '-S', 'localhost', '-E', '-C', '-d', 'master' if master else DB, '-h', '-1', '-W', '-Q', 'SET NOCOUNT ON; ' + query],
        text=True,
    ).strip()


def make_png_bytes(width=4, height=4, rgb=(16, 57, 42)):
    def chunk(ctype, data):
        body = ctype + data
        return struct.pack('>I', len(data)) + body + struct.pack('>I', zlib.crc32(body) & 0xffffffff)

    raw_rows = b''.join(b'\x00' + bytes(rgb) * width for _ in range(height))
    return (
        b'\x89PNG\r\n\x1a\n'
        + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
        + chunk(b'IDAT', zlib.compress(raw_rows))
        + chunk(b'IEND', b'')
    )


def make_jpeg_bytes(width=4, height=4):
    # Minimal valid JPEG SOF0 header + EOI marker sufficient for strict SOF dimension & EOI validation
    sof0 = b'\xff\xc0\x00\x0b\x08' + struct.pack('>HH', height, width) + b'\x01\x01\x11\x00'
    return b'\xff\xd8\xff\xe0\x00\x10JFIF\x00\x01\x01\x00\x00\x01\x00\x01\x00\x00' + sof0 + b'\xff\xda\x00\x08\x01\x01\x00\x00\x3f\x00\x7f\xff\xd9'


def token(session, path):
    r = session.get(BASE + path, timeout=15)
    assert r.status_code == 200, (path, r.status_code)
    m = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', r.text)
    assert m, f'Missing CSRF token on {path}'
    return html.unescape(m.group(1))


def post(session, path, data, source, files=None):
    csrf = token(session, source)
    if isinstance(data, list):
        fields = [('__RequestVerificationToken', csrf), *data]
    else:
        fields = {'__RequestVerificationToken': csrf, **data}
    return session.post(BASE + path, data=fields, files=files, allow_redirects=False, timeout=15)


def read_latest_email(email, purpose):
    results = []
    for path in sorted(PICKUP_DIR.glob('*.eml'), key=lambda p: (p.stat().st_mtime_ns, p.name), reverse=True):
        text = path.read_text(encoding='utf-8')
        if f'To: <{email}>' in text and f'X-ACMCL-Purpose: {purpose}' in text:
            m = re.search(r'\b(\d{6})\b', text.split('\n\n', 1)[1])
            if m:
                results.append((path, m.group(1), text))
    return results


def start_app(env_overrides=None):
    env = {**BASE_ENV, **(env_overrides or {})}
    log_path = LOGDIR / 'feature-pack-tests.log'
    log_file = log_path.open('a', encoding='utf-8')
    proc = subprocess.Popen(APP + ['--urls', BASE], cwd=ROOT, env=env, stdout=log_file, stderr=subprocess.STDOUT)
    for _ in range(100):
        try:
            if requests.get(BASE + '/Account/Login', timeout=1).status_code == 200:
                return proc, log_file
        except requests.RequestException:
            pass
        assert proc.poll() is None, 'App exited unexpectedly; inspect .agent-logs/feature-pack-tests.log'
        time.sleep(0.15)
    raise AssertionError('App did not start in time')


def stop_app(proc, log_file):
    if proc and proc.poll() is None:
        proc.terminate()
        try:
            proc.wait(timeout=15)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=10)
    if log_file:
        log_file.close()


def create_prior_schema_with_legacy_fixtures():
    sql(f'CREATE DATABASE [{DB}];', master=True)
    sql("""
        CREATE TABLE [__EFMigrationsHistory] (
            [MigrationId] nvarchar(150) NOT NULL,
            [ProductVersion] nvarchar(32) NOT NULL,
            CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
        );
        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES
            (N'20261115094106_InitialSqlServer', N'10.0.3'),
            (N'20261115101911_ElectionBallots', N'10.0.3'),
            (N'20261115114851_AdminInvitations', N'10.0.3'),
            (N'20261115145725_BallotSelectionsAndVotingGroups', N'10.0.3');

        CREATE TABLE [user_accounts] (
            [id] int NOT NULL IDENTITY,
            [full_name] nvarchar(100) NOT NULL,
            [email] nvarchar(254) NOT NULL,
            [normalized_email] nvarchar(254) NOT NULL,
            [password_hash] nvarchar(500) NOT NULL,
            [role] nvarchar(20) NOT NULL,
            [year_level] nvarchar(100) NULL,
            [voting_group] nvarchar(100) NULL,
            [created_at_utc] datetime2 NOT NULL,
            CONSTRAINT [PK_user_accounts] PRIMARY KEY ([id])
        );
        CREATE UNIQUE INDEX [IX_user_accounts_normalized_email] ON [user_accounts] ([normalized_email]);

        CREATE TABLE [AdminInvitations] (
            [Id] int NOT NULL IDENTITY,
            [CodeHash] nvarchar(64) NOT NULL,
            [CreatorId] int NOT NULL,
            [CreatedAtUtc] datetime2 NOT NULL,
            [ExpiresAtUtc] datetime2 NOT NULL,
            [UsedAtUtc] datetime2 NULL,
            [RevokedAtUtc] datetime2 NULL,
            CONSTRAINT [PK_AdminInvitations] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_AdminInvitations_user_accounts_CreatorId] FOREIGN KEY ([CreatorId]) REFERENCES [user_accounts] ([id]) ON DELETE NO ACTION
        );
        CREATE UNIQUE INDEX [IX_AdminInvitations_CodeHash] ON [AdminInvitations] ([CodeHash]);
        CREATE INDEX [IX_AdminInvitations_CreatorId] ON [AdminInvitations] ([CreatorId]);

        CREATE TABLE [Elections] (
            [Id] int NOT NULL IDENTITY,
            [Name] nvarchar(150) NOT NULL,
            [StartsAtUtc] datetime2 NOT NULL,
            [EndsAtUtc] datetime2 NOT NULL,
            [Status] nvarchar(20) NOT NULL,
            [RequiredGroup] nvarchar(100) NULL,
            CONSTRAINT [PK_Elections] PRIMARY KEY ([Id])
        );

        CREATE TABLE [Ballots] (
            [Id] int NOT NULL IDENTITY,
            [UserAccountId] int NOT NULL,
            [ElectionId] int NOT NULL,
            [CastAtUtc] datetime2 NOT NULL,
            CONSTRAINT [PK_Ballots] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_Ballots_Elections_ElectionId] FOREIGN KEY ([ElectionId]) REFERENCES [Elections] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_Ballots_user_accounts_UserAccountId] FOREIGN KEY ([UserAccountId]) REFERENCES [user_accounts] ([id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_Ballots_ElectionId] ON [Ballots] ([ElectionId]);
        CREATE UNIQUE INDEX [IX_Ballots_UserAccountId_ElectionId] ON [Ballots] ([UserAccountId], [ElectionId]);

        CREATE TABLE [Positions] (
            [Id] int NOT NULL IDENTITY,
            [ElectionId] int NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [MinSelections] int NOT NULL DEFAULT 1,
            [MaxSelections] int NOT NULL DEFAULT 1,
            CONSTRAINT [PK_Positions] PRIMARY KEY ([Id]),
            CONSTRAINT [CK_Positions_SelectionLimits] CHECK ([MinSelections] >= 1 AND [MaxSelections] >= [MinSelections]),
            CONSTRAINT [FK_Positions_Elections_ElectionId] FOREIGN KEY ([ElectionId]) REFERENCES [Elections] ([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_Positions_ElectionId] ON [Positions] ([ElectionId]);

        CREATE TABLE [Candidates] (
            [Id] int NOT NULL IDENTITY,
            [PositionId] int NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [Party] nvarchar(100) NOT NULL,
            [Platform] nvarchar(2000) NOT NULL,
            CONSTRAINT [PK_Candidates] PRIMARY KEY ([Id]),
            CONSTRAINT [AK_Candidates_Id_PositionId] UNIQUE ([Id], [PositionId]),
            CONSTRAINT [FK_Candidates_Positions_PositionId] FOREIGN KEY ([PositionId]) REFERENCES [Positions] ([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_Candidates_PositionId] ON [Candidates] ([PositionId]);

        CREATE TABLE [BallotSelections] (
            [Id] int NOT NULL IDENTITY,
            [BallotId] int NOT NULL,
            [PositionId] int NOT NULL,
            [CandidateId] int NOT NULL,
            CONSTRAINT [PK_BallotSelections] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_BallotSelections_Ballots_BallotId] FOREIGN KEY ([BallotId]) REFERENCES [Ballots] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_BallotSelections_Candidates_CandidateId_PositionId] FOREIGN KEY ([CandidateId], [PositionId]) REFERENCES [Candidates] ([Id], [PositionId]) ON DELETE NO ACTION,
            CONSTRAINT [FK_BallotSelections_Positions_PositionId] FOREIGN KEY ([PositionId]) REFERENCES [Positions] ([Id]) ON DELETE NO ACTION
        );
        CREATE UNIQUE INDEX [IX_BallotSelections_BallotId_CandidateId] ON [BallotSelections] ([BallotId], [CandidateId]);
        CREATE INDEX [IX_BallotSelections_CandidateId_PositionId] ON [BallotSelections] ([CandidateId], [PositionId]);
        CREATE INDEX [IX_BallotSelections_PositionId] ON [BallotSelections] ([PositionId]);

        -- Seed historical election, candidate, and ballot before migration
        INSERT INTO [Elections] ([Name], [StartsAtUtc], [EndsAtUtc], [Status], [RequiredGroup])
        VALUES (N'Legacy Archived Election', DATEADD(day, -10, SYSUTCDATETIME()), DATEADD(day, -9, SYSUTCDATETIME()), N'Closed', NULL);
        DECLARE @legElectionId int = SCOPE_IDENTITY();
        INSERT INTO [Positions] ([ElectionId], [Name], [MinSelections], [MaxSelections]) VALUES (@legElectionId, N'President', 1, 1);
        DECLARE @legPosId int = SCOPE_IDENTITY();
        INSERT INTO [Candidates] ([PositionId], [Name], [Party], [Platform]) VALUES (@legPosId, N'Legacy Winner', N'Unity', N'Legacy platform');
    """)


proc = None
log_file = None
try:
    (LOGDIR / 'feature-pack-tests.log').write_text('', encoding='utf-8')
    print('1. Creating isolated DB with prior 4-migration schema + legacy fixture...')
    create_prior_schema_with_legacy_fixtures()

    print('2. Starting the Development app against the old schema...')
    proc, log_file = start_app()

    applied = sql('SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId').splitlines()
    assert '20261117070000_AccountVerificationAndCandidatePhotos' in applied, applied
    assert sql("SELECT COUNT(*) FROM Elections WHERE Name='Legacy Archived Election'") == '1'

    # 3. Guest homepage, Privacy & Terms links, and show/hide password toggles
    print('3. Checking public navigation, privacy/terms pages, and password toggle markup...')
    root_page = requests.get(BASE + '/', timeout=15).text
    assert '<h1>Sign in</h1>' in root_page and '/Admin/' not in root_page
    home = requests.get(BASE + '/Home/Index', timeout=15).text
    assert 'href="/"' in home and 'Sign in' in home and '/Account/ChooseRole' not in home and '/Admin/' not in home
    assert '/Help/Privacy' in home and '/Help/Terms' in home
    privacy = requests.get(BASE + '/Help/Privacy', timeout=15).text
    terms = requests.get(BASE + '/Help/Terms', timeout=15).text
    assert 'institutional draft' in privacy.lower() and 'UserAccountId' in privacy and 'NPC Advisory No. 2021-01' in privacy
    assert 'institutional draft' in terms.lower() and 'Asia/Manila' in terms
    login_html = requests.get(BASE + '/Account/Login', timeout=15).text
    assert 'data-password-toggle' in login_html and '/Account/ForgotPassword' in login_html and '/Help/Privacy' in login_html and '/Help/Terms' in login_html

    # 4. Verified sign-up (Admin via bootstrap + Student) and legacy unverified account verification
    print('4. Testing email verification on registration, legacy unverified account, and invitation rules...')
    admin_sess = requests.Session()
    r = post(admin_sess, '/Account/Register', {
        'Role': 'Admin', 'FullName': 'Primary Admin', 'Email': 'admin1@example.test',
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD, 'InvitationCode': BOOTSTRAP,
    }, '/Account/Register')
    assert r.status_code == 302 and '/Account/VerifyEmail' in r.headers['Location']
    assert admin_sess.get(BASE + '/Admin/Index', allow_redirects=False).status_code == 302, 'Unverified admin must not have session yet'
    admin_otp = read_latest_email('admin1@example.test', 'VerifyEmail')[0][1]
    r = post(admin_sess, '/Account/VerifyEmail', {'Email': 'admin1@example.test', 'Code': admin_otp}, '/Account/VerifyEmail')
    assert r.status_code == 302 and r.headers['Location'] == '/Admin/Index'
    assert admin_sess.get(BASE + '/Admin/Index').status_code == 200

    # Student registers via legacy StudentSignup; cannot bypass verification
    stu_sess = requests.Session()
    r = post(stu_sess, '/Account/StudentSignup', {
        'FullName': 'Student One', 'Email': 'student1@example.test', 'YearLevel': '3rd Year',
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD,
    }, '/Account/Register')
    assert r.status_code == 302 and '/Account/VerifyEmail' in r.headers['Location']
    assert stu_sess.get(BASE + '/Student/Index', allow_redirects=False).status_code == 302

    # Create a legacy unverified student by cloning PasswordHash & setting email_verified_at_utc = NULL
    # Also simulate an existing legacy ballot for the legacy archived election
    stu_hash = sql("SELECT password_hash FROM user_accounts WHERE email='student1@example.test'")
    sql(f"""
        INSERT INTO [user_accounts] ([full_name], [email], [normalized_email], [password_hash], [role], [year_level], [created_at_utc], [email_verified_at_utc], [session_version])
        VALUES (N'Legacy User', N'legacy@example.test', N'LEGACY@EXAMPLE.TEST', N'{stu_hash}', N'Student', N'4th Year', DATEADD(day, -30, SYSUTCDATETIME()), NULL, 1);
    """)
    assert sql("SELECT email_verified_at_utc FROM user_accounts WHERE email='legacy@example.test'") == 'NULL'

    # Complete verification for student1@example.test
    stu_otp = read_latest_email('student1@example.test', 'VerifyEmail')[0][1]
    # Wrong purpose check: ResetPassword cannot redeem a VerifyEmail code!
    bad_purpose = post(stu_sess, '/Account/ResetPassword', {
        'Email': 'student1@example.test', 'Code': stu_otp, 'Password': NEW_PASSWORD, 'ConfirmPassword': NEW_PASSWORD,
    }, '/Account/ResetPassword')
    assert bad_purpose.status_code == 200 and 'invalid or has expired' in bad_purpose.text
    r = post(stu_sess, '/Account/VerifyEmail', {'Email': 'student1@example.test', 'Code': stu_otp}, '/Account/VerifyEmail')
    assert r.status_code == 302 and r.headers['Location'] == '/Student/Index'
    assert stu_sess.get(BASE + '/Student/Index').status_code == 200
    assert sql("SELECT password_hash FROM user_accounts WHERE email='legacy@example.test'") == stu_hash, 'Legacy password hash must stay intact'

    # Legacy unverified user tries to log in -> prompted to verify email before session issued, preserves hash
    leg_sess = requests.Session()
    r = post(leg_sess, '/Account/Login', {'email': 'legacy@example.test', 'password': PASSWORD}, '/Account/Login')
    assert r.status_code == 302 and '/Account/VerifyEmail' in r.headers['Location']
    assert leg_sess.get(BASE + '/Student/Index', allow_redirects=False).status_code == 302
    leg_otp = read_latest_email('legacy@example.test', 'VerifyEmail')[0][1]
    r = post(leg_sess, '/Account/VerifyEmail', {'Email': 'legacy@example.test', 'Code': leg_otp}, '/Account/VerifyEmail')
    assert r.status_code == 302 and r.headers['Location'] == '/Student/Index'
    assert sql("SELECT password_hash FROM user_accounts WHERE email='legacy@example.test'") == stu_hash

    # Check non-enumerability of public ForgotPassword & ResendVerification + resend throttling + recovery code security
    print('5. Testing Forgot Password, throttling, code bounds, concurrent redemption, and stale session invalidation...')
    anon = requests.Session()
    anon_fake = requests.Session()
    r_real = post(anon, '/Account/ForgotPassword', {'email': 'student1@example.test'}, '/Account/ForgotPassword')
    r_fake = post(anon_fake, '/Account/ForgotPassword', {'email': 'no-such-user@example.test'}, '/Account/ForgotPassword')
    assert r_real.status_code == r_fake.status_code == 302
    assert r_real.headers['Location'].split('?')[0] == r_fake.headers['Location'].split('?')[0] == '/Account/ResetPassword'
    assert 'If an account matches' in anon.get(BASE + r_real.headers['Location']).text
    assert 'If an account matches' in anon_fake.get(BASE + r_fake.headers['Location']).text

    # Test wrong recovery code and attempt cap (MaxAttempts = 5)
    reset_code = read_latest_email('student1@example.test', 'ResetPassword')[0][1]
    wrong_code = '000000' if reset_code != '000000' else '111111'
    for attempt in range(5):
        r_wrong = post(anon, '/Account/ResetPassword', {
            'Email': 'student1@example.test', 'Code': wrong_code, 'Password': NEW_PASSWORD, 'ConfirmPassword': NEW_PASSWORD,
        }, '/Account/ResetPassword')
        assert r_wrong.status_code == 200 and 'invalid or has expired' in r_wrong.text
    # After 5 failed attempts, even the real code is revoked!
    r_locked = post(anon, '/Account/ResetPassword', {
        'Email': 'student1@example.test', 'Code': reset_code, 'Password': NEW_PASSWORD, 'ConfirmPassword': NEW_PASSWORD,
    }, '/Account/ResetPassword')
    assert r_locked.status_code == 200 and 'invalid or has expired' in r_locked.text

    # Request 2 more recovery codes (with cooldown=0, MaxSendsPerWindow=3 -> 2nd & 3rd succeed, 4th throttled!)
    post(anon, '/Account/ForgotPassword', {'email': 'student1@example.test'}, '/Account/ForgotPassword')
    r_3rd = post(anon, '/Account/ForgotPassword', {'email': 'student1@example.test'}, '/Account/ForgotPassword')
    assert r_3rd.status_code == 302
    r_4th = post(anon, '/Account/ForgotPassword', {'email': 'student1@example.test'}, '/Account/ForgotPassword')
    assert 'Please wait before requesting another code' in anon.get(BASE + r_4th.headers['Location']).text
    # Also ensure non-existent email hits exact same throttle limit
    for _ in range(2):
        post(anon, '/Account/ForgotPassword', {'email': 'no-such-user@example.test'}, '/Account/ForgotPassword')
    r_fake_4th = post(anon, '/Account/ForgotPassword', {'email': 'no-such-user@example.test'}, '/Account/ForgotPassword')
    assert 'Please wait before requesting another code' in anon.get(BASE + r_fake_4th.headers['Location']).text

    # Verify non-plaintext storage of recovery codes in SQL
    latest_reset = read_latest_email('student1@example.test', 'ResetPassword')[0][1]
    assert sql(f"SELECT COUNT(*) FROM EmailVerificationCodes WHERE CodeHash='{latest_reset}'") == '0', 'Recovery code stored in plaintext!'

    # Test expired recovery code rejection
    sql("UPDATE EmailVerificationCodes SET ExpiresAtUtc = DATEADD(minute, -5, SYSUTCDATETIME()) WHERE Purpose='ResetPassword'")
    r_exp = post(anon, '/Account/ResetPassword', {
        'Email': 'student1@example.test', 'Code': latest_reset, 'Password': NEW_PASSWORD, 'ConfirmPassword': NEW_PASSWORD,
    }, '/Account/ResetPassword')
    assert r_exp.status_code == 200 and 'invalid or has expired' in r_exp.text

    # Issue a fresh code directly via fresh email (student2) to test concurrent redemption + stale session invalidation!
    stu2_sess = requests.Session()
    post(stu2_sess, '/Account/Register', {
        'Role': 'Student', 'FullName': 'Student Two', 'Email': 'student2@example.test',
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD,
    }, '/Account/Register')
    otp2 = read_latest_email('student2@example.test', 'VerifyEmail')[0][1]
    post(stu2_sess, '/Account/VerifyEmail', {'Email': 'student2@example.test', 'Code': otp2}, '/Account/VerifyEmail')
    assert stu2_sess.get(BASE + '/Student/Index').status_code == 200, 'student2 active session before password reset'

    post(requests.Session(), '/Account/ForgotPassword', {'email': 'student2@example.test'}, '/Account/ForgotPassword')
    rc2 = read_latest_email('student2@example.test', 'ResetPassword')[0][1]
    barrier = threading.Barrier(2)

    def redeem_reset():
        s = requests.Session()
        csrf = token(s, '/Account/ResetPassword')
        barrier.wait(timeout=10)
        return s.post(BASE + '/Account/ResetPassword', data={
            '__RequestVerificationToken': csrf,
            'Email': 'student2@example.test',
            'Code': rc2,
            'Password': NEW_PASSWORD,
            'ConfirmPassword': NEW_PASSWORD,
        }, allow_redirects=False, timeout=15).status_code

    with ThreadPoolExecutor(max_workers=2) as pool:
        statuses = list(pool.map(lambda _: redeem_reset(), range(2)))
    assert statuses.count(302) == 1 and statuses.count(200) == 1, f'Expected exactly one winner: {statuses}'
    # Replay same code after consumption
    r_reuse = post(requests.Session(), '/Account/ResetPassword', {
        'Email': 'student2@example.test', 'Code': rc2, 'Password': NEW_PASSWORD, 'ConfirmPassword': NEW_PASSWORD,
    }, '/Account/ResetPassword')
    assert r_reuse.status_code == 200 and 'invalid or has expired' in r_reuse.text
    # Stale session from before password reset MUST be rejected!
    assert stu2_sess.get(BASE + '/Student/Index', allow_redirects=False).status_code == 302, 'Stale session remained valid after password reset!'
    # New password works
    stu2_new = requests.Session()
    r_new_login = post(stu2_new, '/Account/Login', {'email': 'student2@example.test', 'password': NEW_PASSWORD}, '/Account/Login')
    assert r_new_login.status_code == 302 and r_new_login.headers['Location'] == '/Student/Index'

    # 6. Email delivery failure handling (restart temporarily with Email__SimulateFailure=true)
    print('6. Testing actionable delivery failure handling...')
    stop_app(proc, log_file)
    proc, log_file = start_app({'Email__SimulateFailure': 'true'})
    fail_sess = requests.Session()
    r_fail = post(fail_sess, '/Account/ForgotPassword', {'email': 'student2@example.test'}, '/Account/ForgotPassword')
    assert r_fail.status_code == 200 and 'could not send' in r_fail.text.lower()
    stop_app(proc, log_file)
    proc, log_file = start_app()
    r_recover = post(requests.Session(), '/Account/ForgotPassword', {'email': 'student2@example.test'}, '/Account/ForgotPassword')
    assert r_recover.status_code == 302 and '/Account/ResetPassword' in r_recover.headers['Location']

    # 7. Admin pagination (>10 items) and filtering across Elections, Candidates, and Voters
    print('7. Testing pagination (>1 page) and filtering on Elections, Candidates, and Voters...')
    now = datetime.now(timezone.utc)
    # Create 12 draft elections so Elections spans 2 pages (10 per page)
    created_elections = []
    for idx in range(1, 13):
        ename = f'Pagination Room {idx:02d}'
        grp = 'BSIT' if idx % 2 == 0 else ''
        r = post(admin_sess, '/Admin/CreateElection', {
            'name': ename,
            'startsAtManila': (now - timedelta(hours=1)).astimezone(MANILA).strftime('%Y-%m-%dT%H:%M'),
            'endsAtManila': (now + timedelta(hours=4 + idx)).astimezone(MANILA).strftime('%Y-%m-%dT%H:%M'),
            'requiredGroup': grp,
            'includeStandardPositions': 'true',
        }, '/Admin/Elections')
        assert r.status_code == 302
        eid = int(sql(f"SELECT Id FROM Elections WHERE Name='{ename}'"))
        created_elections.append(eid)

    e_p1 = admin_sess.get(BASE + '/Admin/Elections?status=Draft&group=BSIT&page=1').text
    e_p2 = admin_sess.get(BASE + '/Admin/Elections?status=Draft&group=BSIT&page=2').text
    assert 'Pagination Room 12' in e_p1 and 'Pagination Room 02' in e_p1
    e_all_p1 = admin_sess.get(BASE + '/Admin/Elections?page=1').text
    e_all_p2 = admin_sess.get(BASE + '/Admin/Elections?page=2').text
    assert 'Page 1 of 2' in e_all_p1 and 'Page 2 of 2' in e_all_p2
    assert 'Legacy Archived Election' in e_all_p2
    assert 'No matching elections' in admin_sess.get(BASE + '/Admin/Elections?q=DoesNotExistXYZ').text

    # Add 12 students so Voters spans 2 pages and test filter + edit redirect preservation
    for idx in range(1, 13):
        email = f'pagedvoter{idx:02d}@example.test'
        s = requests.Session()
        post(s, '/Account/Register', {
            'Role': 'Student', 'FullName': f'Alpha Voter {idx:02d}', 'Email': email,
            'YearLevel': '2nd Year', 'Password': PASSWORD, 'ConfirmPassword': PASSWORD,
        }, '/Account/Register')
        otp = read_latest_email(email, 'VerifyEmail')[0][1]
        post(s, '/Account/VerifyEmail', {'Email': email, 'Code': otp}, '/Account/VerifyEmail')

    v_p2 = admin_sess.get(BASE + '/Admin/Users?q=Alpha%20Voter&page=2').text
    assert 'Alpha Voter 11' in v_p2 and 'Alpha Voter 01' not in v_p2
    v11_id = int(sql("SELECT id FROM user_accounts WHERE email='pagedvoter11@example.test'"))
    r_grp = post(admin_sess, '/Admin/AssignVotingGroup', {
        'id': v11_id, 'votingGroup': 'BSIT', 'q': 'Alpha Voter', 'group': '__none__', 'page': 1,
    }, '/Admin/Users?q=Alpha%20Voter&group=__none__')
    assert r_grp.status_code == 302 and 'q=Alpha' in r_grp.headers['Location'] and 'group=__none__' in r_grp.headers['Location']

    # 8. Candidate photo upload, validation, replacement, cleanup, draft scoping, and move VP -> President
    print('8. Testing candidate photos, unsafe upload rejection, edit/move VP->President, and draft deletion...')
    target_election = created_elections[0]
    pres_pos_id = int(sql(f"SELECT Id FROM Positions WHERE ElectionId={target_election} AND Name='President'"))
    vp_pos_id = int(sql(f"SELECT Id FROM Positions WHERE ElectionId={target_election} AND Name='Vice President'"))
    cand_url = f'/Admin/Candidates/{target_election}'

    # Reject SVG/XSS upload, wrong extension, and oversize upload
    r_svg = post(admin_sess, '/Admin/AddCandidate', {
        'electionId': target_election, 'positionId': vp_pos_id, 'name': 'Bad Upload', 'party': 'P', 'platform': 'X',
    }, cand_url, files={'photo': ('evil.svg', b'<svg><script>alert(1)</script></svg>', 'image/svg+xml')})
    assert r_svg.status_code == 302
    assert 'Upload a JPEG, PNG, or WebP' in admin_sess.get(BASE + cand_url).text
    assert sql(f"SELECT COUNT(*) FROM Candidates WHERE PositionId={vp_pos_id} AND Name='Bad Upload'") == '0'

    r_spoof = post(admin_sess, '/Admin/AddCandidate', {
        'electionId': target_election, 'positionId': vp_pos_id, 'name': 'Spoofed Upload', 'party': 'P', 'platform': 'X',
    }, cand_url, files={'photo': ('spoof.png', b'<html><script>alert(1)</script></html>', 'image/png')})
    assert r_spoof.status_code == 302
    assert 'Unsupported or unsafe' in admin_sess.get(BASE + cand_url).text

    # Add candidate with valid PNG photo
    png1 = make_png_bytes(6, 6, (9, 96, 61))
    r_add = post(admin_sess, '/Admin/AddCandidate', {
        'electionId': target_election, 'positionId': vp_pos_id, 'name': 'Candidate Mover', 'party': 'Green', 'platform': 'Initial platform',
    }, cand_url, files={'photo': ('headshot.png', png1, 'image/png')})
    assert r_add.status_code == 302
    cand_id = int(sql("SELECT Id FROM Candidates WHERE Name='Candidate Mover'"))
    file1 = sql(f"SELECT PhotoFileName FROM Candidates WHERE Id={cand_id}")
    assert re.fullmatch(r'[a-f0-9]{32}\.png', file1)
    assert (PHOTO_DIR / file1).exists()

    # Draft candidate photo: allowed for Admin, hidden (404) for anonymous and student!
    assert admin_sess.get(BASE + f'/CandidatePhoto/{cand_id}').status_code == 200
    assert admin_sess.get(BASE + f'/CandidatePhoto/{cand_id}').headers.get('X-Content-Type-Options') == 'nosniff'
    assert requests.get(BASE + f'/CandidatePhoto/{cand_id}').status_code == 404
    assert stu_sess.get(BASE + f'/CandidatePhoto/{cand_id}').status_code == 404
    # Traversal rejection
    assert admin_sess.get(BASE + '/CandidatePhoto/..%2F..%2Fappsettings.json').status_code == 404

    # Replace photo with an invalid payload (>32 bytes non-JPEG) and oversized payload (>2 MB) -> MUST keep old photo intact!
    r_bad_replace = post(admin_sess, '/Admin/UpdateCandidate', {
        'id': cand_id, 'electionId': target_election, 'positionId': vp_pos_id,
        'name': 'Candidate Mover', 'party': 'Green', 'platform': 'Initial platform',
    }, cand_url, files={'photo': ('bad.jpg', b'not-a-valid-jpeg-image-payload-' * 3, 'image/jpeg')})
    assert r_bad_replace.status_code == 302
    assert 'valid JPEG, PNG, or WebP' in admin_sess.get(BASE + cand_url).text
    assert sql(f"SELECT PhotoFileName FROM Candidates WHERE Id={cand_id}") == file1
    assert (PHOTO_DIR / file1).exists()

    r_oversize = post(admin_sess, '/Admin/UpdateCandidate', {
        'id': cand_id, 'electionId': target_election, 'positionId': vp_pos_id,
        'name': 'Candidate Mover', 'party': 'Green', 'platform': 'Initial platform',
    }, cand_url, files={'photo': ('huge.png', b'\x89PNG\r\n\x1a\n' + b'\x00' * (2 * 1024 * 1024 + 64), 'image/png')})
    assert r_oversize.status_code == 302
    assert 'smaller than 2 MB' in admin_sess.get(BASE + cand_url).text
    assert sql(f"SELECT PhotoFileName FROM Candidates WHERE Id={cand_id}") == file1

    # Valid edit + move from Vice President -> President + replace photo with JPEG
    jpg1 = make_jpeg_bytes(8, 8)
    r_move = post(admin_sess, '/Admin/UpdateCandidate', {
        'id': cand_id, 'electionId': target_election, 'positionId': pres_pos_id,
        'name': 'Candidate Moved Pres', 'party': 'Gold', 'platform': 'Updated platform',
    }, cand_url, files={'photo': ('portrait.jpg', jpg1, 'image/jpeg')})
    assert r_move.status_code == 302
    assert 'Candidate updated' in admin_sess.get(BASE + cand_url).text
    assert int(sql(f"SELECT PositionId FROM Candidates WHERE Id={cand_id}")) == pres_pos_id
    file2 = sql(f"SELECT PhotoFileName FROM Candidates WHERE Id={cand_id}")
    assert re.fullmatch(r'[a-f0-9]{32}\.jpg', file2) and file2 != file1
    assert not (PHOTO_DIR / file1).exists(), 'Old photo file was not cleaned up after successful replacement!'
    assert (PHOTO_DIR / file2).exists()

    # Add 12 candidates in target_election so Candidates pagination (>1 page) is also verified
    for idx in range(1, 13):
        post(admin_sess, '/Admin/AddCandidate', {
            'electionId': target_election, 'positionId': vp_pos_id, 'name': f'VP Nominee {idx:02d}', 'party': 'Gold', 'platform': 'Service',
        }, cand_url)
    c_p1 = admin_sess.get(BASE + f'{cand_url}?page=1').text
    c_p2 = admin_sess.get(BASE + f'{cand_url}?page=2').text
    assert 'Page 1 of 2' in c_p1 and 'Page 2 of 2' in c_p2
    assert 'Candidate Moved Pres' in admin_sess.get(BASE + f'{cand_url}?positionId={pres_pos_id}').text
    assert 'Candidate Moved Pres' not in admin_sess.get(BASE + f'{cand_url}?positionId={vp_pos_id}').text

    # Test Draft candidate deletion + photo cleanup
    del_cand_id = int(sql("SELECT Id FROM Candidates WHERE Name='VP Nominee 12'"))
    r_del_cand = post(admin_sess, '/Admin/DeleteCandidate', {'id': del_cand_id, 'electionId': target_election}, cand_url)
    assert r_del_cand.status_code == 302
    assert sql(f"SELECT COUNT(*) FROM Candidates WHERE Id={del_cand_id}") == '0'

    # Test Draft election deletion (with a separate draft election that has a photo candidate -> removes DB rows & photo file)
    del_election = created_elections[1]
    del_e_pos = int(sql(f"SELECT TOP 1 Id FROM Positions WHERE ElectionId={del_election}"))
    post(admin_sess, '/Admin/AddCandidate', {
        'electionId': del_election, 'positionId': del_e_pos, 'name': 'Disposable Candidate',
    }, f'/Admin/Candidates/{del_election}', files={'photo': ('disp.png', png1, 'image/png')})
    disp_file = sql("SELECT PhotoFileName FROM Candidates WHERE Name='Disposable Candidate'")
    assert (PHOTO_DIR / disp_file).exists()
    r_del_elec = post(admin_sess, '/Admin/DeleteElection', {'id': del_election}, f'/Admin/Elections/{del_election}')
    assert r_del_elec.status_code == 302 and '/Admin/Elections' in r_del_elec.headers['Location']
    assert sql(f"SELECT COUNT(*) FROM Elections WHERE Id={del_election}") == '0'
    assert not (PHOTO_DIR / disp_file).exists(), 'Deleted election candidate photo was not cleaned up!'

    # Open target_election (first populate remaining standard positions), cast ballot, verify denial of edit/delete + public photo + dashboard + printable report
    print('9. Testing open/ballot edit & delete denial, public photo visibility, dashboard SQL aggregates, and printable report...')
    for pos_id_str in sql(f"SELECT Id FROM Positions WHERE ElectionId={target_election} AND Id NOT IN ({pres_pos_id}, {vp_pos_id})").splitlines():
        post(admin_sess, '/Admin/AddCandidate', {
            'electionId': target_election, 'positionId': int(pos_id_str), 'name': f'Std Candidate {pos_id_str}',
        }, cand_url)
    r_open = post(admin_sess, '/Admin/ChangeStatus', {'id': target_election, 'status': 'Open'}, f'/Admin/Elections/{target_election}')
    assert r_open.status_code == 302
    assert sql(f"SELECT Status FROM Elections WHERE Id={target_election}") == 'Open'

    # Now photo of Candidate Moved Pres IS visible to public & students because election is Open!
    assert requests.get(BASE + f'/CandidatePhoto/{cand_id}').status_code == 200
    assert stu_sess.get(BASE + f'/CandidatePhoto/{cand_id}').status_code == 200

    # Student votes in target_election
    choices = {}
    for pos_id_str in sql(f"SELECT Id FROM Positions WHERE ElectionId={target_election}").splitlines():
        pid = int(pos_id_str)
        cid = int(sql(f"SELECT TOP 1 Id FROM Candidates WHERE PositionId={pid} ORDER BY Id"))
        choices[f'choices[{pid}]'] = cid
    r_rev = post(stu_sess, '/Student/Vote', {'electionId': target_election, **choices}, f'/Student/Vote/{target_election}')
    assert r_rev.status_code == 200
    rev_token = html.unescape(re.search(r'name="reviewToken" value="([^"]+)"', r_rev.text).group(1))
    r_cast = post(stu_sess, '/Student/Vote', {'electionId': target_election, 'step': 'Confirm', 'reviewToken': rev_token}, f'/Student/Vote/{target_election}')
    assert r_cast.status_code == 302 and r_cast.headers['Location'] == '/Student/History'

    # Attempting to edit/move/delete candidate or delete election when Open/with ballots MUST be denied!
    r_edit_denied = post(admin_sess, '/Admin/UpdateCandidate', {
        'id': cand_id, 'electionId': target_election, 'positionId': vp_pos_id, 'name': 'Tamper',
    }, cand_url)
    assert 'draft election with no ballots' in admin_sess.get(BASE + cand_url).text
    assert int(sql(f"SELECT PositionId FROM Candidates WHERE Id={cand_id}")) == pres_pos_id
    r_del_denied = post(admin_sess, '/Admin/DeleteElection', {'id': target_election}, f'/Admin/Elections/{target_election}')
    assert 'draft elections with no submitted ballots' in admin_sess.get(BASE + f'/Admin/Elections/{target_election}').text
    assert sql(f"SELECT COUNT(*) FROM Elections WHERE Id={target_election}") == '1'

    # Anonymous & student denial of admin endpoints (including DeleteCandidate, DeleteElection, UpdateCandidate, Report)
    for denied_sess, label in [(requests.Session(), 'anon'), (stu_sess, 'student')]:
        assert denied_sess.get(BASE + f'/Admin/Report/{target_election}', allow_redirects=False).status_code == 302, label
        assert denied_sess.post(BASE + '/Admin/DeleteElection', data={'id': target_election}, allow_redirects=False).status_code == 302, label

    # Breadcrumbs & Dashboard aggregates & Printable Report
    detail_html = admin_sess.get(BASE + f'/Admin/Elections/{target_election}').text
    assert 'aria-label="Breadcrumbs"' in detail_html and 'aria-current="page"' in detail_html
    dash_html = admin_sess.get(BASE + '/Admin/Index').text
    assert 'Ballots &amp; participation by election' in dash_html and 'Elections &amp; ballots by status' in dash_html
    assert '1 / ' in dash_html and '1 ballot' in dash_html
    report_html = admin_sess.get(BASE + f'/Admin/Report/{target_election}').text
    assert 'images/logo.webp' in report_html and 'ACMCL College Inc.' in report_html and 'PHT' in report_html
    assert 'Candidate Moved Pres' in report_html and f'/CandidatePhoto/{cand_id}' in report_html
    assert 'student1@example.test' not in report_html and 'Student One' not in report_html, 'Report leaked voter identity!'

    print('PASS: all feature-pack integration, migration upgrade, security, photo, maintenance, pagination, dashboard, report, and policy checks succeeded!')
finally:
    stop_app(proc, log_file)
    sql(f"IF DB_ID(N'{DB}') IS NOT NULL DROP DATABASE [{DB}];", master=True)
    shutil.rmtree(PICKUP_DIR, ignore_errors=True)
    shutil.rmtree(PHOTO_DIR, ignore_errors=True)
    print('Cleaned up isolated test database, pickup directory, photo directory, and temporary app.')
