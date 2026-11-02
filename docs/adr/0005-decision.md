# ADR-0005: PostgreSQL isolation
Status: Proposed / pending approval

## Context
Baseline SCP-ARCH-001 requires this decision before the dependent feature or production deployment.

## Decision
Proposed: each service gets its own database, credentials and migrations. Local bootstrap administrator must never be used by application services.

## Alternatives
Evaluate client-approved managed offerings and self-hosting against cost, security and operating capacity.

## Consequences
Only reversible foundation work may proceed. Do not assume this proposal is approved.

## Security and operations impact
Owner must document credentials, isolation, failure modes, monitoring and recovery before approval.

## Approval
Owner: pending. Approver: pending. Date: pending. Evidence: pending.
