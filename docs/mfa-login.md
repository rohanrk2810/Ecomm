# MFA verification: first implementation slice

Plan: extend the existing Identity password form to verify an already enrolled
account using the maintained authenticator provider or a one-time recovery code.
Persist replay protection under the owning Identity database and verify the real
password form before committing. No staff role, branch scope or enrollment is granted.

The same form accepts optional AuthenticatorCode or RecoveryCode. A confirmed account
must first pass its password/lockout check. If TwoFactorEnabled is set, one valid second
factor is mandatory. Both fields together are rejected. Passwords and factor values
are cleared from model state before rendering a failure. Existing CSRF and IP quota
apply; invalid well-formed factor attempts count toward Identity account lockout.

MfaGate serializes verification using the account email advisory lock shared with
password recovery. It reloads the account and verifies its security stamp, confirmation,
MFA status and lockout. Recovery codes are redeemed through UserManager inside the
transaction. Authenticator validation uses the maintained Identity provider; production
code does not implement TOTP cryptography. Successful code digests are retained for five
minutes, beyond the provider's neighboring-step acceptance window, to reject replay.
Expired digests for an account are removed on its next successful authenticator login.
No raw OTP is persisted. Existing sign-in audit records capture success/rejection.

Apply AddMfaCodeReplayProtection explicitly before starting the updated Identity image.
No new package, secret, public registration or staff API is introduced. Existing customer
OIDC sessions remain the only session kind; they confer no staff permissions.

Subsequent work (enrollment and initial recovery display are now implemented below):
production key custody, recovery-code regeneration, factor reset/offboarding policy, authoritative MFA session assurance,
staff invitation, role/branch policy approval and authorization enforcement. Do not call
this a completed staff access system. Test-only enrollment uses synthetic accounts only.

Provider behavior verified against the pinned framework source:
https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Identity/Extensions.Core/src/AuthenticatorTokenProvider.cs

## Authenticator enrollment

Implementation plan: expose a dedicated Identity-hosted enrollment form from the
storefront account page, require confirmed email/current password at each POST, protect
factor material at rest, activate only after a valid first authenticator code, and revoke
old sessions. Verify CSRF, wrong credentials/code, encrypted storage and recovery replay.

After sign-in, choose Set up an authenticator on /account. The gateway checks the BFF
session before redirecting to /account/mfa on the configured Identity issuer. The Identity
form itself requires the account password, email confirmation and lockout checks on each
step; no account identifier from a cookie or URL grants permission to enroll.

Start/resume shows a manual setup key, with a ten-minute challenge bound to the security
stamp. Choose a time-based account in an authenticator app. Starting a fresh setup rotates
the authenticator key/security stamp and invalidates existing sessions. The form clears
the provider cookie and uses anonymous CSRF identity for the next password-protected POST.
Confirm with the password and app code. MFA is enabled only on a correct code. Wrong
passwords/codes count toward account lockout; valid passwords alone do not reset those
attempts during enrollment. Existing MFA accounts cannot restart or replace their factor.

Confirmation changes the security stamp, revokes durable sessions, consumes the challenge,
records MfaEnrolled audit, generates ten recovery codes and records the enrollment OTP as
used. The codes appear only in that POST response, with no-store and no-referrer headers;
GET/repeated confirmation cannot retrieve them. Save them privately, then sign in again
with a fresh app code or a recovery code. No automatic login, trusted device or bypass.

ProtectedIdentityStore retains the maintained Identity authenticator/recovery algorithms
and wraps their token values with Data Protection bound to user/provider/name. New keys
and recovery sets are protected at rest. Existing legacy plaintext tokens are readable
for compatibility and become protected when rewritten; this is not a bulk migration of
old installations. No real enrolled accounts were created by development tests. Production
must assess legacy material, protect/retain the key ring and test rotation/recovery.

Apply AddMfaEnrollment explicitly before starting the new images. A new table stores only
challenge timestamps/security stamp, never raw keys. No new package or secret is required.
Run scripts/test/mfa-enrollment-smoke.py against local containers and captured SMTP.

Enrollment does not grant staff access. MFA assurance claims, staff invitation, branch/role
policies, factor reset, recovery-code regeneration and account offboarding remain required
separate work. Recovery codes are not a substitute for an approved staff recovery policy.

## Durable MFA session assurance

The login transaction now records MfaVerifiedAtUtc only after a successful second factor.
Existing sessions default to no assurance and require a fresh login. OIDC amr is derived
from this durable record rather than a browser flag or the account enrollment setting.
Code exchange and introspection reject a token asserting MFA when its session assurance
has been removed; disabling MFA also invalidates an MFA-marked session. The gateway
retains the validated amr claim, cross-checks it during introspection and provides a
VerifiedMfa policy. This policy alone grants no staff membership or business permission.

Only the minimal amr protocol field is added explicitly to confidential-BFF introspection;
OpenIddict intentionally withholds general application claims from a presenter that is
not an API audience. The gateway exposes mfaVerified as display information on /bff/session;
client state never authorizes staff requests. Apply AddSessionMfaAssurance explicitly.
