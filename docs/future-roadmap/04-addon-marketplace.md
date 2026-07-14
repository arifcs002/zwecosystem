# BRD: Ad On / Addon Marketplace

## What it is
A catalog of optional paid add-ons a company can enable on top of their base
subscription plan (e.g. extra SMS credits, an extra staff seat, a premium
theme, a WhatsApp integration). ClickDokan lists this as its own top-level
"Ad On" menu, separate from the base subscription Plans page.

## Why it matters
This is a monetization lever independent of the base subscription tiers —
lets the platform owner (you, running ZW Ecosystem as a SaaS) sell targeted
upgrades instead of forcing a full plan upgrade for one feature.

## Scope (v1)
A simple addon catalog + per-company activation record. No in-app payment
processing required for v1 (superadmin manually marks an addon
active/inactive per company after receiving payment offline, or reuse the
in-progress Payment Gateway module for automated purchase later).

## Data model
```
Addon : AuditEntity  (global, not per-company — this is the catalog superadmin manages)
  Id, Name, Description, Price (decimal), BillingCycle ('one_time'|'monthly'|'yearly')

CompanyAddon : AuditEntity
  Id, CompanyId, AddonId (FK), ActivatedDate (DateTime), ExpiryDate (DateTime?), IsActive (bool)
```

## API endpoints
- `backend/Controllers/AddonsController.cs`: `GET api/addons` (public catalog, any authenticated user), superadmin-only `POST/PUT/DELETE api/addons` for catalog management
- `GET api/company-addons` (addons active for the current company), superadmin `POST api/company-addons` to activate one for a company, `PUT api/company-addons/{id}/deactivate`

## Admin UI
- Superadmin: `admin/src/app/components/admin/addon-management/` — catalog CRUD, plus a per-company activation screen (likely nested under Companies management)
- Company admin: a simple "Ad On" page under company workspace showing the catalog with an "Activate" button (which for v1 just submits a request — no payment flow — resulting in a `CompanyAddon` row with `IsActive=false` pending superadmin approval, OR wires into the Payment Gateway module from docs once that's live)

## Open questions
- Should addon purchase go through the new Payment Gateway module (bKash/Nagad/BanglaQR) directly, or stay manual/offline for v1?
- Does activating an addon need to flip any feature flags elsewhere in the app (e.g. "extra SMS credits" addon → raise a monthly SMS-send counter/limit)? If so, that enforcement logic needs to be designed per addon type.

## Implementation prompt
```
Implement the Addon Marketplace for ZW Ecosystem per
docs/future-roadmap/04-addon-marketplace.md. Add Addon (global catalog) and
CompanyAddon (per-tenant activation record) entities to backend/Domain/Entities.cs
following the AuditEntity pattern — note Addon has no CompanyId (it's a shared
catalog managed by superadmin), while CompanyAddon does. Add the schema via an
idempotent raw-SQL block in Program.cs. Create backend/Controllers/AddonsController.cs
(public GET, superadmin-only write) and a company-addons endpoint set for
viewing/activating/deactivating per company. Build two Angular UIs: a superadmin
addon-catalog management screen, and a company-workspace "Ad On" page listing the
catalog with activation status. Register routes for both the /admin and
company-workspace route blocks in app.routes.ts, with nav entries in the
appropriate sidebar sections of admin-layout.component.html. Verify it compiles
before finishing. Flag in your final summary that payment-flow wiring for paid
activation is intentionally left as a follow-up (see Open Questions in the BRD).
```
