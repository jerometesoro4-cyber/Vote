"""Focused synthetic checks for .gitignore, .env.example, scripts/import-dotenv.ps1, and EmailService Brevo setup validation.
Uses only synthetic in-memory/temp files and an isolated ACMCLVoting_AgyTest_Brevo_* database; never touches ACMCLVoting or real credentials.
"""
import html
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import subprocess
import tempfile
import time
import requests

ROOT = Path(__file__).resolve().parents[1]
LOGDIR = ROOT / '.agent-logs'
LOGDIR.mkdir(exist_ok=True)
LOGFILE = LOGDIR / 'brevo-dotenv-tests.log'
LOGFILE.write_text('', encoding='utf-8')

DB = 'ACMCLVoting_AgyTest_Brevo_' + secrets.token_hex(8)
assert re.fullmatch(r'ACMCLVoting_AgyTest_Brevo_[0-9a-f]{16}', DB)
PASSWORD = 'TestPassword-BrevoCheck2026!'
BOOTSTRAP = secrets.token_urlsafe(32)
PICKUP_DIR = Path(tempfile.mkdtemp(prefix='vote-brevo-mail-'))
PHOTO_DIR = Path(tempfile.mkdtemp(prefix='vote-brevo-photos-'))
TEMP_ENV_DIR = Path(tempfile.mkdtemp(prefix='vote-brevo-env-'))

with socket.socket() as probe:
    probe.bind(('127.0.0.1', 0))
    PORT = probe.getsockname()[1]
BASE = f'http://127.0.0.1:{PORT}'


def find_app_dll():
    release_dll = ROOT / 'bin/Release/net10.0/voting.dll'
    debug_dll = ROOT / 'bin/Debug/net10.0/voting.dll'
    if release_dll.exists():
        return release_dll
    if debug_dll.exists():
        return debug_dll
    raise FileNotFoundError('Build voting.csproj (Debug or Release) before running brevo_dotenv_smoke.py')


APP_DLL = find_app_dll()
APP = ['dotnet', str(APP_DLL)]


def sql(query, master=False):
    return subprocess.check_output(
        ['sqlcmd', '-b', '-S', 'localhost', '-E', '-C', '-d', 'master' if master else DB, '-h', '-1', '-W', '-Q', 'SET NOCOUNT ON; ' + query],
        text=True,
    ).strip()


