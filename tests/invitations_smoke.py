"""Integration checks in an isolated SQL Server database; never targets ACMCLVoting.
Requires a built Debug app, requests, SQLCMD, and Windows SQL Server permissions.
The random test database and temporary localhost process are removed in finally.
"""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import hashlib
import html
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import threading
import time
import requests

ROOT=Path(__file__).resolve().parents[1]
DB='ACMCLVoting_InvitationTests_'+secrets.token_hex(8)
assert re.fullmatch(r'ACMCLVoting_InvitationTests_[0-9a-f]{16}',DB)
BOOTSTRAP=secrets.token_urlsafe(32)
PASSWORD=secrets.token_urlsafe(24)
with socket.socket() as probe:
    probe.bind(('127.0.0.1',0))
    PORT=probe.getsockname()[1]
BASE=f'http://127.0.0.1:{PORT}'
ENV={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development',
     'ConnectionStrings__VotingDb':f'Server=localhost;Database={DB};Integrated Security=True;TrustServerCertificate=True',
     'AdminAccount__InvitationCode':BOOTSTRAP}
APP=['dotnet',str(ROOT/'bin/Debug/net10.0/voting.dll')]
LOGDIR=ROOT/'.agent-logs';LOGDIR.mkdir(exist_ok=True)

def sql(query,master=False):
    return subprocess.check_output(['sqlcmd','-b','-S','localhost','-E','-C','-d','master' if master else DB,'-h','-1','-W','-Q','SET NOCOUNT ON; '+query],text=True).strip()
def token(session,path):
    r=session.get(BASE+path);assert r.status_code==200,(path,r.status_code)
    m=re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"',r.text)
    assert m,path
    return html.unescape(m.group(1))
def post(session,path,data,source='/Admin/Settings'):
    return session.post(BASE+path,data={**data,'__RequestVerificationToken':token(session,source)},allow_redirects=False)
def register(email,code='',role='Admin',session=None,legacy=False):
    session=session or requests.Session()
    data={'Role':role,'FullName':'Invitation Test','Email':email,'Password':PASSWORD,'ConfirmPassword':PASSWORD,'InvitationCode':code}
    r=post(session,'/Account/AdminSignup' if legacy else '/Account/Register',data,'/Account/Register')
    return session,r

def generate(session):
    r=post(session,'/Admin/GenerateInvitation',{});assert r.status_code==302
    r=session.get(BASE+r.headers['Location']);assert r.status_code==200
    assert 'no-store' in r.headers.get('Cache-Control','')
    match=re.search(r'id="invitation-code"[^>]*value="([^"]+)"',r.text)
    assert match,'New code was not shown'
    code=html.unescape(match.group(1));assert re.fullmatch('[0-9A-F]{64}',code)
    digest=hashlib.sha256(code.encode()).hexdigest().upper()
    invitation_id=int(sql(f"SELECT Id FROM AdminInvitations WHERE CodeHash='{digest}'"))
    assert sql(f'SELECT DATEDIFF(day,CreatedAtUtc,ExpiresAtUtc) FROM AdminInvitations WHERE Id={invitation_id}')=='7'
    again=session.get(BASE+'/Admin/Settings');assert 'id="invitation-code"' not in again.text and code not in again.text
    assert digest not in again.text,'Hash leaked in UI'
    return code,invitation_id

