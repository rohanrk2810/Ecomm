"""Validate local gateway -> Identity -> PostgreSQL -> acknowledged Kafka event."""
import json
import pathlib
import subprocess
import time
import urllib.request
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
COMPOSE = ['docker', 'compose', '--env-file', str(ROOT / '.env'), '-f',
           str(ROOT / 'deploy/docker-compose.local.yml')]
marker = uuid.uuid4().hex
email = 'smoke-' + marker + '@example.invalid'
password = 'Synthetic!' + uuid.uuid4().hex + '7'


def register(address):
    request = urllib.request.Request('http://localhost:5080/api/v1/identity/registrations',
        data=json.dumps({'email': address, 'password': password}).encode(),
        headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(request, timeout=20) as response:
        if response.status != 202 or json.loads(response.read()).get('status') != 'pending_verification':
            raise RuntimeError('Unexpected registration state')


register(email)
register(email.upper())
print('PASS gateway registration and duplicate pending response', flush=True)
sql = '''SELECT o."Id"::text FROM identity."AspNetUsers" u
JOIN identity."Outbox" o ON o."Payload"->'data'->>'userId' = u."Id"::text
WHERE u."NormalizedEmail" = upper(:'email') AND o."PublishedAtUtc" IS NOT NULL;'''
event_id = ''
deadline = time.monotonic() + 30
while time.monotonic() < deadline:
    result = subprocess.run(COMPOSE + ['exec', '-T', 'postgres', 'psql', '-X', '-qAt',
        '-v', 'ON_ERROR_STOP=1', '-v', 'email=' + email, '-U', 'commerce_identity',
        '-d', 'commerce_identity'], input=sql, capture_output=True, text=True, timeout=15)
    if result.returncode:
        raise RuntimeError('Identity-owned event status query failed')
    events = result.stdout.strip().splitlines()
    if len(events) > 1:
        raise RuntimeError('Duplicate logical registration events')
    if events:
        event_id = events[0]
        break
    time.sleep(0.5)
if not event_id:
    raise RuntimeError('Registration event did not receive Kafka acknowledgement in time')
print('PASS one durable Identity event acknowledged by Kafka', flush=True)
consumer = subprocess.run(COMPOSE + ['exec', '-T', 'kafka',
    '/opt/kafka/bin/kafka-console-consumer.sh', '--bootstrap-server', 'localhost:9092',
    '--topic', 'commerce.identity.users.v1', '--from-beginning', '--timeout-ms', '5000'],
    capture_output=True, text=True, timeout=30)
events = [json.loads(line) for line in consumer.stdout.splitlines() if line.startswith('{')]
matched = [event for event in events if event.get('eventId') == event_id]
if not matched:
    raise RuntimeError('Acknowledged registration event not found by independent Kafka consumer')
for event in matched:
    if event.get('type') != 'UserRegistered' or event.get('version') != 1:
        raise RuntimeError('Incorrect event contract')
    if email in json.dumps(event) or password in json.dumps(event):
        raise RuntimeError('Sensitive fields leaked into event payload')
print('PASS independent Kafka consume with PII-free versioned envelope', flush=True)
