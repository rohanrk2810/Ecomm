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

Remaining: protected authenticator enrollment and key custody, recovery-code display and
regeneration, factor reset/offboarding policy, authoritative MFA session assurance,
staff invitation, role/branch policy approval and authorization enforcement. Do not call
this a completed staff access system. Test-only enrollment uses synthetic accounts only.

Provider behavior verified against the pinned framework source:
https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Identity/Extensions.Core/src/AuthenticatorTokenProvider.cs
