# BRD: Custom / Sub Domain Management

## What it is
Lets a company use their own domain (e.g. `shop.theirbrand.com`) or a
platform-provided subdomain (e.g. `theirbrand.zwecosystem.com`) to point at
their storefront, instead of only the generic slug-based URL the storefront
currently uses. Listed as a feature on every ClickDokan pricing tier.

## Why it matters
Multi-tenant SaaS storefronts strongly benefit from letting each store look
"branded" (their own domain) rather than obviously running on shared
infrastructure — this is a common expectation once a merchant is paying for
a subscription tier.

## Scope (v1)
- Sub-domain: company picks a subdomain slug (may already partly exist —
  verify how `Company.Slug` / tenant resolution currently works before
  building this, since there may be overlap)
- Custom domain: company enters their own domain; admin panel shows the DNS
  CNAME/A record they need to point at the server, plus a "verified"
  status once DNS resolves correctly
- **Out of scope for v1**: automatic TLS/SSL certificate provisioning for
  custom domains (this needs real infrastructure work — reverse proxy +
  Let's Encrypt automation — flagged as its own follow-up, and ties into
  the still-open "API is cleartext HTTP" item from the July 2026 security
  review)

## Data model
Likely just new columns on `Company` (verify `Slug` isn't already serving
this purpose first):
```sql
ALTER TABLE companies ADD COLUMN IF NOT EXISTS custom_domain TEXT;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS custom_domain_verified BOOLEAN NOT NULL DEFAULT false;
```

## API endpoints
Extend `CompaniesController` (or `TenantController`, whichever owns
company-settings today):
- `PUT api/companies/{id}/custom-domain` — set the requested domain
- `POST api/companies/{id}/custom-domain/verify` — server-side DNS lookup
  (resolve the domain, check it points to the platform's IP) → updates
  `custom_domain_verified`

## Admin UI
Add a "Domain" section to the existing Settings/`dashboard-config` page:
input for custom domain, instructions showing the DNS record to add, a
"Verify" button, and a status badge (Pending/Verified).

## Open questions
- Who operates DNS/reverse-proxy routing once a custom domain is verified —
  is there existing nginx config that needs a per-tenant server block, or
  a wildcard approach? This is infrastructure work beyond just the admin UI
  and needs a decision before implementation starts.
- TLS strategy for custom domains (see Scope note above) — needs a separate
  BRD/decision once ready to tackle.

## Implementation prompt
```
Implement Custom/Sub Domain management for ZW Ecosystem per
docs/future-roadmap/07-custom-domain.md. IMPORTANT: before writing code,
first check how Company.Slug and tenant resolution currently work (grep for
Slug usage in TenantController/CompaniesController and the X-Tenant-ID header
resolution in ApplicationDbContext) to avoid duplicating existing subdomain
logic. Add custom_domain and custom_domain_verified columns to companies via
an idempotent Program.cs raw-SQL block. Extend the appropriate controller with
endpoints to set a custom domain and to verify it (server-side DNS resolution
check against the platform's IP). Add a "Domain" section to the existing
Settings/dashboard-config admin page: an input for the custom domain, an
instructions block showing the required DNS record, a Verify button, and a
status badge. Do NOT attempt TLS/certificate automation or nginx reverse-proxy
routing changes — those are explicitly out of scope per the BRD and need a
separate infrastructure decision first. Verify the UI compiles before finishing.
```
