"""Check shared registration rendering and invalid submissions; creates no accounts."""
import os,re
import requests
base=os.environ.get('VOTE_TEST_URL','http://127.0.0.1:5189')
s=requests.Session()
def token(path):
 r=s.get(base+path);assert r.status_code==200
 return re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"',r.text).group(1)
def post(data):
 return s.post(base+'/Account/Register',data={**data,'__RequestVerificationToken':token('/Account/Register')},allow_redirects=False)
page=s.get(base+'/Account/Login').text
assert page.count('Create an account')==1 and 'Administrator registration</a>' not in page
for old,role in [('StudentSignup','Student'),('AdminSignup','Admin')]:
 r=s.get(base+'/Account/'+old);assert r.status_code==200 and 'name="Role"' in r.text and f'value="{role}" selected="selected"' in r.text
assert s.post(base+'/Account/Register',data={}).status_code==400
common={'FullName':'','Email':'invalid','Password':'x','ConfirmPassword':'y'}
r=post({**common,'Role':'Owner'});assert r.status_code==200 and 'Choose a valid account type' in r.text
r=post({**common,'Role':'Admin','InvitationCode':''});assert r.status_code==200 and ('invitation code is invalid' in r.text or 'Administrator registration is not configured' in r.text)
assert 'value="Admin" selected="selected"' in r.text
r=post({**common,'Role':'Student','FullName':'Preserved Name'});assert r.status_code==200 and 'Preserved Name' in r.text and 'passwords do not match' in r.text
print('PASS: one create-account link, shared role selector, legacy redirects, role validation, invitation requirement, password confirmation, CSRF and field retention')
