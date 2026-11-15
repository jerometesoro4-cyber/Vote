"""Local HTTP smoke test. Requires requests and VOTE_TEST_* credentials.
Creates one uniquely named test election; removes only that election in finally.
SQLCMD uses local Windows authentication for fixture lookup/cleanup.
"""
import os, re, subprocess, uuid
from datetime import datetime, timedelta, timezone
import requests
BASE = os.environ.get('VOTE_TEST_URL', 'http://127.0.0.1:5189')
def sql(query):
    return subprocess.check_output(['sqlcmd','-b','-S','localhost','-E','-C','-d','ACMCLVoting','-h','-1','-W','-Q','SET NOCOUNT ON; '+query],text=True).strip()
def token(s, path):
    r=s.get(BASE+path); assert r.status_code==200,(path,r.status_code)
    return re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"',r.text).group(1)
def post(s,path,source,data):
    return s.post(BASE+path,data={**data,'__RequestVerificationToken':token(s,source)},allow_redirects=False)
def login(role):
    s=requests.Session(); path='/Account/'+role+'Login'
    r=post(s,path,path,{'username' if role=='Admin' else 'email':os.environ['VOTE_TEST_'+role.upper()+'_EMAIL'],'password':os.environ['VOTE_TEST_'+role.upper()+'_PASSWORD']})
    assert r.status_code==302,(role,r.status_code); return s
admin=login('Admin'); student=login('Student'); name='SMOKE-'+uuid.uuid4().hex; election_id=None
try:
    for page in ['Index','Elections','Positions','Candidates','Users','Monitoring','Results','Settings']:
        assert admin.get(BASE+'/Admin/'+page).status_code==200,page
    assert student.get(BASE+'/Admin/Index',allow_redirects=False).status_code==302
    assert admin.post(BASE+'/Admin/CreateElection',data={'name':'csrf'}).status_code==400
    now=datetime.now(timezone.utc)
    r=post(admin,'/Admin/CreateElection','/Admin/Elections',{'name':name,'startsAtUtc':(now-timedelta(hours=1)).strftime('%Y-%m-%dT%H:%M'),'endsAtUtc':(now+timedelta(hours=1)).strftime('%Y-%m-%dT%H:%M')});assert r.status_code==302
    election_id=int(sql("SELECT Id FROM Elections WHERE Name='"+name+"'"))
    for position in ['President','Secretary']:
        post(admin,'/Admin/AddPosition','/Admin/Positions',{'electionId':election_id,'name':position})
    positions=[int(i) for i in sql(f'SELECT Id FROM Positions WHERE ElectionId={election_id} ORDER BY Id').split()]
    assert len(positions)==2
    choices={}
    for pos in positions:
        post(admin,'/Admin/AddCandidate','/Admin/Candidates',{'positionId':pos,'name':name+' Candidate','party':'Test','platform':'Test fixture'})
        choices[pos]=int(sql(f'SELECT Id FROM Candidates WHERE PositionId={pos}'))
    post(admin,'/Admin/ChangeStatus','/Admin/Elections',{'id':election_id,'status':'Open'})
    path='/Student/Vote/'+str(election_id)
    assert student.get(BASE+path).status_code==200
    bad=post(student,'/Student/Vote',path,{'electionId':election_id,'candidateIds':99999999});assert bad.status_code==302
    assert sql(f'SELECT COUNT(*) FROM Ballots WHERE ElectionId={election_id}')=='0'
    fields={'electionId':election_id,**{f'choices[{p}]':c for p,c in choices.items()}}
    r=post(student,'/Student/Vote',path,fields);assert r.status_code==302 and 'History' in r.headers.get('Location','')
    post(student,'/Student/Vote',path,fields)
    assert sql(f'SELECT COUNT(*) FROM Ballots WHERE ElectionId={election_id}')=='1'
    assert name not in requests.get(BASE+'/PublicPortal/Results').text
    post(admin,'/Admin/ChangeStatus','/Admin/Elections',{'id':election_id,'status':'Closed'})
    assert name in requests.get(BASE+'/PublicPortal/Results').text
    assert name in student.get(BASE+'/Student/History').text
    for page in ['Index','ActiveElections','Candidates','History','Results']:
        assert student.get(BASE+'/Student/'+page).status_code==200,page
    print('PASS: both roles, all dashboards/pages, CSRF, election setup, invalid ballot, valid ballot, duplicate rejection, closed results and history')
finally:
    if election_id:
        sql(f'BEGIN TRANSACTION; DELETE FROM BallotSelections WHERE BallotId IN (SELECT Id FROM Ballots WHERE ElectionId={election_id}); DELETE FROM Ballots WHERE ElectionId={election_id}; DELETE FROM Candidates WHERE PositionId IN (SELECT Id FROM Positions WHERE ElectionId={election_id}); DELETE FROM Positions WHERE ElectionId={election_id}; DELETE FROM Elections WHERE Id={election_id}; COMMIT;')
