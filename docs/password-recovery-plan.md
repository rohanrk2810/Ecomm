# Password recovery implementation plan

Status: implementation pending. Owning service: Identity; gateway provides same-origin
routing. This slice extends customer recovery only and grants no staff access.

## Contracts and data

- POST /api/v1/identity/password-reset-requests accepts email and always returns a
  neutral 202 for valid input, including unknown/unverified accounts and cooldown.
- POST /api/v1/identity/password-resets accepts userId, token and newPassword; return
  a generic invalid/expired-link error without revealing account existence.
- Use ASP.NET Core Identity password-reset tokens (purpose distinct from confirmation),
  plus an independent challenge table containing digest, expiry, cooldown and consumption.
  Never reuse EmailChallenges: verification and recovery must not invalidate one another.
- Configure and validate short expiry and resend cooldown. Serialize per-account issuance
  and consumption with the existing PostgreSQL transaction/advisory-lock pattern.
- Atomically persist challenge and protected queued email. Extend the existing dispatcher
  with an explicit purpose/template; verification remains the default for existing rows.
  Add an EF migration for challenge data and message purpose. Never store plaintext tokens.

## Security and browser behavior

- Only confirmed accounts receive recovery mail. Apply existing IP quotas and a durable
  per-account cooldown. Do not reset lockout merely because a reset was requested.
- Links carry user/token in the fragment. Angular clears the fragment immediately and
  keeps the token only in component memory; no local storage or automatic submission.
- Validate Identity password policy on the server. Consume only after successful reset;
  invalid passwords must allow correction without spending the link.
- In one transaction: reset password/security stamp, consume challenge, revoke all durable
  customer sessions, cancel outstanding recovery mail and record a non-sensitive audit.
  Do not automatically sign in. Existing BFF sessions fail subsequent introspection.
- Request page shows a neutral pending state; reset page provides explicit confirmation,
  password-policy hints, loading/error/success states and a link back to sign-in.

## Required verification before completion

Integration: unknown/unverified addresses, cooldown, correct reset, incorrect/expired/
consumed/cross-account token, password-policy rejection, concurrent consumption, resend
invalidation, transaction rollback, old-password rejection and session revocation.
Frontend: fragment clearing, explicit submit, duplicate prevention, neutral request state
and safe retry. Native Docker smoke: captured SMTP, persistent-key restart, actual reset,
old session rejection and new-password OIDC login. Run formatter, build, relevant tests,
dependency/secret scans; update setup and validation evidence. Commit verified backend
and frontend separately using the existing repository owner identity; do not push.
