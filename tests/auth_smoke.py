"""Read-only account checks. Requires requests and VOTE_TEST_* credentials."""
import os,re
import requests
BASE=os.environ.get('VOTE_TEST_URL','http://127.0.0.1:5189')
def token(session,path):
 r=session.get(BASE+path); assert r.status_code==200,(path,r.status_code)
 return re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"',r.text).group(1)
for role in ['ADMIN','STUDENT']:
 session=requests.Session()
 for path in ['/','/Account/Login','/Account/'+role.title()+'Login','/Account/ChooseRole']:
  response=session.get(BASE+path);assert response.status_code==200 and '<h1>Sign in</h1>' in response.text,path
 email=os.environ['VOTE_TEST_'+role+'_EMAIL'];password=os.environ['VOTE_TEST_'+role+'_PASSWORD']
 assert session.post(BASE+'/Account/Login',data={'email':email,'password':password}).status_code==400
 response=session.post(BASE+'/Account/Login',data={'__RequestVerificationToken':token(session,'/Account/Login'),'email':email,'password':'incorrect'},allow_redirects=False)
 assert response.status_code==200 and 'Invalid email or password' in response.text and email in response.text
 response=session.post(BASE+'/Account/Login',data={'__RequestVerificationToken':token(session,'/Account/Login'),'email':email.upper(),'password':password,'returnUrl':'https://example.com/'},allow_redirects=False)
 expected='/'+role.title()+'/Index'
 assert response.status_code==302 and response.headers['Location']==expected,(role,response.status_code,response.headers.get('Location'))
 assert session.get(BASE+expected).status_code==200
 response=session.post(BASE+'/Account/Logout',data={'__RequestVerificationToken':token(session,expected)},allow_redirects=False)
 assert response.status_code==302
 assert '<h1>Sign in</h1>' in session.get(BASE+response.headers['Location']).text
 assert session.get(BASE+expected,allow_redirects=False).status_code==302
for path in ['/Account/StudentSignup','/Account/AdminSignup']:
 response=requests.get(BASE+path);assert response.status_code==200 and 'auth-shell' in response.text
print('PASS: unified login for both roles, case-insensitive email, wrong password, email retention, CSRF, external redirect rejection, dashboard access, logout and signup pages')
