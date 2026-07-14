# BRD: ChatGPT / AI Integration

## What it is
ClickDokan lists "Chatgpt" as a per-plan feature — most likely an AI chat
widget for customer support on the storefront, and/or an AI-assisted
product-description generator in the admin panel. Exact scope on their side
is unconfirmed (no screenshot of this feature seen yet), so this BRD covers
the two most common versions of this feature in e-commerce SaaS products.

## Why it matters
AI-assisted content generation (product descriptions, blog drafts) saves
merchants time; an AI chat widget can answer basic customer FAQs on the
storefront without staff involvement. Either is a reasonable v1 depending on
which one is confirmed after further competitor research.

## Scope (v1 — pick ONE to start, don't build both at once)
**Option A — Admin-side AI product description generator:**
Given a product name + a few keywords, generate a draft description the
merchant can edit before saving. Lowest scope, no public-facing risk surface
(no customer-facing chat to moderate).

**Option B — Storefront AI chat widget:**
A customer-facing chat bubble answering questions about products/orders
using store data as context. Bigger scope: needs conversation history
storage, rate limiting, and careful prompt scoping so the AI doesn't
hallucinate policies or pricing.

**Recommendation: build Option A first** — smaller, safer, faster to ship,
and still delivers real value.

## Data model (Option A)
No new entity strictly required — this can be a stateless API call. If
generation history/audit is wanted: 
```
AiGenerationLog : AuditEntity
  Id, CompanyId, ProductId (FK?), Prompt (text), Response (text), CreatedDate
```

## API endpoints (Option A)
New `backend/Controllers/AiController.cs`:
- `POST api/ai/generate-description` — `{productName, keywords}` → calls an
  LLM API (Anthropic/OpenAI — pick based on cost/quality preference; use
  `IHttpClientFactory` the same way `SmsSender`/payment providers do) and
  returns generated text. **Do not store the AI provider's API key in
  `CompanySettings`/plaintext** — use environment variables like
  `JWT_SECRET`/`DB_CONNECTION_STRING` already are in `Program.cs`/`.env`,
  since this key is shared platform-wide, not per-tenant.

## Admin UI
On the existing "Add Product"/"Edit Product" page (`add-product.component`),
add a "✨ Generate with AI" button next to the description field that calls
the endpoint and fills the textarea with the draft (still editable/required
before save — never auto-publish AI text unreviewed).

## Open questions
- Confirm with an actual ClickDokan screenshot of their "Chatgpt" feature
  before committing scope — this BRD is a best-guess based on the feature
  name alone.
- Which LLM provider/API key will the platform owner supply, and what's the
  per-generation cost budget (rate-limit per company per day/month)?

## Implementation prompt
```
Implement an AI product-description generator for ZW Ecosystem per
docs/future-roadmap/09-ai-integration.md (Option A — NOT the chat widget,
that's explicitly deferred). Add environment-variable-based config for the
LLM API key (follow the JWT_SECRET/DB_CONNECTION_STRING pattern already in
Program.cs/.env — do not put this key in CompanySettings since it's
platform-wide, not per-tenant). Create backend/Controllers/AiController.cs
with POST api/ai/generate-description accepting {productName, keywords},
calling the LLM API via IHttpClientFactory (same pattern as SmsSender), and
returning the generated draft text. Add rate limiting per company (simple
count check, e.g. via a new AiGenerationLog entity or a CompanySettings
counter) to bound cost. On the frontend, add a "✨ Generate with AI" button
next to the description field on the add-product/edit-product page that
calls this endpoint and populates the textarea as an editable draft — never
auto-save without the merchant reviewing it. Verify it compiles before
finishing.
```
