# Email verification

Registration now creates an unconfirmed account, an expiring challenge and a protected
verification email in one Identity database transaction. MailKit delivers the message
asynchronously to SMTP. Development uses Mailpit; nothing is relayed to real recipients.
Production sender/provider, certificate/key custody and policies remain unapproved.

POST /api/v1/identity/email-verifications consumes the active challenge, confirms the
email and inserts UserEmailVerified v1 into the transactional Kafka outbox atomically.
Expired, replaced, consumed and cross-account tokens fail. Concurrent confirmations
produce one successful confirmation. The maintained ASP.NET Core Identity token provider
validates user binding, purpose, expiry and security stamp in addition to the stored digest.

POST /api/v1/identity/email-verification-requests returns a generic 202 for valid, unknown,
malformed, confirmed or cooldown-limited requests. Per-account resend cooldown persists
in PostgreSQL; API quota is shared with registration. Response timing is not constant-time.

Angular receives the token from the URL fragment, removes it from the history/address bar,
keeps it in memory only and requires an explicit Verify email click. It never sends a
verification mutation on GET or displays the token. A successful verification does not
create a session. OIDC/BFF login/logout, recovery and staff authorization are subsequent work.

## Local commands

Dependency bootstrap now includes Mailpit. Its UI is http://localhost:8025 and SMTP is
localhost:1025. Both are bound to loopback. Host Identity uses ignored .tools/identity-keys;
the container uses an app-owned identity-keys named volume. Do not delete these keys while
pending links or queued messages need to survive. Test fixtures use separate temporary keys.

```powershell
./scripts/dev/bootstrap.ps1 -DependenciesOnly
./scripts/dev/provision-identity.ps1
docker compose --env-file .env -f deploy/docker-compose.local.yml --profile identity --profile app build identity gateway
docker compose --env-file .env -f deploy/docker-compose.local.yml --profile identity run --rm --no-deps identity --migrate
docker compose --env-file .env -f deploy/docker-compose.local.yml --profile identity --profile app up -d identity gateway --no-build
py scripts/test/verification-smoke.py
```

If registry downloads fail on this workstation, the scoped IPv4 importer also supports
`axllent/mailpit:v1.33.0`. The smoke test registers only a generated .invalid address,
reads its captured email without printing the bearer link, restarts Identity, confirms
after restart, rejects replay and independently consumes the PII-free Kafka event.
Synthetic smoke records/mail are retained. No production/customer data is used.

## Delivery recovery and keys

SMTP retries use increasing delay and a configured maximum attempt count. Abandoned and
expired messages remain recorded. Monitor pending age, Attempts and AbandonedAtUtc; review
credentials/network/key availability before replay. Do not repeatedly resend to bypass
delivery errors. No operator UI or automatic abandoned-message replay is implemented yet.
SMTP can accept a message before a crash prevents marking delivery; duplicate mail is
possible. The confirmation action still consumes one active challenge once.

Recipient/link payloads are DataProtection-protected in VerificationEmails, not plaintext
or Kafka data. Outside Development, the service requires a key-encryption certificate,
persistent directory and SMTP STARTTLS. Inject certificate/password and SMTP credentials
through approved secrets. Approve shared replica key storage, backup/restore, retention,
rotation and delivery alerts before release. Local key files are development-only.

Runtime defaults are 30-minute links, 60-second resend cooldown and five delivery attempts;
they are provisional security settings, not approved client policy. Existing accounts from
the earlier registration slice can request a link through the resend page.

Verified locally: 17 Identity + 6 gateway tests, 19 storefront + 1 operations tests, both
frontend production builds, locked Linux builds, real SMTP capture, key persistence across
container restart, replay rejection and Kafka consumption. NuGet audit reported no known
vulnerable dependencies. Remote CI execution and production readiness are not claimed.
