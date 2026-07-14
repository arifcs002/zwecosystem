# Future Roadmap — Competitor Gap Analysis (ClickDokan)

This folder tracks features found in competitor SaaS platforms (starting with
[ClickDokan](https://clickdokan.com)) that ZW Ecosystem doesn't have yet.
Each feature gets its own BRD (Business Requirements Document) file with
enough detail — including a ready-to-use implementation prompt — to hand to
Claude (or any dev) in a future session and start building immediately.

## Status legend
- 🔲 Not started — BRD written, no code yet
- 🟡 In progress
- ✅ Done

## Gap list (from ClickDokan sidebar/plans screenshot, 2026-07-15)

| # | Feature | Status | BRD |
|---|---------|--------|-----|
| 1 | Accounting module | 🔲 | [01-accounting.md](01-accounting.md) |
| 2 | Marketing Settings | 🔲 | [02-marketing-settings.md](02-marketing-settings.md) |
| 3 | Task Management | 🔲 | [03-task-management.md](03-task-management.md) |
| 4 | Ad On / Addon Marketplace | 🔲 | [04-addon-marketplace.md](04-addon-marketplace.md) |
| 5 | Subscription Plans UI (Plans page) | 🔲 | [05-subscription-plans-ui.md](05-subscription-plans-ui.md) |
| 6 | Referral Program | 🔲 | [06-referral-program.md](06-referral-program.md) |
| 7 | Custom/Sub Domain management | 🔲 | [07-custom-domain.md](07-custom-domain.md) |
| 8 | Blog module | 🔲 | [08-blog-module.md](08-blog-module.md) |
| 9 | ChatGPT / AI integration | 🔲 | [09-ai-integration.md](09-ai-integration.md) |

## How to use a BRD file
Each file has:
1. **What it is** — plain description of the feature, based on what's visible in the competitor's UI
2. **Why it matters** — business reason to add it
3. **Scope** — what's in/out for a first version
4. **Data model** — new tables/entities needed (following this repo's existing `AuditEntity` + `CompanyId` tenant-isolation pattern)
5. **API endpoints** — new controller routes needed
6. **Admin UI** — new pages/nav entries needed (following the existing `page-container`/`data-table`/collapsible-nav-group patterns)
7. **Implementation prompt** — a copy-pasteable prompt to give Claude Code to start building that exact feature, referencing this repo's real file paths and conventions

## Notes
- These are drafted from a single pricing/sidebar screenshot — scope may need
  revision once more competitor screens are reviewed (see the "Open
  questions" section in each BRD for what's still unconfirmed).
- Follow existing conventions already established in this repo when
  implementing any of these: `backend/Domain/Entities.cs` for entities,
  `ApplicationDbContext` query filters for tenant isolation, stored procedures
  in `backend/StoredProcedures/procedures.sql` for core writes, Angular
  standalone components under `admin/src/app/components/admin/`, and the
  collapsible sidebar groups in `admin-layout.component.html`.
