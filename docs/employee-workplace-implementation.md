# Implementation: Employee Workspace and Administrator Oversight

**English** | [Русский](employee-workplace-implementation.ru.md) | [简体中文](employee-workplace-implementation.zh-CN.md)

Basis: [employee workspace requirements](employee-workplace.md), the [overall plan](plan.md), and current [employee rules](organization-employees.md). This plan clarifies the technical scope. Completed invitations, the intake form, and data migration are not implemented again. Mobile navigation and the completed part of the CRM are out of scope. After this priority, the next items are end-to-end auto service verification and replacing the post-build script with the Vite manifest.

## Scope and current state

- Detailing, auto service, and fleet share one authentication system, business model, and server. Employees need a compact view inside the CRM; separate clients for each scenario are not created.
- `OrganizationEmployee` stores assignments and history; `OrganizationMember` represents an account and its permissions. An employee profile already exists, but before this work it was not linked to a member.
- Services are stored as JSON in `Maintenance.Details`, without a stable row ID. Both service editing paths recreate the collection. Operation events must not refer to a row index or its name.
- Service states are currently `Planned`, `InProgress`, `Completed`, and `Cancelled`; readiness and handover are stored as additional dates. Completing a service currently marks it ready for handover immediately; this must change when administrator review is introduced.
- Inventory already accepts g/kg/ml/l, but a movement is stored in the item's unit, and expense synchronization deletes previous movements. Expense auditing requires immutable history and preservation of the original unit.
- The `employee` role currently has broad CRM, inventory, and finance permissions. The worker role must be separate, or its effective permissions must be explicitly limited by an administrator before opening the workspace.

## Phases

### 1. Team and access

1. Link an employee to a member only within the same organization; one member may be linked to no more than one employee profile. Revoking access preserves the employee and history.
2. Let administrators manage the link in “Team” and show its access state. Creating an employee without an account remains available.
3. Add narrow permissions for viewing and executing the employee’s own operations. By default, workers cannot access finance, plans, purchase prices, other people's work, or settings. The server checks membership, the link, and assignment independently of button visibility.
4. When a new person is invited together with an employee profile, acceptance of the invitation must link the profile while preserving the existing invitation flow.

First slice: link an existing employee to an existing member, with a migration, audit, and server-side checks. It does not grant the member new permissions automatically.

First-slice status: linking existing records is implemented, with organization ownership checked in the service and by a composite foreign key, account uniqueness, version checks, and audit events for linking/unlinking. Deleting a member removes the link with a separate event. Rolling back the migration drops the link column and therefore loses links created afterward; employee and member profiles remain.

Current status of phases 2–3: stable IDs for service rows and migration of old JSON records, a worker role, server-side operation transitions with version and request key, time intervals and events, the “My work” screen, administrator progress updates, and returning work with a reason have been added. The administrator confirms readiness through the existing service completion action; after operation tracking begins, that action requires every service row to be completed. A started record cannot be cancelled, returned to planned, or deleted, and an employee with an active operation cannot be unlinked from an account. Rolling back the work-tracking migration deletes the log and intervals, so execution data cannot be recovered after rollback.

Also implemented: profile creation and linking when an invitation is accepted, self-claiming unassigned work when allowed by the organization, administrator time correction with a reason, material issue and return per operation, notes and photos, approval of additional work, and a production report. “My work” is separate from administration sections and shows only assignments for the current employee, their planned time, stages, work price, and execution actions. Employee users do not see cost price or purchase prices.

A package has a fixed base-unit quantity recorded on receipt. An employee can consume it in full or in part, and the movement preserves the entered quantity and conversion factor. Automatically suggesting a service’s standard material quantity is not implemented yet: the catalog stores service links only for service kits, and has no material-to-service link or standard quantity. Server integration tests use the notification projects included in this repository; checks requiring containers need Docker. The migration keeps existing same-name inventory items in different compatible units separate to preserve history; new items use g, ml, or pcs.

Rolling back the base-unit migration restores the previous kg/l units for converted balances. If a new same-name item in a larger unit was created after the migration, rollback may require resolving a uniqueness conflict on `(OrganizationId, Name, Unit)` first.