def run_ps(script_text, extra_env=None):
    env = {**os.environ, **(extra_env or {})}
    return subprocess.run(
        ['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script_text],
        cwd=ROOT,
        env=env,
        text=True,
        capture_output=True,
        check=True,
    )


def parse_dotenv_via_ps(dotenv_path, overwrite=False, extra_env=None, keys=None):
    keys = keys or []
    flag = ' -Overwrite' if overwrite else ''
    key_list = ','.join(f"'{k}'" for k in keys)
    ps = (
        f". '{ROOT / 'scripts/import-dotenv.ps1'}' -Path '{dotenv_path}'{flag}; "
        f"$out = @{{}}; "
        f"foreach ($k in @({key_list})) {{ $out[$k] = [System.Environment]::GetEnvironmentVariable($k, 'Process') }}; "
        f"$out | ConvertTo-Json -Compress"
    )
    res = run_ps(ps, extra_env=extra_env)
    return json.loads(res.stdout)


def check_gitignore_and_env_example():
    print('1. Checking .gitignore rules and .env.example defaults...')
    ignored = ['.env', '.env.local', '.env.production', 'private.env', 'App_Data/dev-emails/sample.eml', 'sample.eml']
    for rel in ignored:
        r = subprocess.run(['git', 'check-ignore', '-q', rel], cwd=ROOT)
        assert r.returncode == 0, f'Expected {rel} to be ignored by git'

    r_ex = subprocess.run(['git', 'check-ignore', '-q', '.env.example'], cwd=ROOT)
    assert r_ex.returncode == 1, '.env.example must NOT be ignored by git'

    example_text = (ROOT / '.env.example').read_text(encoding='utf-8')
    for expected in [
        'ASPNETCORE_ENVIRONMENT=Development',
        'Email__SmtpHost=smtp-relay.brevo.com',
        'Email__SmtpPort=587',
        'Email__UseSsl=true',
        'Email__SmtpUsername=',
        'Email__SmtpPassword=',
        'Email__FromAddress=',
        'Email__FromName=ACMCL Vote',
        'Email__PickupDirectory=',
    ]:
        assert expected in example_text, f'Missing line in .env.example: {expected}'

    # Ensure blank placeholders in .env.example
    for blank_key in ['Email__SmtpUsername', 'Email__SmtpPassword', 'Email__FromAddress', 'Email__PickupDirectory', 'AdminAccount__InvitationCode']:
        m = re.search(rf'^{blank_key}=(.*)$', example_text, flags=re.MULTILINE)
        assert m and m.group(1).strip() == '', f'{blank_key} must be blank in .env.example'


def check_dotenv_loader():
    print('2. Checking scripts/import-dotenv.ps1 parsing, escaping, malformed lines, and precedence...')
    # Load .env.example into clean PowerShell environment
    keys_example = [
        'ASPNETCORE_ENVIRONMENT',
        'Email__SmtpHost',
        'Email__SmtpPort',
        'Email__UseSsl',
        'Email__SmtpUsername',
        'Email__SmtpPassword',
        'Email__FromAddress',
        'Email__FromName',
        'Email__PickupDirectory',
    ]
    clean_env = {k: v for k, v in os.environ.items() if not k.startswith(('Email__', 'ASPNETCORE_', 'ConnectionStrings__', 'AdminAccount__'))}
    # Use clean process env by clearing keys in PS before dot-sourcing
    clear_cmds = '; '.join(f"[System.Environment]::SetEnvironmentVariable('{k}', $null, 'Process')" for k in keys_example)
    key_list = ','.join(f"'{k}'" for k in keys_example)
    ps_ex = (
        f"{clear_cmds}; "
        f". '{ROOT / 'scripts/import-dotenv.ps1'}' -Path '{ROOT / '.env.example'}'; "
        f"$out = @{{}}; foreach ($k in @({key_list})) {{ $out[$k] = [System.Environment]::GetEnvironmentVariable($k, 'Process') }}; "
        f"$out | ConvertTo-Json -Compress"
    )
    loaded_ex = json.loads(subprocess.run(
        ['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', ps_ex],
        cwd=ROOT, env=clean_env, text=True, capture_output=True, check=True,
    ).stdout)
    assert loaded_ex['ASPNETCORE_ENVIRONMENT'] == 'Development'
    assert loaded_ex['Email__SmtpHost'] == 'smtp-relay.brevo.com'
    assert loaded_ex['Email__SmtpPort'] == '587'
    assert loaded_ex['Email__UseSsl'] == 'true'
    assert loaded_ex['Email__FromName'] == 'ACMCL Vote'
    assert loaded_ex['Email__SmtpUsername'] is None
    assert loaded_ex['Email__SmtpPassword'] is None
    assert loaded_ex['Email__FromAddress'] is None
    assert loaded_ex['Email__PickupDirectory'] is None

    # Synthetic dotenv with spaces, '=', '#', '\', '$', export, quotes, and malformed lines
    synthetic_env = TEMP_ENV_DIR / 'synthetic.env'
    synthetic_env.write_text(
        '\n'.join([
            '# Comment line',
            '   ',
            'malformed_without_equals',
            '=missing_key',
            '123INVALID=badkey',
            'export TEST_PLAIN=synthetic value with spaces',
            'TEST_EQUALS_HASH=part1=part2#notacomment\\tail$literal',
            'TEST_BACKSLASH=Server=.\\SQLEXPRESS;Database=ACMCLVoting;Integrated Security=True',
            'TEST_DQUOTED="  quoted space # hash = eq \\ slash $(not-eval)  "',
            "TEST_SQUOTED='single # = \\ $raw'",
            'TEST_PRECEDENCE=from-file',
            'TEST_EMPTY_CLEARS=',
        ]),
        encoding='utf-8',
    )

    test_keys = [
        'TEST_PLAIN', 'TEST_EQUALS_HASH', 'TEST_BACKSLASH',
        'TEST_DQUOTED', 'TEST_SQUOTED', 'TEST_PRECEDENCE', 'TEST_EMPTY_CLEARS', '123INVALID',
    ]
    # Without -Overwrite: existing TEST_PRECEDENCE and TEST_EMPTY_CLEARS stay intact
    res_no_overwrite = parse_dotenv_via_ps(
        synthetic_env,
        overwrite=False,
        extra_env={'TEST_PRECEDENCE': 'from-process', 'TEST_EMPTY_CLEARS': 'keep-me'},
        keys=test_keys,
    )
    assert res_no_overwrite['TEST_PLAIN'] == 'synthetic value with spaces'
    assert res_no_overwrite['TEST_EQUALS_HASH'] == r'part1=part2#notacomment\tail$literal'
    assert res_no_overwrite['TEST_BACKSLASH'] == r'Server=.\SQLEXPRESS;Database=ACMCLVoting;Integrated Security=True'
    assert res_no_overwrite['TEST_DQUOTED'] == r'  quoted space # hash = eq \ slash $(not-eval)  '
    assert res_no_overwrite['TEST_SQUOTED'] == r'single # = \ $raw'
    assert res_no_overwrite['TEST_PRECEDENCE'] == 'from-process'
    assert res_no_overwrite['TEST_EMPTY_CLEARS'] == 'keep-me'
    assert res_no_overwrite['123INVALID'] is None

    # With -Overwrite: file overrides process and empty value clears previous process var
    res_overwrite = parse_dotenv_via_ps(
        synthetic_env,
        overwrite=True,
        extra_env={'TEST_PRECEDENCE': 'from-process', 'TEST_EMPTY_CLEARS': 'clear-me'},
        keys=test_keys,
    )
    assert res_overwrite['TEST_PRECEDENCE'] == 'from-file'
    assert res_overwrite['TEST_EMPTY_CLEARS'] is None


def start_app(env_overrides):
    base_env = {
        **os.environ,
        'ASPNETCORE_ENVIRONMENT': 'Development',
        'ConnectionStrings__VotingDb': f'Server=localhost;Database={DB};Integrated Security=True;TrustServerCertificate=True',
        'AdminAccount__InvitationCode': BOOTSTRAP,
        'CandidatePhotos__StoragePath': str(PHOTO_DIR),
        'EmailVerification__ResendCooldownSeconds': '0',
        'EmailVerification__MaxSendsPerWindow': '20',
    }
    for k in ['Email__SmtpHost', 'Email__SmtpPort', 'Email__UseSsl', 'Email__SmtpUsername', 'Email__SmtpPassword', 'Email__FromAddress', 'Email__FromName', 'Email__PickupDirectory']:
        base_env.pop(k, None)
    env = {**base_env, **env_overrides}
    log_handle = LOGFILE.open('a', encoding='utf-8')
    proc = subprocess.Popen(APP + ['--urls', BASE], cwd=ROOT, env=env, stdout=log_handle, stderr=subprocess.STDOUT)
    for _ in range(100):
        try:
            if requests.get(BASE + '/Account/Login', timeout=1).status_code == 200:
                return proc, log_handle
        except requests.RequestException:
            pass
        assert proc.poll() is None, 'App exited unexpectedly; check .agent-logs/brevo-dotenv-tests.log'
        time.sleep(0.15)
    raise AssertionError('App did not start in time')


def stop_app(proc, log_handle):
    if proc and proc.poll() is None:
        proc.terminate()
        try:
            proc.wait(timeout=15)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=10)
    if log_handle:
        log_handle.close()


