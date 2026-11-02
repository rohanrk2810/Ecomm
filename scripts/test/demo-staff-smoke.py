"""Local demo account smoke. Reads ignored credentials; never prints them or resets MFA."""
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



import sys
config=json.loads((ROOT/'.tools/demo-staff-config.json').read_text(encoding='utf-8-sig'))
authorize=redirect(request(APP+'/bff/login'),APP)
login=redirect(request(authorize),IDP)
form=inputs(request(login)[2]);form.update({'Email':config['Email'],'Password':config['Password']})
response=request(login,form)
if response[0]==400:
    print('SKIP interactive MFA/account availability requires user action; no credential was changed')
    sys.exit(0)
authorize=redirect(response,IDP)
callback=redirect(request(authorize),IDP)
redirect(request(callback),APP)
session=json.loads(request(APP+'/bff/session')[2])
assert session['authenticated'] and not session['mfaVerified']
assert request(APP+'/api/v1/identity/staff-access')[0]==403
assert request(IDP+'/account/mfa')[0]==200
print('PASS requested local demo login; staff remains inaccessible until interactive MFA enrollment')
