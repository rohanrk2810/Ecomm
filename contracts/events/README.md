# Event contract policy
Identity registration now publishes UserRegistered v1; see UserRegistered.v1.schema.json.
Successful email confirmation publishes UserEmailVerified v1. Neither carries email or token data.
Add further schemas with their owning services.
Envelope requires eventId, eventType/version, occurredAtUtc, producer, aggregateId/version,
correlationId, causationId and W3C trace context. Use past-tense facts and avoid unnecessary PII.
Publish with transactional outbox; consume with durable inbox and bounded retry/DLQ.
Assume duplicate/out-of-order delivery. Never promise end-to-end exactly-once.
