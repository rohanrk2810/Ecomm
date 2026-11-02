# ADR-0015: Local object storage emulator
Status: Reversible development default; production provider pending

## Context
Phase 1 requires a local object storage emulator. The attempted MinIO image
lookup returned access denied. No storage adapter or business service exists yet.

## Decision
Use Microsoft Azurite 3.35.0 Blob emulator locally with a random custom account key.
Bind port 10000 to loopback only. No Azure cloud account is needed.
Health checks only the listening socket. Authenticated object operations must be
tested with the first storage-owning service.

## Alternatives
S3 emulators and approved S3/cloud storage remain options for the final adapter.

## Consequences
Azurite implements Azure Blob APIs, not S3. It cannot validate an eventual S3 adapter.
Replace it when provider/API compatibility is approved. Service boundaries unchanged.

## Security and operations
Random credentials in ignored local .env. Development only. Production needs
private storage, TLS, IAM, lifecycle and recovery policies.
Source: https://github.com/Azure/Azurite

## Approval
Production provider, owner and approval pending. No production approval inferred.
