"""Synthetic local OIDC browser flow. Never print cookies, credentials or protocol URLs."""
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

compose = ['docker', 'compose', '--env-file', str(ROOT / '.env'), '-f', str(ROOT / 'deploy/docker-compose.local.yml')]
subprocess.run(compose + ['restart', 'identity', 'gateway'], check=True, capture_output=True)
deadline = time.monotonic() + 40
while time.monotonic() < deadline:
    try:
        result = request(APP + '/bff/session')
        if result[0] == 200 and json.loads(result[2])['authenticated']:
            break
    except (urllib.error.URLError, OSError, json.JSONDecodeError):
        pass
    time.sleep(.5)
else:
    raise AssertionError('Session did not recover after container restart')
print('PASS persistent certificates, protection keys and Redis session after restart', flush=True)
assert request(APP + '/bff/logout', {})[0] == 400
assert json.loads(request(APP + '/bff/session')[2])['authenticated']
csrf = json.loads(request(APP + '/bff/antiforgery')[2])['requestToken']
old_session = '; '.join(cookie.name + '=' + cookie.value for cookie in jar if cookie.name.startswith('commerce.bff.session'))
logout = redirect(request(APP + '/bff/logout', {'__RequestVerificationToken': csrf}), APP)
assert 'id_token_hint' not in urllib.parse.parse_qs(urllib.parse.urlsplit(logout).query)
page = request(logout)
assert page[0] == 200, 'Provider logout confirmation failed'
callback = redirect(request(IDP + '/connect/logout', inputs(page[2])), IDP)
assert urllib.parse.urlsplit(callback).path == '/signout-callback-oidc', 'Provider did not honor registered logout callback'
redirect(request(callback), APP)
assert json.loads(request(APP + '/bff/session')[2])['authenticated'] is False
assert json.loads(request(APP + '/bff/session', extra_headers={'Cookie': old_session})[2])['authenticated'] is False
assert request(APP + '/signin-oidc?code=forged&state=forged')[0] == 400
print('PASS logout CSRF rejection, provider revocation, logout callback and forged callback rejection', flush=True)
