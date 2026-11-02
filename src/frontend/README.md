# Commerce frontend

One Angular 22 workspace with two independently buildable applications.
See the root README for tool selection on this workstation.

- `npm ci`: install from lockfile. Stop dev servers first on Windows.
- `npm run start:storefront`: customer shell at localhost:4200.
- `npm run start:operations`: staff shell at localhost:4201.
- `npm run build`: both production builds.
- `npm test`: Vitest/jsdom startup, configuration and operations shell tests.
- `npm run format:check`: formatting verification.

Each app loads public/runtime-config.json before startup. Only presentation metadata
and a validated same-origin API path belong there. No secrets. Invalid configuration
fails startup visibly. Routes lazy-load pages. /api requests proxy to localhost:5080.
Production edge must supply same-origin routing. No shopping or authentication yet.
