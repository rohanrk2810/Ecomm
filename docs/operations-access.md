# Operations current-access screen

Plan: add a lazy operations-only page and typed same-origin client for the current staff
API. Display only confirmed server grants; distinguish sign-in, forbidden, unavailable,
empty and loading states. Clear previous grants when refreshing or losing access.

Open http://localhost:4201/staff-access after signing in and completing MFA. The development
applications share localhost cookies through their gateway proxies. Sign in redirects to
the configured storefront BFF callback; return to the operations page and Check again.
Production hosting/cookie domains and separate-client deployment still need approval.

This screen reads the caller's own current branch permissions. It does not assign roles,
grant staff membership or implement commerce administration. The backend remains the
authorization boundary; rendered permissions never authorize a browser command.
