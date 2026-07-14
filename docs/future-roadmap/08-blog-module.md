# BRD: Blog Module

## What it is
Lets a storefront publish blog posts (articles, announcements, style guides,
etc.) on their public-facing site — a common content-marketing/SEO feature
listed on every ClickDokan pricing tier.

## Why it matters
Blog content drives organic search traffic to a storefront and is a
standard expectation for any modern storefront builder — the app already
has an Ecommerce "Pages & Policies" builder (`ecommerce-pages.component`),
so this is a natural sibling feature reusing similar patterns.

## Scope (v1)
- Admin: create/edit/delete blog posts (title, slug, cover image, rich-text
  or markdown body, published/draft status, published date)
- Storefront: a blog listing page + individual post page, reachable from the
  storefront nav (extends the existing `ecommerce-navigation` builder)

## Data model
```
BlogPost : AuditEntity
  Id, CompanyId, Title, Slug (unique per company), CoverImageUrl (string?),
  Body (text — markdown or HTML), Status ('DRAFT'|'PUBLISHED'), PublishedDate (DateTime?)
```

## API endpoints
New `backend/Controllers/BlogController.cs`:
- Admin (`[Authorize]`): `GET/POST/PUT/DELETE api/blog/posts`
- Public (`[AllowAnonymous]`, tenant-scoped like `StorefrontController`):
  `GET api/blog/posts/public` (published only), `GET api/blog/posts/public/{slug}`

## Admin UI
New `admin/src/app/components/admin/ecommerce/blog/` (list + editor, follow
the same file/route pattern as `ecommerce-pages.component`). Add to the
Ecommerce collapsible nav group. Storefront-side rendering goes wherever the
public shop components live (`components/public-shop/` per the existing
folder structure seen in the Angular build output).

## Open questions
- Rich-text editor library choice (or plain Markdown textarea for v1 —
  simpler, no new dependency)?
- Do blog posts need categories/tags, or is a flat list sufficient for v1?

## Implementation prompt
```
Implement a Blog module for ZW Ecosystem per docs/future-roadmap/08-blog-module.md.
Add a BlogPost entity to backend/Domain/Entities.cs (AuditEntity, CompanyId+nav,
unique Slug per company, Title, CoverImageUrl, Body, Status, PublishedDate). Add
the schema via an idempotent Program.cs raw-SQL block with a unique index on
(company_id, slug). Create backend/Controllers/BlogController.cs with admin CRUD
plus AllowAnonymous public endpoints (list published posts, get by slug) following
the same tenant-scoping pattern as StorefrontController. Build the admin UI: a new
component under admin/src/app/components/admin/ecommerce/blog/ (list + editor with
a plain Markdown/HTML textarea for v1, no new rich-text-editor dependency) matching
the file/route pattern of the existing ecommerce-pages component, added to the
Ecommerce nav group. Build the public-facing blog listing + post-detail pages
wherever the existing public-shop components live. Register routes for both admin
and public sides. Verify it compiles before finishing.
```
