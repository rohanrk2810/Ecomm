"""Synthetic SMTP/OIDC/MFA enrollment flow. Never print secrets or bearer URLs."""
import html
import http.cookiejar
import json
import os
import re
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

APP = os.environ.get('PUBLIC_APP_ORIGIN', 'http://localhost:4200').rstrip('/')
IDP = 'http://localhost:5082'
ROOT = Path(__file__).resolve().parents[2]


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        return None


jar = http.cookiejar.CookieJar()
browser = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), NoRedirect())


def request(url, body=None, as_json=False, extra_headers=None):
    data = None if body is None else (json.dumps(body) if as_json else urllib.parse.urlencode(body)).encode()
    headers = {'Content-Type': 'application/json' if as_json else 'application/x-www-form-urlencoded'}
    headers.update(extra_headers or {})
    try:
        response = browser.open(urllib.request.Request(url, data=data, headers=headers), timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    return response.code, response.headers, response.read().decode()


def inputs(page):
    result = {}
    for element in re.findall(r'<input[^>]+>', page):
        name = re.search(r'name="([^"]+)"', element)
        value = re.search(r'value="([^"]*)"', element)
        if name:
            result[name[1]] = html.unescape(value[1]) if value else ''
    return result


def redirect(result, origin):
    assert result[0] == 302, 'Expected browser redirect, received ' + str(result[0])
    return urllib.parse.urljoin(origin, result[1]['Location'])


email = 'oidc-smoke-' + uuid.uuid4().hex + '@example.invalid'
password = 'Synthetic!' + uuid.uuid4().hex + '8'
assert request('http://localhost:5080/api/v1/identity/registrations', {'email': email, 'password': password}, True)[0] == 202
deadline = time.monotonic() + 40
message = None
while time.monotonic() < deadline:
    summaries = json.loads(request('http://localhost:8025/api/v1/messages')[2])['messages']
    summary = next((item for item in summaries if any(person['Address'] == email for person in item['To'])), None)
    if summary:
        message = json.loads(request('http://localhost:8025/api/v1/message/' + summary['ID'])[2])
        break
    time.sleep(.5)
assert message, 'Synthetic verification mail was not delivered'
link = re.findall(r'http://localhost:4200/verify-email#[^\s]+', message['Text'])[0]
values = urllib.parse.parse_qs(urllib.parse.urlsplit(link).fragment)
assert request('http://localhost:5080/api/v1/identity/email-verifications', {'userId': values['user'][0], 'token': values['token'][0]}, True)[0] == 200

authorize = redirect(request(APP + '/bff/login'), APP)
assert urllib.parse.parse_qs(urllib.parse.urlsplit(authorize).query)['code_challenge_method'] == ['S256']
login = redirect(request(authorize), IDP)
form = inputs(request(login)[2])
form.update({'Email': email, 'Password': password})
authorize = redirect(request(login, form), IDP)
callback = redirect(request(authorize), IDP)
assert urllib.parse.urlsplit(callback).path == '/signin-oidc'
redirect(request(callback), APP)
session = json.loads(request(APP + '/bff/session')[2])
assert session['authenticated'] is True
cookies = [cookie for cookie in jar if cookie.name == 'commerce.bff.session']
assert cookies and all('httponly' in {name.lower() for name in cookie._rest} for cookie in cookies)
print('PASS real SMTP verification -> OIDC S256 authorization code -> opaque BFF cookie', flush=True)


import base64, hashlib, hmac, struct
page = request(IDP + '/account/mfa')
form = inputs(page[2]); form.update({'Email':email,'Password':password})
assert request(IDP + '/account/mfa?handler=Begin', {'Email':email,'Password':password})[0] == 400
begin = request(IDP + '/account/mfa?handler=Begin', form)
assert begin[0] == 200 and 'no-store' in begin[1].get('Cache-Control','')
key = re.search(r'id="setup-key">([^<]+)', begin[2])[1]
assert json.loads(request(APP + '/bff/session')[2])['authenticated'] is False
counter = struct.pack('>Q', int(time.time()) // 30)
digest = hmac.new(base64.b32decode(key + '='*((8-len(key)%8)%8)), counter, hashlib.sha1).digest()
offset = digest[-1]&15
code = str((struct.unpack('>I',digest[offset:offset+4])[0]&0x7fffffff)%1000000).zfill(6)
form=inputs(begin[2]); form.update({'Email':email,'Password':password,'Code':code})
confirmed=request(IDP + '/account/mfa?handler=Confirm',form)
assert confirmed[0] == 200
codes=re.findall(r'<li><code>([^<]+)</code></li>',confirmed[2])
assert len(codes)==10 and key not in confirmed[2]
fresh_page = request(IDP + '/account/mfa')[2]
assert all(code not in fresh_page for code in codes)
print('PASS password reauthentication, enrollment CSRF, protected setup, session invalidation and one-time recovery display',flush=True)
authorize=redirect(request(APP + '/bff/login'),APP)
login=redirect(request(authorize),IDP)
form=inputs(request(login)[2]); form.update({'Email':email,'Password':password})
assert request(login,form)[0]==400
form['RecoveryCode']=codes[0]
authorize=redirect(request(login,form),IDP)
callback=redirect(request(authorize),IDP)
redirect(request(callback),APP)
final_session = json.loads(request(APP+'/bff/session')[2])
assert final_session['authenticated'] is True and final_session['mfaVerified'] is True
print('PASS enrolled account rejects password alone and completes OIDC with a recovery code',flush=True)
