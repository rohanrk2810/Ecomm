# Initial threat model — review required
Trust boundaries: browser → edge/gateway → private services → private data stores;
payment webhook ingress; Kafka producer/consumer; private object storage and admin actions.

| Threat | Required control | Foundation state |
|---|---|---|
| Spoofed identity | Approved OIDC, staff MFA, service identities | Pending; gateway denies protected routes |
| Cross-customer/branch access | Resource ownership and branch checks inside each service | Required in Phase 2+ |
| Header/log injection | Server-generated correlation ID, structured logs | Implemented at gateway |
| Payment tampering/replay | Signed webhooks, amount verification, durable inbox, reconciliation | Required in Phase 5 |
| Oversell/duplicate events | DB atomic reservation, outbox/inbox and idempotency | Required in Phase 4+ |
| CSRF/XSS | BFF CSRF protection, Angular escaping, edge CSP and secure cookies | Auth/edge pending |
| Secret exposure | Environment/secret manager, ignored local credentials, no browser secrets | Foundation policy |
| Denial of service | Edge limits, quotas, timeout/backpressure and load validation | Pending before public launch |
| Upload abuse | MIME/signature limits, malware scan, private buckets | Required with catalog media |

Data classification: public product presentation; confidential profiles/addresses;
restricted credentials, provider secrets and financial metadata. Raw card/CVV prohibited.
No real customer data or production secrets in local development. Security/privacy owner,
retention periods and review sign-off pending. Local plaintext dependencies are loopback-only
and may be used only for development, never shared dev/staging/production.
