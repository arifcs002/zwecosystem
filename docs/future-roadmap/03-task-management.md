# BRD: Task Management

## What it is
An internal to-do/task list for staff — assign a task to a user, set a due
date, mark it done. Not customer-facing; helps a shop owner track "restock
Product X", "call supplier Y", etc. inside the same admin panel instead of a
separate tool.

## Why it matters
Multi-staff companies (the app already supports `User`/`Role`/`Permission`
with multiple staff per company) have no built-in way to delegate/track
internal work — they'd use WhatsApp or a paper list today.

## Scope (v1)
Simple task list: title, description, assignee, due date, status
(TODO/IN_PROGRESS/DONE), priority. No Kanban board, no sub-tasks, no comments
for v1 — those are good v2 candidates.

## Data model
```
Task : AuditEntity
  Id, CompanyId, Title, Description (string?), AssignedToUserId (FK → User),
  DueDate (DateTime?), Status ('TODO'|'IN_PROGRESS'|'DONE'),
  Priority ('LOW'|'MEDIUM'|'HIGH')
```
(Name the table `staff_tasks` to avoid colliding with any framework/DB reserved word `tasks`.)

## API endpoints
New `backend/Controllers/TasksController.cs`:
- `GET api/tasks?status=&assignedToUserId=`
- `POST api/tasks`, `PUT api/tasks/{id}`, `PUT api/tasks/{id}/status`, `DELETE api/tasks/{id}`
- Scope by `_context.CompanyId` same as every other controller; a non-admin user should probably only see tasks assigned to them (`assignedToUserId == _context.CurrentUserId`) unless they have a manage-tasks permission — decide during implementation using the existing `Role`/`Permission` check pattern.

## Admin UI
New `admin/src/app/components/admin/task-management/` — list view grouped/filterable by status, with a create/edit modal (assignee dropdown sourced from `UsersController.GetUsers`). Same `page-container`/`data-table` shape as `order-management`.

## Open questions
- Notify the assignee (SMS/email) when a task is assigned or nearing its due date? (Reuse `ISmsSender`/SMTP already in the codebase.)
- Should company admins see everyone's tasks, or only tasks they created/are assigned?

## Implementation prompt
```
Implement Task Management for ZW Ecosystem per docs/future-roadmap/03-task-management.md.
Add a Task entity (table name staff_tasks to avoid keyword collision) to
backend/Domain/Entities.cs — AuditEntity, CompanyId+nav, AssignedToUserId FK to User,
Title, Description, DueDate, Status, Priority. Add the schema via an idempotent raw-SQL
block in Program.cs. Create backend/Controllers/TasksController.cs with CRUD + a
status-update endpoint, scoped by CompanyId, where non-admin users only see tasks
assigned to them (check the existing Role/Permission pattern used elsewhere, e.g. how
UsersController gates role assignment). Build the Angular admin UI: a new
task-management standalone component (list filterable by status/assignee, create/edit
modal with an assignee dropdown populated from the Users API), same page-container/
data-table shape as order-management.component.ts. Register the route and add a nav
entry (new "Team" group or under Admin & Reports — your call). Verify it compiles
before finishing.
```