process=None
try:
    with (LOGDIR/'invitation-tests.log').open('w',encoding='utf-8') as log:
        subprocess.run(APP+['--migrate'],cwd=ROOT,env=ENV,check=True,stdout=log,stderr=subprocess.STDOUT)
        process=subprocess.Popen(APP+['--urls',BASE],cwd=ROOT,env=ENV,stdout=log,stderr=subprocess.STDOUT)
        for _ in range(100):
            try:
                if requests.get(BASE+'/Account/Login',timeout=1).status_code==200:break
            except requests.RequestException:pass
            assert process.poll() is None,'App exited: see .agent-logs/invitation-tests.log'
            time.sleep(.15)
        else:raise AssertionError('App did not start')
        admin,r=register('first@example.test',BOOTSTRAP);assert r.status_code==302 and '/Admin' in r.headers['Location']
        _,r=register('bootstrap-reuse@example.test',BOOTSTRAP);assert r.status_code==200 and 'invitation code is invalid' in r.text
        _,r=register('legacy-bootstrap@example.test',BOOTSTRAP,legacy=True);assert r.status_code==200 and 'invitation code is invalid' in r.text
        student,r=register('student@example.test',role='Student');assert r.status_code==302
        assert requests.get(BASE+'/Admin/Settings',allow_redirects=False).status_code==302
        assert student.get(BASE+'/Admin/Settings',allow_redirects=False).status_code==302
        assert post(student,'/Admin/GenerateInvitation',{},'/Student/Index').status_code==302
        assert admin.post(BASE+'/Admin/GenerateInvitation',data={}).status_code==400
        code,invitation_id=generate(admin)
        _,r=register('student@example.test',code);assert r.status_code==200 and 'already exists' in r.text
        assert sql(f'SELECT COUNT(*) FROM AdminInvitations WHERE Id={invitation_id} AND UsedAtUtc IS NULL')=='1','Duplicate signup consumed code'
        invitee,r=register('invited@example.test',code);assert r.status_code==302 and '/Admin' in r.headers['Location']
        assert sql(f'SELECT COUNT(*) FROM AdminInvitations WHERE Id={invitation_id} AND UsedAtUtc IS NOT NULL')=='1'
        _,r=register('reuse@example.test',code);assert r.status_code==200 and 'invitation code is invalid' in r.text
        expired,expired_id=generate(admin)
        sql(f'UPDATE AdminInvitations SET ExpiresAtUtc=DATEADD(day,-1,SYSUTCDATETIME()) WHERE Id={expired_id}')
        _,r=register('expired@example.test',expired);assert r.status_code==200 and 'invitation code is invalid' in r.text
        revoked,revoked_id=generate(admin)
        assert post(admin,'/Admin/RevokeInvitation',{'id':revoked_id}).status_code==302
        _,r=register('revoked@example.test',revoked);assert r.status_code==200 and 'invitation code is invalid' in r.text
        assert 'Revoked' in admin.get(BASE+'/Admin/Settings').text
        concurrent,concurrent_id=generate(invitee)
        barrier=threading.Barrier(2)
        def redeem(index):
            session=requests.Session();csrf=token(session,'/Account/Register');barrier.wait(timeout=10)
            return session.post(BASE+'/Account/Register',data={'__RequestVerificationToken':csrf,'Role':'Admin','FullName':'Concurrent Test','Email':f'concurrent{index}@example.test','Password':PASSWORD,'ConfirmPassword':PASSWORD,'InvitationCode':concurrent},allow_redirects=False).status_code
        with ThreadPoolExecutor(max_workers=2) as executor:
            statuses=list(executor.map(redeem,[1,2]))
        assert statuses.count(302)==1 and all(status in [200,302] for status in statuses),statuses
        assert sql("SELECT COUNT(*) FROM user_accounts WHERE email LIKE 'concurrent%@example.test'")=='1'
        legacy,legacy_id=generate(admin)
        _,r=register('legacy-invited@example.test',legacy,legacy=True);assert r.status_code==302
        _,r=register('legacy-used@example.test',legacy,legacy=True);assert r.status_code==200 and 'invitation code is invalid' in r.text
        print('PASS: first-admin bootstrap, bootstrap lockout, admin-only generation, hashed code storage, shown-once code, 7-day expiry, duplicate rollback, use/reuse, revocation, invited-admin delegation, concurrent redemption and legacy routes.')
finally:
    if process and process.poll() is None:
        process.terminate()
        try:process.wait(timeout=15)
        except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
    sql(f"IF DB_ID(N'{DB}') IS NOT NULL DROP DATABASE [{DB}];",master=True)
    print('Cleaned up the isolated test database and temporary app. Main database was not used.')
