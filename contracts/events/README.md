# Event contract policy
No business events implemented in the foundation. Add schemas with the owning service.
Envelope requires eventId, eventType/version, occurredAtUtc, producer, aggregateId/version,
correlationId, causationId and W3C trace context. Use past-tense facts and avoid unnecessary PII.
Publish with transactional outbox; consume with durable inbox and bounded retry/DLQ.
Assume duplicate/out-of-order delivery. Never promise end-to-end exactly-once.
