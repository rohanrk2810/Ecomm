"""Local synthetic email verification test. Never print mail bodies or bearer links."""
import json
import re
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASE = 'http://localhost:5080/api/v1/identity'
COMPOSE = ['docker', 'compose', '--env-file', str(ROOT / '.env'), '-f', str(ROOT / 'deploy/docker-compose.local.yml')]


def get(url):
    with urllib.request.urlopen(url, timeout=10) as response:
        return json.loads(response.read())


def post(path, body):
    request = urllib.request.Request(BASE + path, data=json.dumps(body).encode(),
                                     headers={'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            return response.status, json.loads(response.read())
    except urllib.error.HTTPError as error:
        return error.code, {}


email = 'verification-smoke-' + uuid.uuid4().hex + '@example.invalid'
deadline = time.monotonic() + 30
while True:
    try:
        if get('http://localhost:5082/health/ready').get('status') == 'ready' and get('http://localhost:5080/api/v1/platform/status').get('stage') == 'foundation':
            break
    except (urllib.error.URLError, OSError):
        pass
    if time.monotonic() > deadline:
        raise RuntimeError('Gateway/Identity did not become ready')
    time.sleep(0.5)
status, _ = post('/registrations', {'email': email, 'password': 'Synthetic!' + uuid.uuid4().hex + '8'})
if status != 202:
    raise RuntimeError('Synthetic registration failed')
message = None
deadline = time.monotonic() + 40
while time.monotonic() < deadline:
    summaries = get('http://localhost:8025/api/v1/messages')['messages']
    summary = next((item for item in summaries if any(person['Address'] == email for person in item['To'])), None)
    if summary:
        message = get('http://localhost:8025/api/v1/message/' + summary['ID'])
        break
    time.sleep(0.5)
if message is None:
    raise RuntimeError('Verification email was not delivered to the local capture inbox')
links = re.findall(r'http://localhost:4200/verify-email#[^\s]+', message['Text'])
if len(links) != 1:
    raise RuntimeError('Verification message has an unexpected link structure')
url = urllib.parse.urlsplit(links[0])
if url.query:
    raise RuntimeError('Bearer token must not be in an HTTP query string')
values = urllib.parse.parse_qs(url.fragment)
body = {'userId': values['user'][0], 'token': values['token'][0]}
print('PASS protected queue -> real SMTP -> local captured verification email', flush=True)

# Verify persistent token keys and challenge state survive a real container restart.
subprocess.run(COMPOSE + ['--profile', 'identity', 'restart', 'identity'], check=True, capture_output=True)
deadline = time.monotonic() + 30
while True:
    try:
        if get('http://localhost:5082/health/ready').get('status') == 'ready':
            break
    except (urllib.error.URLError, OSError):
        pass
    if time.monotonic() > deadline:
        raise RuntimeError('Identity readiness did not recover after restart')
    time.sleep(0.5)
status, result = post('/email-verifications', body)
if status != 200 or result.get('status') != 'verified':
    raise RuntimeError('Verification failed after persistent-key restart')
if post('/email-verifications', body)[0] != 400:
    raise RuntimeError('Consumed verification link was accepted again')
if post('/email-verification-requests', {'email': 'unknown-' + uuid.uuid4().hex + '@example.invalid'})[0] != 202:
    raise RuntimeError('Resend endpoint did not preserve the generic response')
print('PASS persistent keys after restart, successful verification and replay rejection', flush=True)
print('PASS neutral response for unknown resend address', flush=True)
consumer = subprocess.run(COMPOSE + ['exec', '-T', 'kafka',
    '/opt/kafka/bin/kafka-console-consumer.sh', '--bootstrap-server', 'localhost:9092',
    '--topic', 'commerce.identity.users.v1', '--from-beginning', '--timeout-ms', '5000'],
    capture_output=True, text=True, timeout=30)
events = [json.loads(line) for line in consumer.stdout.splitlines() if line.startswith('{')]
verified = [event for event in events if event.get('type') == 'UserEmailVerified' and event.get('aggregateId') == body['userId']]
if not verified:
    raise RuntimeError('Email verification event was not consumed from Kafka')
for event in verified:
    if event.get('version') != 1 or email in json.dumps(event) or body['token'] in json.dumps(event):
        raise RuntimeError('Invalid or sensitive verification event contract')
print('PASS independent Kafka consume of PII-free UserEmailVerified event', flush=True)
