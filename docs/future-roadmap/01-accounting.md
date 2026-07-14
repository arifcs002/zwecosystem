# BRD: Accounting Module

## What it is
A bookkeeping section separate from Orders/POS — tracks money in/out that isn't
tied to a single sales order: expenses (rent, salaries, utilities), income
categories, and a basic ledger/journal so the store owner can see profit/loss
without exporting to Excel.

## Why it matters
Right now `Order.Total`/`Payment.Amount` only capture sales revenue. A shop
owner has no way to record expenses or see net profit inside the app — they
do it manually outside. This is a common ask for any SME storefront tool.

## Scope (v1)
**In scope:**
- Expense entry (category, amount, date, note, optional receipt image)
- Expense categories (Rent, Salary, Utilities, Marketing, Other — company-defined, not hardcoded)
- Simple Profit & Loss report: (Order revenue − Refunds) − Expenses, filterable by date range
**Out of scope (later):** full double-entry ledger, tax filing, bank reconciliation, multi-currency.

## Data model
New entities in `backend/Domain/Entities.cs` (follow the `Payment`/`Refund` entity style — extend `AuditEntity`, `CompanyId` + `Company` nav `[JsonIgnore]`):
```
ExpenseCategory : AuditEntity
  Id, CompanyId, Name (string)

Expense : AuditEntity
  Id, CompanyId, ExpenseCategoryId (FK), Amount (decimal),
  ExpenseDate (DateTime), Note (string?), ReceiptImageUrl (string?)
```
Schema via the existing `Program.cs` idempotent raw-SQL pattern (see the `fraud_checks`/`refunds` blocks added ~line 428+ as the template):
```sql
CREATE TABLE IF NOT EXISTS expense_categories (...);
CREATE TABLE IF NOT EXISTS expenses (...);
```

## API endpoints
New `backend/Controllers/AccountingController.cs` (`[Authorize]`, same shape as `RefundsController.cs`):
- `GET/POST api/accounting/categories`
- `GET/POST api/accounting/expenses` (with `?from=&to=&categoryId=` filters)
- `DELETE api/accounting/expenses/{id}`
- `GET api/accounting/profit-loss?from=&to=` — aggregates `Orders.Total`, `Refunds` (status=COMPLETED), and `Expenses` for the range

## Admin UI
New component `admin/src/app/components/admin/accounting/` (list + a small P&L summary card at top, same `page-container`/`data-table` pattern as `refund-management`). Add to the **Sales & Finance** collapsible nav group (`admin-layout.component.html`) alongside Payments/Refunds/Pricing.

## Open questions
- Should expenses support recurring entries (e.g. monthly rent auto-added)?
- Receipt image upload — reuse existing `UploadController` image-only restriction?

## Implementation prompt
```
Implement the Accounting module for ZW Ecosystem per docs/future-roadmap/01-accounting.md.
Add ExpenseCategory and Expense entities to backend/Domain/Entities.cs following the
Refund entity's exact style (AuditEntity, CompanyId+nav, [JsonIgnore] on nav props).
Add DbSets + query filters to ApplicationDbContext.cs. Add the schema via an idempotent
raw-SQL block in Program.cs (same pattern as the fraud_checks/refunds tables added
previously). Create backend/Controllers/AccountingController.cs with CRUD for categories
and expenses plus a GET profit-loss endpoint that aggregates Orders.Total minus completed
Refunds minus Expenses for a date range. Then build the Angular admin UI: a new standalone
component under admin/src/app/components/admin/accounting/ using the same page-container/
data-table/modal patterns as refund-management.component.ts, with a P&L summary card at
the top. Register the route in app.routes.ts (both route blocks) and add a nav entry under
the "Sales & Finance" collapsible group in admin-layout.component.html. Build and verify
it compiles before finishing.
```
