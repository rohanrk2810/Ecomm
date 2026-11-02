# Architecture and ownership
Angular storefront and operations portal call only same-origin gateway paths.
Gateway owns routing and transport concerns, not commerce data. Every future service
owns its database, credentials, contracts and migrations. Cross-service SQL is prohibited.

| Owner | Authoritative data |
|---|---|
| Identity | Accounts, credentials metadata, roles, sessions |
| Customer | Profiles, addresses, preferences, consent |
| Catalog | Products, SKU, UOM, media metadata |
| Pricing | Prices, promotion rules, coupons |
| Inventory | Stock ledger, lots, reservations |
| Cart | Carts and versions |
| Order | Order snapshots and checkout saga |
| Payment | Attempts, verified webhooks, refunds |
| Fulfilment | Capacity, tasks, dispatch and handover |
| Procurement | Suppliers, purchase orders and goods receipt |
| Billing | Invoice snapshots, series and credit notes |
| Search | Derived search projection |
| Notification | Templates and delivery attempts |
| Reporting | Derived reporting projection |

Gateway, Angular shells and the first Identity registration slice exist. Identity owns
commerce_identity and the isolated synthetic commerce_identity_tests database. Login/session
and customer profile services are pending. No empty business services have been created.

## Initial glossary and workflows
Branch: physical store and authorization scope. SKU: stock keeping unit.
Reservation: durable, expiring allocation of stock. Quote: versioned server price calculation.
Saga: durable orchestration of local transactions and compensation.
FEFO: first-expiring eligible batch allocated first.

Checkout: validate quote/cart/ownership → reserve inventory atomically → hosted payment →
verify signed webhook → confirm order → fulfilment and billing.
Failure/expiry: compensate once; unknown payment stays pending for reconciliation.
Duplicate requests/events must not repeat effects. No browser redirect proves payment.
Goods receipt: procurement records receipt → event → inventory adds stock once.
