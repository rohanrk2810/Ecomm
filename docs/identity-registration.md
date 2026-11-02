# Identity registration slice

Scope: create an unconfirmed customer account and durable UserRegistered event.
Email/password was selected by the user. Mobile OTP is deferred. Sign-in, OIDC/BFF,
email delivery/confirmation, reset, MFA, sessions, role assignment and customer profiles
are subsequent slices, not implemented by this endpoint. No token is issued here.

Identity owns its PostgreSQL database, role and migration history. Microsoft ASP.NET
Core Identity hashes passwords. The application layer validates email/password settings;
the infrastructure adapter persists the account and event in one database transaction.
An advisory transaction lock serializes attempts for one normalized email; unique indexes
remain authoritative. Duplicate/case-varied requests return the same 202 pending state
without changing an existing account or creating another event. Response timing is not
constant-time: response shape alone is not a complete enumeration defense.

Unknown JSON fields (roles, confirmation flags, IDs) are rejected. Public registration
cannot grant staff permissions or confirm an email. Account remains unconfirmed.
Only POST /api/v1/identity/registrations and GET /api/v1/identity/registration-policy
are intended as public business endpoints. All other Identity API routes fail closed.
GET readiness verifies connectivity and migration state. Migrations run via an explicit
--migrate command, not on normal replica startup. No service queries Identity SQL directly.

## Durable events
UserRegistered v1 contains only a generated account ID and standard envelope metadata.
No email, password, hash or confirmation secret goes to Kafka. Publisher claims one row
with FOR UPDATE SKIP LOCKED, waits for Kafka acknowledgement, then marks it published.
Failed publication leaves the durable row pending and increments attempts with a safe log.
Crash after publish/before marking can duplicate events; consumers need durable inboxes.
No inbound consumer exists in this slice, so no unused inbox is scaffolded. Poison/corrupt
outbox payload stops the worker for operator attention; no silent dropping or automatic DLQ.
Production needs pending-age/attempt alerts, retention/replay controls and provider TLS/SASL
configuration before enabling this development Kafka adapter outside local environments.

## Local setup
From the repository root after dependency bootstrap:

```powershell
./scripts/dev/provision-identity.ps1
. ./scripts/dev/identity-environment.ps1
dotnet run --no-launch-profile --project src/services/identity/Commerce.Identity.Api -- --migrate
dotnet test src/services/identity/Commerce.Identity.IntegrationTests -c Release
./scripts/dev/start-identity.ps1
```

Provisioning generates an ignored local Identity password, creates its restricted role,
commerce_identity and commerce_identity_tests databases, and a development-only single
partition/topic. Bootstrap DB administrator credentials never enter the Identity process.
Do not reset .env without deliberately handling existing database credentials.
Integration tests require exactly commerce_identity_tests and use synthetic .invalid addresses.
Publisher is disabled in isolated API tests; live Kafka delivery is checked separately.

Development password policy and request quota are validated runtime settings, not an approved
client policy. The current direct-IP limiter sees the gateway IP behind the proxy, so its
quota is shared behind that gateway. Distributed edge/client limits need design before launch.
No passwords, request bodies or SQL parameter values may be logged. Sessions/CSRF are not
applicable to this unauthenticated, non-cookie endpoint; they are mandatory with BFF sign-in.
Registration remains a local preview pending activation/email delivery and security review.

Container setup: build profiles identity/app, run the Identity image once with --migrate,
then start identity and gateway. `py scripts/test/identity-smoke.py` verifies duplicate
gateway requests, one acknowledged outbox event and independent Kafka consumption.
That smoke account is intentionally retained as synthetic development data.
The local connection disables unused GSS negotiation because this development PostgreSQL
does not use Kerberos; production transport needs the approved TLS/workload policy.
Reference: https://www.npgsql.org/doc/release-notes/10.0.html

Verified: generated/applied EF migration, 9 real PostgreSQL Identity tests plus 6 gateway
tests, locked Linux Identity/gateway builds, native Kafka producer delivery and independent
Kafka consumption. NuGet audit reported no known vulnerabilities. Updated CI has not run
remotely. No remote push has been performed.
