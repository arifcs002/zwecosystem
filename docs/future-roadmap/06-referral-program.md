# BRD: Referral Program

## What it is
Lets an existing company (tenant) refer new companies to sign up for the
platform, earning a reward (discount, credit, or cash) when the referral
converts to a paying subscriber.

## Why it matters
Cheap customer acquisition channel for the platform owner — turns existing
happy customers into a sales channel with no ad spend.

## Scope (v1)
- Each company gets a unique referral code (reuse the existing `app_code`
  generation pattern in `Program.cs` — `upper(substr(md5(random()::text ||
  id::text), 1, 6))` — for a `referral_code` column instead of a new scheme)
- New company signup form accepts an optional referral code
- Track referral → signup → "converted" (became a paying subscriber) as a
  simple status; reward crediting is manual/superadmin-triggered for v1

## Data model
```
-- add to companies table (Program.cs raw-SQL ALTER, same pattern as app_code):
companies.referral_code TEXT UNIQUE

Referral : AuditEntity
  Id, ReferrerCompanyId (FK → Company), ReferredCompanyId (FK → Company),
  Status ('SIGNED_UP'|'CONVERTED'|'REWARDED'), RewardAmount (decimal?), RewardedDate (DateTime?)
```

## API endpoints
- `backend/Controllers/ReferralsController.cs`: `GET api/referrals/my-code` (current company's code + link), `GET api/referrals` (list of referrals made by this company + their status), superadmin `PUT api/referrals/{id}/reward` to mark rewarded
- Company registration endpoint (wherever new-company signup lives, likely `CompaniesController` or `TenantController`) accepts an optional `referralCode` param — on successful signup, looks up the referring company by code and inserts a `Referral` row with `Status='SIGNED_UP'`

## Admin UI
- Company workspace: a "Referral Program" page showing their code/link (shareable) + a table of who they've referred and reward status
- Superadmin: a referrals list across all companies, with a "Mark Rewarded" action

## Open questions
- What's the actual reward — subscription discount, cash payout, or account credit? This determines whether "Rewarded" needs to actually touch billing/`SubscriptionPlan` or is just a manual note for the platform owner's own bookkeeping.
- Does "Converted" get triggered automatically (e.g. referred company's first payment) or is it also manual for v1? Given Payment Gateway automation is still being built out, manual is the pragmatic v1 choice.

## Implementation prompt
```
Implement the Referral Program for ZW Ecosystem per
docs/future-roadmap/06-referral-program.md. Add a referral_code column to
companies via an idempotent Program.cs raw-SQL block, generated the same way
app_code already is (upper(substr(md5(random()::text || id::text), 1, 6))),
with a unique index. Add a Referral entity to backend/Domain/Entities.cs
(AuditEntity, ReferrerCompanyId + ReferredCompanyId FKs to Company, Status,
RewardAmount, RewardedDate). Create backend/Controllers/ReferralsController.cs
with endpoints to get the current company's referral code/link, list referrals
made by the company, and a superadmin-only mark-rewarded endpoint. Find wherever
new company/tenant signup currently happens and add an optional referralCode
parameter that looks up the referrer by code and inserts a Referral row with
Status=SIGNED_UP on success. Build two Angular UIs: a company-workspace
"Referral Program" page (shareable code/link + referral list with status), and
a superadmin cross-company referrals list with a "Mark Rewarded" action.
Register routes and nav entries. Verify it compiles before finishing. Leave
"Converted" status transition manual for v1 per the BRD's open questions.
```
