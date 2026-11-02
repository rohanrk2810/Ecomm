# ADR-0016: Identity email verification and delivery
Status: Implemented for local development; production sender/provider and key custody pending

## Context
Accounts are registered with email/password and cannot sign in before email ownership is verified.
Security tokens must not appear in Kafka, HTTP query logs, browser storage or plaintext queue rows.

## Decision
Use ASP.NET Core Identity's maintained DataProtection email token provider. Identity owns a
single active challenge per account with SHA256 token digest, expiry, consumed timestamp and
persistent resend cooldown. Confirmation and UserEmailVerified v1 are committed together.
An email-address transaction lock serializes registration/resend/confirmation; a consumed
challenge cannot succeed again. New resend invalidates the old challenge and cancels queued mail.

Identity owns this security mail queue. It stores a DataProtection-protected recipient/link
payload and delivers through MailKit SMTP. General customer/business notifications remain
the future Notification Service's responsibility; no verification secret enters Kafka.
SMTP acknowledgement is followed by a local delivery update. Crash between those steps can
duplicate mail; the verification action remains single-use. Retries are bounded with increasing
delay. Exhausted or expired messages remain visible in the queue, never silently deleted.

Use Mailpit v1.33.0 on loopback for development. It captures SMTP locally and is not configured
to relay mail externally. Local sender and lifetime/cooldown values are provisional settings.
No production email provider, sender identity, DNS/branding or legal policy has been approved.

Verification links carry the secret in a URL fragment, not a query string. Angular removes
the fragment from the visible/history URL, retains it in memory and waits for an explicit
Verify email action before POSTing. Link scanners following GETs cannot activate accounts.

## Keys and operations
Persist keys outside source: ignored .tools for host development and an app-owned Docker
volume for containers. Application/purpose isolation protects the email payload. Outside
development, validated configuration requires an encryption certificate and SMTP STARTTLS.
Certificate/secret-manager setup, shared key custody, backup/rotation and delivery-age alerts
must be reviewed before launch. Never log mail bodies, links, tokens or SMTP credentials.

## Consequences and alternatives
Expired, replayed, replaced and cross-account tokens are rejected. Unknown/confirmed/malformed
resend addresses receive the same generic accepted response. Timing is not constant-time.
SMTP is pluggable; vendor-managed email and managed key storage can replace local adapters.
OIDC/BFF login, password recovery, staff MFA and branch authorization are subsequent slices.

## Approval
Development work authorized by the user in chat. Production provider/policy sign-off pending.