Rolling back the package-factor migration deletes the stored `PackageQuantity` values; completed movements retain their recorded original quantity, unit, and factor.

### 2. Operations and time

1. Give every non-material service row a stable ID. Migrate old rows without losing name, price, quantity, or assignment. Both work-composition editing paths must retain the ID of an unchanged operation and prevent deletion of an operation with execution history.
2. Store operation state separately from service state: assigned → in progress → paused → in progress → completed. Starting and resuming open a work interval; pausing and completing close it.
3. Each command accepts an operation version and request key, atomically validates the transition, and stores a separate event: organization, service visit, operation, employee, member, old and new states, time, reason, and version. Repeating the same request returns the previous result. A concurrent change returns a conflict without creating a second event.
4. Pausing requires a reason. A time or mistaken-transition correction is a separate event with a reason and author; source events are not changed.
5. Starting the first required operation moves the service visit to in progress. Completing one operation does not complete the service. Once all required operations are done, it awaits administrator review. Only review marks it ready for handover; employees do not process payment or hand over the vehicle.

### 3. Workspace and oversight

- On a phone, an employee sees today’s work, active work, assigned work, and work completed during the shift. Work available for self-claim is shown only when enabled in organization settings.
- A work card contains the equipment and registration number, bay, planned time, the employee’s operations, intake details, approved additions, and materials. Actions depend on permissions and state.
- For each operation, the administrator sees the employee, time, pause and reason, remaining work, and review queue. The first update mechanism is a shared polling request; changes appear without a page reload, and a stopped or failed refresh is visible to the user.
- During review, an administrator can confirm readiness or return an operation to work with a reason and a separate event.
- A user with `maintenance.edit` may mark assigned, active, or paused work complete on behalf of an employee through the “Work control” dialog. A reason is required; the event records the author and the original employee. An open interval is closed, and no time is added to work that was never started. The action is available while the service is in progress and after completion until handover; the server protects cancelled and handed-over records. Repeating the command creates no second event; a stale version returns a conflict.

### 4. Materials

- Base quantities are stored as `decimal`: g, ml, or whole pcs. Receipts may use kg/l; consumption may use g/ml and compatible larger units. Packages use the material’s fixed conversion factor. Counted quantities are always whole numbers.
- A movement stores the entered amount, unit, factor, base amount, cost, organization, service visit, operation, employee, member, and request key. A correction does not delete the previous movement; it creates a reversal or adjustment.
- An employee can consume stock only for their own active operation; receiving stock, prices, and deletion are unavailable to them. Balances cannot go negative. A service’s standard quantity is suggested, but cost is calculated from actual use and the inventory valuation method.

### 5. Production workflow

- Defects, comments, and photos belong to a specific operation and are visible to the administrator.
- A request for additional work does not change the order composition or price before approval. The administrator’s decision is stored as an event; approved work gets its own operation.
- Reports for time, consumption, cost, and adjustments read the saved events and movements.

## Acceptance scenarios

| Scenario | Work card context and oversight |
| --- | --- |
| Detailing | Project, preparation and application stages, curing time, before/after photos, and coating quality review before handover. |
| Auto service | Work order, diagnostics, defects, approved additional work, parts, and a technical check before handover. |
| Fleet | Owned equipment, mileage/engine hours, scheduled maintenance, inspection, defect review, and documents; no external customer is required. |

Shared criteria: an account receives only its role in each organization; the server rejects another employee’s assignment and another organization; repeating a command creates no second transition or deduction; concurrent versions conflict; time equals the sum of closed intervals; history cannot be edited retroactively; completing one operation does not close the service; handover is unavailable before administrator review; progress updates appear without a manual refresh. Material checks cover 3 kg → 3,000 g, 200 l → 200,000 ml, consumption in a smaller unit, cost, balance, whole pieces, and correction history.

For each phase, compare the migration with its neighbors and the EF model, build, run server permission/concurrency/history tests, build all three clients, and check the interface on phone and desktop in both themes.
