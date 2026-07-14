# BRD: Subscription Plans UI

## What it is
A visible "Plans" page (like ClickDokan's pricing-tier screenshot) showing
the SaaS subscription tiers a company can be on, with limits (max products,
max orders, max stores, max users) and an upgrade path.

## Why it matters
The backend **already has** a `SubscriptionPlan` entity, seeded with "Basic
Plan" (৳1500/mo) and "Premium Plan" (৳3500/mo) — see
`ApplicationDbContext.SeedInitialData` — but there is **no admin UI**
anywhere that shows these plans, lets a company see which plan they're on,
or lets a superadmin edit plan limits/pricing. This is the single biggest
gap versus ClickDokan's screenshot, since it's the exact feature shown.

## Scope (v1)
- Superadmin: CRUD screen for `SubscriptionPlan` (name, price, billing cycle, feature limits stored in the existing `Features` JSON column: max_products, pos_enabled, ecommerce_enabled, multi_staff, etc.)
- Company admin: a read-only "Current Plan" view showing their plan + usage vs limits (e.g. "342 / 500 products used")
- Superadmin: ability to change which plan a company is on (likely already has a `Company.SubscriptionPlanId` or similar FK — verify and reuse if present, add if missing)

## Data model
Reuse the existing `SubscriptionPlan` entity — **no new entity needed**. Verify (during implementation) whether `Company` already has a `SubscriptionPlanId` FK; if not, add one via the standard `Program.cs` raw-SQL `ALTER TABLE companies ADD COLUMN IF NOT EXISTS subscription_plan_id INTEGER REFERENCES subscription_plans(id)` pattern.

## API endpoints
- `backend/Controllers/SubscriptionPlansController.cs` (new): superadmin CRUD on `SubscriptionPlan`
- Extend `CompaniesController` (existing) with `PUT api/companies/{id}/subscription-plan` to change a company's plan
- A "my plan + usage" endpoint for company admins: current plan + live counts (`Products.Count()`, `Users.Count()`, etc. scoped by `CompanyId`) vs the plan's JSON limits

## Admin UI
- Superadmin: `admin/src/app/components/admin/subscription-plans/` — plan cards/table CRUD, matching the visual style of the ClickDokan screenshot (tier cards with feature checklist) since that's a proven layout for this exact use case
- Company workspace: a "My Plan" page/section (could live inside existing Settings/`dashboard-config` page rather than a whole new nav item) showing plan name, limits, and current usage bars

## Open questions
- Does changing a company's plan need to enforce limits immediately (e.g. block adding a 501st product on the Startup-equivalent plan)? If yes, that enforcement needs to be added to `ProductsController.CreateProduct` etc. — a meaningfully bigger follow-up than just displaying the plan.
- Is billing/payment collection for plan upgrades in scope, or admin-manual for now (superadmin flips the plan after receiving payment outside the app)?

## Implementation prompt
```
Implement the Subscription Plans UI for ZW Ecosystem per
docs/future-roadmap/05-subscription-plans-ui.md. First inspect the existing
SubscriptionPlan entity and its seed data in ApplicationDbContext.SeedInitialData,
and check whether Company already has a SubscriptionPlanId FK — add one via an
idempotent Program.cs raw-SQL ALTER TABLE if missing. Create
backend/Controllers/SubscriptionPlansController.cs with superadmin CRUD, extend
CompaniesController with an endpoint to change a company's plan, and add a
"my plan + usage" endpoint that returns the current company's plan alongside
live counts (products, users, stores) compared to the plan's Features JSON
limits. Build two Angular UIs: a superadmin subscription-plans management screen
styled as tier cards with a feature checklist (matching the reference
screenshot layout — price, feature list with checkmarks, a "counts" row for
products/orders/store/users), and a "My Plan" view in the company workspace
(could be a tab/section on the existing dashboard-config page rather than a
new nav item) showing usage vs limits. Register routes and nav entries. Verify
it compiles before finishing. Do not implement hard usage-limit enforcement
(blocking creation past a limit) — that's flagged as a separate follow-up in
the BRD's open questions.
```
