"""Synthetic local SMTP password recovery; never prints credentials or bearer links."""
import json, re, time, uuid, urllib.request, urllib.error, urllib.parse
BASE='http://localhost:5080/api/v1/identity/'
def get(url):
    with urllib.request.urlopen(url,timeout=15) as r: return json.load(r)
def post(path, body):
    try:
        with urllib.request.urlopen(urllib.request.Request(BASE+path,data=json.dumps(body).encode(),headers={'Content-Type':'application/json'}),timeout=20) as r: return r.status
    except urllib.error.HTTPError as e: return e.code
def mail(email, path):
    deadline=time.monotonic()+40
    while time.monotonic()<deadline:
        for row in get('http://localhost:8025/api/v1/messages')['messages']:
            if any(x['Address']==email for x in row['To']):
                text=get('http://localhost:8025/api/v1/message/'+row['ID'])['Text']
                links=re.findall(r'http://localhost:4200/'+path+r'#[^\s]+',text)
                if links: return urllib.parse.parse_qs(urllib.parse.urlsplit(links[0]).fragment)
        time.sleep(.5)
    raise AssertionError('Synthetic mail not delivered')
email='reset-smoke-'+uuid.uuid4().hex+'@example.invalid'
assert post('registrations',{'email':email,'password':'Synthetic!'+uuid.uuid4().hex+'8'})==202
v=mail(email,'verify-email')
assert post('email-verifications',{'userId':v['user'][0],'token':v['token'][0]})==200
assert post('password-reset-requests',{'email':email})==202
v=mail(email,'reset-password')
body={'userId':v['user'][0],'token':v['token'][0],'newPassword':'Replacement!'+uuid.uuid4().hex+'8'}
assert post('password-resets',body)==200
assert post('password-resets',body)==400
print('PASS real SMTP recovery delivery, successful password reset and single-use rejection')
