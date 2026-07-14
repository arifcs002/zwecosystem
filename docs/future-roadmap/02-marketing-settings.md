# BRD: Marketing Settings

## What it is
A dedicated settings section for marketing/promo tooling: discount coupon
codes, banner/popup campaign scheduling, and (later) email/SMS campaign
blasts to the customer list. Distinct from `CompanySetting` (which today
holds unstructured key/value store config like `sms_api_url`, `shop_name`).

## Why it matters
The storefront currently has no coupon/promo-code system — a very common
ask from earlier feature discussions ("Coupon/Promo codes" was flagged as
High priority in the original module-gap discussion). ClickDokan bundles
this under a visible "Marketing Settings" menu, suggesting customers expect
to find it there.

## Scope (v1)
**In scope:** Coupon codes (fixed amount or % discount, usage limit, expiry date, min order amount), applied at checkout.
**Out of scope (later):** popup/banner campaign builder, email/SMS blast campaigns (SMS sending infra already exists via `ISmsSender` — reuse it later for this).

## Data model
```
Coupon : AuditEntity
  Id, CompanyId, Code (string, unique per company), DiscountType ('PERCENT'|'FIXED'),
  DiscountValue (decimal), MinOrderAmount (decimal?), MaxUses (int?), UsedCount (int),
  ExpiryDate (DateTime?), IsActive (bool)
```
Add via `Program.cs` raw-SQL block: `CREATE TABLE IF NOT EXISTS coupons (...)` with a unique index on `(company_id, code)`.

## API endpoints
New `backend/Controllers/CouponsController.cs`:
- `GET/POST/PUT/DELETE api/coupons` (admin CRUD)
- `POST api/coupons/validate` — `{code, orderSubtotal}` → returns discount amount or rejection reason (expired/limit reached/min not met). Called from checkout (`OrdersController.CreatePublicOrder` — apply discount to `Total` before `sp_create_online_order`, similar to how `deliveryCharge`/`freeAbove` are read from `CompanySettings` today).

## Admin UI
New `admin/src/app/components/admin/marketing-settings/` (coupon list + create/edit modal, same shape as `pricing-management`). Storefront checkout page (`admin/src/app/components/admin/ecommerce/checkout/` or wherever public checkout UI lives) gets a "Have a coupon?" input that calls `POST api/coupons/validate`.

## Open questions
- Per-product or per-category coupon restriction, or storewide only for v1?
- Should coupon usage be tracked per-customer (one use per customer) or just a global `MaxUses`?

## Implementation prompt
```
Implement the Marketing Settings (Coupons) module for ZW Ecosystem per
docs/future-roadmap/02-marketing-settings.md. Add a Coupon entity to
backend/Domain/Entities.cs (AuditEntity, CompanyId+nav, unique Code per
company). Add the schema via an idempotent raw-SQL block in Program.cs with
a unique index on (company_id, code). Create backend/Controllers/CouponsController.cs
with CRUD endpoints plus POST api/coupons/validate that checks expiry, max
uses, and min order amount, returning the computed discount. Wire coupon
validation into the public checkout flow in OrdersController.CreatePublicOrder
(apply the discount to subtotal/total before calling sp_create_online_order,
following how delivery_charge/free_delivery_above are already read from
CompanySettings there). Build the admin UI: a new marketing-settings standalone
component (list + create/edit modal, same shape as pricing-management), and
add a "Have a coupon?" input to the storefront checkout component that calls
the validate endpoint. Register routes and a nav entry under an appropriate
group. Verify it compiles before finishing.
```
