# Organization employees

**English** | [Русский](organization-employees.ru.md)

Employees perform work without needing their own accounts. Their cards are in the “Team” section of the Detailing, Service Station, and Fleet workspace menus. A name is required; a phone number is optional. Archiving preserves previous assignments but prevents new ones. Job titles and specializations are not modeled.

“Team” shows employees, CRM users, and pending invitations in one list. Each row shows a name (or the email for a user without an employee card), CRM role, link status, and a “Card” button. A card can link a user to an employee or create an employee card. When adding someone, choose “Employee” or “CRM user”, then choose an access method. `#employees` opens the team list; the legacy `#members` route retains expanded access management. “Organization settings” opens from a single button without a dropdown, while “Directories” is in the “Work” group. The legacy `#catalog` route remains available. Permissions are checked independently; having an employee record does not grant CRM access.

In customer-assignee selectors, a CRM user is displayed using the name of the employee explicitly linked to that user in this organization; if the employee has no name, the email is used. Email is added in parentheses on the same line only when full names collide. The assigned owner is also shown by employee name in customer cards and lists. Unlinking restores email display; names in completed-work history are not rewritten.

The agreed merge rules are documented separately in [“Team and access”](team-and-access-design.md).

The `employees.manage` permission controls the employee directory (granted to the owner and administrator by default). The existing maintenance-edit permission controls assigning performers. Editing is unavailable in read-only mode.

A record stores `EmployeeId`, and `Performer` contains a server-side snapshot of the name. Organizations no longer save a free-form performer name. Personal vehicles retain the previous behavior. A service line can specify a separate employee; no assignment means “Same as the record”. Materials cannot have a performer assigned.

The server verifies that the employee belongs to the organization and is active. An unchanged archived assignment can be saved while editing other data. Renaming an employee does not rewrite names in past records. Changing or removing a performer is recorded in history with the author, time, previous assignment, and new assignment; employees with identical names remain distinguishable. Saving an assignment from the card uses the record version to protect against concurrent changes.

## Migrations

Apply `PreserveFullMaintenanceHistory` and `AddOrganizationEmployees` before starting the application. The first removes the history-description length limit. The second creates the directory and clears manually entered performer names only for organization vehicles. The old name is retained in a “Migration to employee directory” event with the displayed author “System update”; the required technical `UserId` for the event is inherited from the original record. Users and organization members are neither created nor deleted.

Rolling back `AddOrganizationEmployees` restores cleared names from these events while the employee directory is empty. If employees exist, rollback stops to avoid losing new data. Rolling back the history length change also stops if descriptions longer than 1000 characters have since been added.

Verification covers server scenarios for organization isolation, permissions, archiving, versions, and history; migration with populated data and rollback; and browser scenarios in all three clients, light and dark themes, and widths of 320–1500 px.
