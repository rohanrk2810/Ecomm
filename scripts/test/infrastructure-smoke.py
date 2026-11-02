"""Exercise local dependencies using synthetic, temporary records only."""
import base64
import datetime
import email.utils
import hashlib
import hmac
import json
import pathlib
import subprocess
import time
import urllib.parse
import urllib.request
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
COMPOSE = ['docker', 'compose', '--env-file', str(ROOT / '.env'), '-f',
           str(ROOT / 'deploy/docker-compose.local.yml')]


def command(service, *arguments, stdin=None):
    result = subprocess.run(COMPOSE + ['exec', '-T', service, *arguments],
                            input=stdin, text=True, capture_output=True, timeout=60)
    if result.returncode:
        raise RuntimeError(f'{service} smoke command failed (exit {result.returncode})')
    return result.stdout.strip()


def storage_request(method, path, key, body=None, query=None, blob=False):
    date = email.utils.format_datetime(datetime.datetime.now(datetime.timezone.utc), usegmt=True)
    headers = {'x-ms-date': date, 'x-ms-version': '2023-11-03'}
    if body is not None:
        headers['Content-Type'] = 'application/octet-stream'
        headers['Content-Length'] = str(len(body))
    if blob:
        headers['x-ms-blob-type'] = 'BlockBlob'
    canonical_headers = ''.join(f'{name}:{headers[name]}\n' for name in sorted(headers)
                                if name.startswith('x-ms-'))
    canonical_resource = '/commerce' + path
    if query:
        canonical_resource += ''.join(f'\n{name}:{query[name]}' for name in sorted(query))
    fields = [method, '', '', str(len(body)) if body else '', '',
              headers.get('Content-Type', ''), '', '', '', '', '', '']
    signature = base64.b64encode(hmac.new(base64.b64decode(key),
        ('\n'.join(fields) + '\n' + canonical_headers + canonical_resource).encode(),
        hashlib.sha256).digest()).decode()
    headers['Authorization'] = 'SharedKey commerce:' + signature
    url = 'http://127.0.0.1:10000' + path
    if query:
        url += '?' + urllib.parse.urlencode(query)
    with urllib.request.urlopen(urllib.request.Request(url, data=body, headers=headers,
                                                       method=method), timeout=10) as response:
        return response.read()


def main():
    marker = uuid.uuid4().hex
    sql = ('BEGIN; CREATE TEMP TABLE commerce_smoke(value text); '
           "INSERT INTO commerce_smoke VALUES ('synthetic'); "
           'SELECT count(*) FROM commerce_smoke; ROLLBACK;')
    result = command('postgres', 'psql', '-U', 'commerce_bootstrap', '-d', 'postgres',
                     '-v', 'ON_ERROR_STOP=1', '-tAc', sql)
    if '1' not in result.splitlines():
        raise RuntimeError('PostgreSQL transactional write/read failed')
    print('PASS PostgreSQL transactional write/read/rollback', flush=True)

    key = 'commerce:v1:smoke:' + marker
    try:
        if command('redis', 'redis-cli', 'SET', key, 'synthetic', 'EX', '60') != 'OK':
            raise RuntimeError('Redis write failed')
        if command('redis', 'redis-cli', 'GET', key) != 'synthetic':
            raise RuntimeError('Redis read failed')
    finally:
        command('redis', 'redis-cli', 'DEL', key)
    print('PASS Redis write/read with TTL and cleanup', flush=True)

    topic = 'commerce.local.smoke.' + marker
    kafka_cli = '/opt/kafka/bin/'
    created = False
    try:
        command('kafka', kafka_cli + 'kafka-topics.sh', '--bootstrap-server', 'localhost:9092',
                '--create', '--topic', topic, '--partitions', '1', '--replication-factor', '1')
        created = True
        payload = json.dumps({'eventId': marker, 'type': 'LocalSmokeCompleted', 'version': 1})
        command('kafka', kafka_cli + 'kafka-console-producer.sh', '--bootstrap-server',
                'localhost:9092', '--topic', topic, stdin=payload + '\n')
        consumed = command('kafka', kafka_cli + 'kafka-console-consumer.sh', '--bootstrap-server',
                'localhost:9092', '--topic', topic, '--from-beginning', '--max-messages', '1',
                '--timeout-ms', '15000')
        if json.loads(consumed) != json.loads(payload):
            raise RuntimeError('Kafka produced/consumed payload mismatch')
    finally:
        if created:
            command('kafka', kafka_cli + 'kafka-topics.sh', '--bootstrap-server', 'localhost:9092',
                    '--delete', '--topic', topic)
    print('PASS Kafka topic/create/produce/consume/delete', flush=True)

    values = dict(line.split('=', 1) for line in (ROOT / '.env').read_text().splitlines()
                  if line and not line.startswith('#'))
    container = '/commerce/smoke-' + marker
    storage_key = values['OBJECT_STORAGE_KEY']
    created = False
    try:
        storage_request('PUT', container, storage_key, b'', {'restype': 'container'})
        created = True
        storage_request('PUT', container + '/synthetic.txt', storage_key, b'synthetic smoke', blob=True)
        if storage_request('GET', container + '/synthetic.txt', storage_key) != b'synthetic smoke':
            raise RuntimeError('Object storage read mismatch')
    finally:
        if created:
            storage_request('DELETE', container, storage_key, query={'restype': 'container'})
    print('PASS authenticated object upload/read/delete', flush=True)

    with urllib.request.urlopen('http://127.0.0.1:13133/', timeout=10) as health:
        if health.status != 200:
            raise RuntimeError('Collector is unhealthy')
    start = time.time_ns()
    payload = {'resourceSpans': [{'resource': {'attributes': [
        {'key': 'service.name', 'value': {'stringValue': 'commerce.local.smoke'}}]},
        'scopeSpans': [{'scope': {'name': 'local-smoke'}, 'spans': [{
            'traceId': marker, 'spanId': marker[:16], 'name': 'InfrastructureSmoke',
            'kind': 1, 'startTimeUnixNano': str(start), 'endTimeUnixNano': str(start + 1000)}]}]}]}
    request = urllib.request.Request('http://127.0.0.1:4318/v1/traces',
        data=json.dumps(payload).encode(), headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(request, timeout=10) as response:
        result = json.loads(response.read() or b'{}')
        if result.get('partialSuccess', {}).get('rejectedSpans', 0):
            raise RuntimeError('Collector rejected trace spans')
    print('PASS collector health and OTLP trace acceptance', flush=True)


if __name__ == '__main__':
    main()
