# Customer sign-in and sessions

Implementation plan: add a maintained OIDC provider in Identity, a server-side gateway
session and a lazy storefront account screen. Verify protocol misuse, revocation and
real container restart before committing. This is a local development slice; production
provider selection and security sign-off remain pending under ADR-0002.

Identity owns accounts, OpenIddict protocol records, customer sessions and authentication
audits in its PostgreSQL database. Only verified email/password accounts can sign in.
Identity lockout applies after five failures for fifteen minutes by default; login also
uses the existing per-IP quota. MFA-enabled accounts fail closed until MFA is implemented.
No staff permissions are granted by customer authentication.

OpenIddict 7.7.2 handles authorization code with mandatory S256 PKCE, signing, encryption,
client validation and code replay prevention. Only the server/EF packages are installed.
The confidential first-party BFF client has exact configured callback URLs and implicit
first-party consent. No external consent or third-party client registration is exposed.
Default access/ID token lifetime is fifteen minutes; provider sessions last sixty minutes.
There is no refresh token. Sign-in requires a fresh password check.

Gateway stores protected authentication tickets and tokens in Redis under random
`commerce:bff:v1:session:` keys with absolute expiry. The browser receives an opaque
HttpOnly, SameSite=Lax cookie; Angular receives only availability/authentication/user ID
and a CSRF request token. It never receives OAuth tokens. Production requires HTTPS and
Secure cookies. Gateway has no Identity database credentials. The private backchannel
allows only configured issuer origins and preserves the public Host for discovery.

Each authenticated request introspects the token through Identity and checks the durable
session, confirmation and security stamp. Revoked/expired sessions are rejected. Redis
or provider outages fail closed with a safe 503; Redis is checked in gateway readiness.
Redis loss signs users out. Redis must have a deliberate no-eviction/session-capacity
policy in deployment. Protection keys and signing/encryption certificates persist across
container restarts. Development key volumes need local filesystem protection; production
requires protected key custody, rotation and backup procedures.

GET `/bff/login` begins OIDC; GET `/bff/session` returns safe session status;
GET `/bff/antiforgery` provides the form CSRF token. POST `/bff/logout` requires authentication
and CSRF, deletes the Redis ticket and redirects to the provider. The provider requires a
second CSRF-protected confirmation before revoking its durable session and clearing its
cookie. Tokens are omitted from logout URLs. Abandoning confirmation leaves the provider
session until expiry, but the BFF session has already been removed. No protocol query or
password is logged intentionally; protocol routes are excluded from application traces.

## Local setup

Start dependencies, then run `scripts/dev/provision-identity.ps1`. It generates ignored
OIDC client/certificate secrets along with the existing isolated database setup.
For Docker use the same compose file and `.env` as the development runbook:

```powershell
docker compose --env-file .env -f deploy/docker-compose.local.yml --profile identity --profile app build identity gateway
docker compose --env-file .env -f deploy/docker-compose.local.yml --profile identity run --rm --no-deps identity --prepare-oidc
docker compose --env-file .env -f deploy/docker-compose.local.yml --profile identity run --rm --no-deps identity --migrate
docker compose --env-file .env -f deploy/docker-compose.local.yml --profile identity run --rm --no-deps identity --provision-oidc
docker compose --env-file .env -f deploy/docker-compose.local.yml --profile identity --profile app up -d identity gateway --no-build
```

Certificate generation is explicit and development-only. Normal startup never migrates,
creates certificates or registers clients. Host mode uses `identity-environment.ps1`,
the same three explicit dotnet CLI commands and `start-identity.ps1`/`start-gateway.ps1`.
Host and Docker keep separate certificate/protection-key directories; finish existing
login flows before switching runtime. Start/restart the Angular storefront after proxy
configuration changes, then open `http://localhost:4200/account`.

`python scripts/test/oidc-smoke.py` uses synthetic accounts and captured Mailpit email,
checks the complete flow and restarts the two containers. CI overrides
`PUBLIC_APP_ORIGIN=http://localhost:5080` so callbacks work without a frontend process.
Recovery, staff MFA, branch roles, session administration, audit retention/cleanup,
production deployment and operational outage alerting remain subsequent work.

## Customer password recovery

The storefront exposes /forgot-password and /reset-password. Requests receive a neutral
202, including unknown/unverified accounts. Identity sends only to confirmed accounts,
with a durable cooldown (PasswordRecovery:CooldownSeconds, default 60) and challenge
expiry (PasswordRecovery:LifetimeMinutes, default 15, validated 5?30). The maintained
Identity token provider also enforces its own lifetime. Links are fragment-only and are
cleared by the page; submitting is explicit. Browser reload requires reopening the link.

An independent PasswordChallenges table stores only the digest and lifecycle timestamps.
The migration adds a Kind discriminator to the protected mail queue, defaulting existing
messages to verification. Recovery and confirmation cancellation are isolated by purpose.
Successful reset atomically changes the password/security stamp, consumes the challenge,
revokes durable sessions, cancels pending recovery mail and records PasswordReset audit.
Invalid passwords do not spend a link. There is no automatic sign-in or lockout reset.
Apply the owning Identity migration explicitly, rebuild/restart Identity and gateway,
then use Mailpit locally. No new secrets or packages are needed. Run
python scripts/test/password-reset-smoke.py for captured SMTP delivery/reset/replay checks.
Production sender, key custody, abuse monitoring and staff recovery policies remain pending.
