# Requested local demo account

Plan: provision one development-only fixture through an explicit Identity CLI command,
write random credentials only to ignored local files, preserve existing account security
on rerun, and verify login without bypassing staff MFA. No schema change is needed.

After building/migrating the current Identity image, run:

```powershell
./scripts/dev/provision-demo-staff.ps1
```

The script generates a unique example.invalid email, strong random password, role ID and
authorization-branch ID in .tools/demo-staff-config.json. It verifies that Git ignores
both credential paths before writing. Passwords travel to Docker via named environment
variables, not command-line values. Read .tools/demo-staff-account.txt locally; neither
file is committed or printed. Keep these local credentials private.

The explicit --seed-demo-staff command rejects non-Development environments, real email
domains and unexpected databases. It creates an already-confirmed synthetic user, a demo
role/branch assignment, an audit and the existing PII-free registration/verified outbox
facts in one transaction. Confirmation is a demo-only fixture exception, never a public
registration bypass. The authorization branch is not a physical commerce branch record.
No stock, order, payment or production data is created. The role label is configurable
fixture data; names confer no authority. It currently contains Identity access/delegation
permission references, not a completed staff administration interface.

The fixture deliberately has no enrolled MFA factor. Sign in at /account, choose Set up
an authenticator, enter the password, add the manual key in your authenticator app and
confirm its code. Save recovery codes privately. Sign in again using a fresh app or
recovery code; /api/v1/identity/staff-access then shows only the demo branch grants.
Password-only login must receive 403 there even though the account has staff membership.

Reruns require the original demo marker and matching password; they never overwrite
passwords, MFA, lockout, membership or revoked assignments. If you change the password,
update your private configuration deliberately; the script will not reset it for you.
Do not delete configuration and assume a replacement account will inherit a prior role.

python scripts/test/demo-staff-smoke.py checks the requested account's initial login and
password-only denial without displaying its credentials or enrolling/replacing a factor.
An already-enrolled account requires interactive MFA and is left untouched. Synthetic
integration tests check environment/domain guards, atomic audit/events and safe reruns.