def token(session, path):
    r = session.get(BASE + path, timeout=15)
    assert r.status_code == 200, (path, r.status_code)
    m = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', r.text)
    assert m, f'Missing CSRF token on {path}'
    return html.unescape(m.group(1))


def post(session, path, data, source):
    csrf = token(session, source)
    return session.post(BASE + path, data={'__RequestVerificationToken': csrf, **data}, allow_redirects=False, timeout=15)


def check_email_service_modes():
    print('3. Creating isolated database and checking EmailService Brevo validation & test sink...')
    sql(f'CREATE DATABASE [{DB}];', master=True)
    migrate_env = {
        **os.environ,
        'ASPNETCORE_ENVIRONMENT': 'Development',
        'ConnectionStrings__VotingDb': f'Server=localhost;Database={DB};Integrated Security=True;TrustServerCertificate=True',
    }
    with LOGFILE.open('a', encoding='utf-8') as mf:
        subprocess.run(APP + ['--migrate'], cwd=ROOT, env=migrate_env, check=True, stdout=mf, stderr=subprocess.STDOUT)

    # Case A: .env.example defaults (Brevo host set, username/password/from blank) -> actionable credential error, no .eml written
    proc, lh = start_app({
        'Email__SmtpHost': 'smtp-relay.brevo.com',
        'Email__SmtpPort': '587',
        'Email__UseSsl': 'true',
        'Email__SmtpUsername': '',
        'Email__SmtpPassword': '',
        'Email__FromAddress': '',
        'Email__PickupDirectory': '',
    })
    try:
        s = requests.Session()
        r_reg = post(s, '/Account/Register', {
            'Role': 'Student', 'FullName': 'Brevo Setup Check', 'Email': 'brevo-check@example.test',
            'Password': PASSWORD, 'ConfirmPassword': PASSWORD,
        }, '/Account/Register')
        assert r_reg.status_code == 302 and '/Account/VerifyEmail' in r_reg.headers['Location']
        verify_page = s.get(BASE + r_reg.headers['Location'], timeout=15).text
        assert 'could not send the verification email' in verify_page.lower()
        assert list(PICKUP_DIR.glob('*.eml')) == [], 'No .eml should be written when Brevo SMTP is configured'
        log_text = LOGFILE.read_text(encoding='utf-8')
        assert 'SMTP credentials are missing' in log_text
    finally:
        stop_app(proc, lh)

    # Case B: @smtp-brevo.com login mistakenly used as Email__FromAddress -> actionable sender error
    proc, lh = start_app({
        'Email__SmtpHost': 'smtp-relay.brevo.com',
        'Email__SmtpPort': '587',
        'Email__UseSsl': 'true',
        'Email__SmtpUsername': 'synthetic-login@smtp-brevo.com',
        'Email__SmtpPassword': 'synthetic-smtp-key-value',
        'Email__FromAddress': 'synthetic-login@smtp-brevo.com',
        'Email__PickupDirectory': '',
    })
    try:
        r_forgot = post(requests.Session(), '/Account/ForgotPassword', {'email': 'brevo-check@example.test'}, '/Account/ForgotPassword')
        assert r_forgot.status_code == 200 and 'could not send the recovery email' in r_forgot.text.lower()
        log_text = LOGFILE.read_text(encoding='utf-8')
        assert "not the '@smtp-brevo.com' SMTP login" in log_text
    finally:
        stop_app(proc, lh)

    # Case C: Both Email__SmtpHost and Email__PickupDirectory set -> ambiguous mode error
    proc, lh = start_app({
        'Email__SmtpHost': 'smtp-relay.brevo.com',
        'Email__PickupDirectory': str(PICKUP_DIR),
    })
    try:
        r_ambig = post(requests.Session(), '/Account/ForgotPassword', {'email': 'brevo-check@example.test'}, '/Account/ForgotPassword')
        assert r_ambig.status_code == 200 and 'could not send the recovery email' in r_ambig.text.lower()
        log_text = LOGFILE.read_text(encoding='utf-8')
        assert 'cannot enable both Email:SmtpHost and Email:PickupDirectory' in log_text
    finally:
        stop_app(proc, lh)

    # Case D: Production rejects Email__PickupDirectory
    proc, lh = start_app({
        'ASPNETCORE_ENVIRONMENT': 'Production',
        'Email__SmtpHost': '',
        'Email__PickupDirectory': str(PICKUP_DIR),
    })
    try:
        r_prod = post(requests.Session(), '/Account/ForgotPassword', {'email': 'brevo-check@example.test'}, '/Account/ForgotPassword')
        assert r_prod.status_code == 200 and 'could not send the recovery email' in r_prod.text.lower()
        log_text = LOGFILE.read_text(encoding='utf-8')
        assert 'Email:PickupDirectory is for local testing only and cannot be used in Production' in log_text
    finally:
        stop_app(proc, lh)

    # Case E: Explicit local pickup in Development works when Email__SmtpHost is cleared
    proc, lh = start_app({
        'ASPNETCORE_ENVIRONMENT': 'Development',
        'Email__SmtpHost': '',
        'Email__PickupDirectory': str(PICKUP_DIR),
    })
    try:
        s = requests.Session()
        r_resend = post(s, '/Account/ResendVerification', {'email': 'brevo-check@example.test'}, '/Account/VerifyEmail')
        assert r_resend.status_code == 302
        emls = list(PICKUP_DIR.glob('*.eml'))
        assert len(emls) == 1
        eml_text = emls[0].read_text(encoding='utf-8')
        assert 'To: <brevo-check@example.test>' in eml_text
        code = re.search(r'\b(\d{6})\b', eml_text.split('\n\n', 1)[1]).group(1)
        r_verify = post(s, '/Account/VerifyEmail', {'Email': 'brevo-check@example.test', 'Code': code}, '/Account/VerifyEmail')
        assert r_verify.status_code == 302 and r_verify.headers['Location'] == '/Student/Index'
    finally:
        stop_app(proc, lh)


try:
    check_gitignore_and_env_example()
    check_dotenv_loader()
    check_email_service_modes()
    print('PASS: all Brevo SMTP configuration, .gitignore, .env.example, import-dotenv.ps1, and EmailService checks passed!')
finally:
    sql(f"IF DB_ID(N'{DB}') IS NOT NULL DROP DATABASE [{DB}];", master=True)
    shutil.rmtree(PICKUP_DIR, ignore_errors=True)
    shutil.rmtree(PHOTO_DIR, ignore_errors=True)
    shutil.rmtree(TEMP_ENV_DIR, ignore_errors=True)
    print('Cleaned up isolated test database and temporary directories.')
