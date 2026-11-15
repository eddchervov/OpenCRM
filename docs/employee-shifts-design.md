# Employee Shifts and Time Tracking in OpenCRM

**English** | [Русский](employee-shifts-design.ru.md)

> One solution for detailing and auto service, built on the existing employee and shift model. The sections below distinguish what already works from what is still proposed. Related guides are listed in the [documentation index](README.md). Proposed phases below are design options, not a promised implementation schedule.

## At a glance

1. A manager maintains the organization’s shared weekly employee schedule. The code already supports creating, editing, and cancelling planned shifts, time zones, viewing one’s own shift, and displaying assigned work outside scheduled hours.
2. Repeating the previous week and an availability hint when assigning a worker have been added. Authorized HTTP checks are complete; browser acceptance remains in the interface plan. Detailing and auto service use the same calculation based on employee and planned work-order time. It warns when work exceeds the shift but does not block urgent work. Availability is unknown when exact time is missing or the user cannot view the schedule.
3. Employees execute assigned work as they do today: starting, pausing, and completing an operation do not depend on a shift marker. Operation intervals are not the same as time spent at work.
4. Actual shift start and end will be added only after a confirmed need, and will be enabled separately by each organization. Ordinary clock events do not require manager approval; corrections require a reason and retain history.

There are no separate “simple” and “advanced” modes. Weekly repetition is enough for a 5/2 schedule; 2/2, 3/1, and other rotations, breaks, timesheets, reports, and payroll calculations are outside the next step. The shared model and shift screen are not duplicated for different business scenarios.

## Goal and baseline

A manager schedules the team and assigns work according to availability. On a phone, an employee sees the next shift and their operations. If attendance tracking is later needed, the employee can clock in and out without switching to a separate system; this time remains separate from operation execution time.

An employee profile already exists without a required account and can be linked to an organization member when needed. Operations retain assignments, states, events, and active-work intervals. Planner tasks belong to work orders and CRM actions. Therefore a shift is a separate entity linked to `OrganizationEmployee`. Operation intervals do not automatically populate a timesheet: they do not include waiting, preparation, or breaks between operations.

Related design for the unified team section, invitations, and permissions: [Team and access](team-and-access-design.md).

The solution is shared by detailing, auto service, and fleet. It does not change existing work-order permissions and does not require a shift marker to execute an assigned operation.

Current code references: [employee profile](../src/OpenCRM.Domain/OrganizationEmployee.cs), [operation intervals](../src/OpenCRM.Domain/MaintenanceWorkInterval.cs), [planner tasks](../src/OpenCRM.Domain/OrganizationSchedulerTask.cs), and [employee workspace](../src/OpenCRM.Frontend/packages/workspace/src/components/WorkspaceWorkSection.vue).

## Implementation phases

The first slice is implemented: weekly schedule, time-zone selection, create and edit planned shifts with immediate publication on save, cancellation, employee view of the next published shift, and display of assigned maintenance with out-of-shift time highlighted. Week repetition and assignment warnings are in code and await application checks. Actual clock events, breaks, unavailability, notifications, and reports described below remain proposals for separate phases.

## How shifts relate to maintenance

A shift means employee availability; maintenance is a specific assignment. Creating a shift does not assign work automatically. Maintenance or service does not need a separate `ShiftId`: the screen calculates the relationship from the employee and planned time. If a work order or shift is moved, the list is recalculated without manually relinking records.

- For each service, the worker is the service assignee, or the primary work-order assignee if no service assignee is selected. Materials are not work items.
- A shift cell shows only the number of assigned work orders and an out-of-shift indicator. Clicking opens a separate list below the schedule with date, employee, vehicle, and time, with links to work-order cards. If services in one work order are assigned to different people, each person sees their own assignment.
- Maintenance belongs to a shift when its reliable planned interval overlaps the displayed shift for the assigned employee. Partial overlap is marked “Outside shift.” For overnight work, instants are compared after converting the local schedule into the organization’s time zone; the maintenance calendar date must not be compared directly with the shift’s UTC time.
- If maintenance has no exact planned time, “Time not set” is shown and a same-day shift association is not asserted. Individual services do not yet have their own planned intervals: the whole maintenance duration is not counted as each service’s duration or summed as workload.
- When assigning or moving work, employees working during that interval are shown beside the worker selector. If a published shift covers only part of the interval or no shift exists, a warning appears. Saving remains available for urgent work; a draft shift is not treated as confirmed availability.
- Actual operation time is tracked separately from planned maintenance and shift attendance. Moving maintenance does not rewrite completed-operation history. Reports count a work order and service once even if the work spans two shifts.

## What users see

### Simple navigation

Managers get a separate “Shifts” item in the side menu. “Team” remains the people and access-settings list. The employee’s “My work” page gets a compact row for the next published shift. There are no permanent “Timesheet” and “Reports” tabs.

### Manager

- “Shifts” opens the current week, with employees in rows and days in columns. A cell shows shift hours and a short maintenance count; details and out-of-shift time appear in a separate list below the schedule. Primary actions are adding a shift and repeating the previous week. Copying a day and bulk actions are available in contextual actions. Organization opening hours are shown as a hint.
- A shift is published as soon as its form is saved. Employees see published shifts; notifications for additions and changes belong to a later phase. Older drafts can be published by opening and saving the shift.
- When assigning an operation, employees scheduled for that time are suggested first. If the selected person is unavailable, a short warning appears; an urgent work order can still be assigned.
- Attendance issues do not need a separate screen: “Review clock events” with a count appears only for open shifts, missing events, or correction requests and opens their list. The manager can correct an event with a reason from its row.
- “Period summary” in “Team” opens a team report with a period filter; on narrow screens it is in the header action menu. General “Statistics” remains an organization overview.
- A manager can schedule employees without accounts and enter their actual time manually when needed. Such records are marked as manager-entered.

### Employee

- The top of “My work” has a compact row with today’s/next shift, hours, and state. On a phone, one primary action is shown according to state: “Start shift,” “Start break,” or “Return”; “End shift” remains a second available action. The row is hidden until the schedule feature is enabled. When schedules are enabled without clock tracking, it shows planned hours only.
- After ending a shift, a short summary opens with attendance, breaks, active operation time, and completed work. These amounts have distinct labels and do not clutter the main screen.
- If an event was missed, the employee opens their shift and requests a correction with a reason. A manager approves or rejects it. Employees see only their schedule, clock events, and work.
- Starting an unscheduled shift and working outside scheduled hours are allowed; a manager gets an item to review. Network retries do not create a second clock event.

### Employee profile

The profile combines name and contact details, active status, account link, upcoming shifts, current assignments, and brief work totals. Member role determines CRM access; job function and specializations describe which work can be assigned. These concepts are stored separately. Specializations may be added after the basic schedule as optional assignment hints; they must not slow down adding an employee.

A separate “Unavailable” block lets a manager mark an absence without creating a fake shift: day off, vacation, or another reason. The reason is visible only to users with team-management permission; the planner needs only the unavailable state. Employee absence requests and approvals may be added after the basic schedule.

### Employee without an account

An employee can be assigned work and shifts without registering. They cannot use self-service clock buttons. A manager enters both planned and actual times; the report marks the source. If an account is linked later, history remains on the same employee profile.

## Data model and rules

1. Store an IANA time zone for the organization. The manager explicitly selects it when enabling the schedule for the first time. Existing `WorkdayStart`/`WorkdayEnd` remain suggested opening hours, not a replacement for shifts. Changing the time zone preserves existing shifts as UTC intervals but displays them and selects weeks in the new zone. Planned maintenance hours remain local wall-clock times.
2. `OrganizationEmployeeShift` stores OrganizationId, EmployeeId, planned start and end (empty for an unscheduled shift), planned break duration, local start date, time-zone snapshot, publication state, actual start/end, actual-time source, and Version. Actual start and end are UTC instants. A published planned shift must have both boundaries.
3. `OrganizationEmployeeShiftBreak` stores actual break intervals. Multiple breaks per shift are allowed. Attendance duration is the actual shift interval minus breaks.
4. `OrganizationEmployeeUnavailability` stores an unavailable period and category. A new shift or assignment during that period raises a warning but does not delete existing history.
5. Changes to published plans and actual time are recorded in existing organization audit with author, time, reason, and before/after values. Employee clock events and manual corrections are distinguishable. Operation corrections remain in the operation log.
6. Planned shifts for one employee cannot overlap; actual intervals for separate shifts cannot overlap either. An employee may have only one open actual shift and one open break. A break must be within an actual shift. Concurrent requests are protected by a transaction, version, and command key.
7. An overnight shift remains one record across midnight and appears on every affected day in the weekly schedule. Publication uses the local start date in the organization’s current time zone. Period totals use overlap between UTC intervals and period boundaries in the organization’s time zone. Ambiguous or nonexistent local times during a clock change require explicit selection/correction in the form.
8. The system does not silently close an open shift. It appears in the manager’s exception list. Ending a shift with an open break closes that break at the same instant after confirmation.
9. An employee cannot be archived with an open shift; earlier shifts and assignments remain available in history. Historical shifts are never deleted; a mistaken plan is cancelled with a recorded reason.
10. Starting, pausing, and completing an operation remain independent from shift clock events. Work outside a shift is flagged in the management report; the existing workflow is not blocked.
11. Historical operations from before shifts were enabled remain intact. For periods before launch, planned and actual shift values are “no data,” not zero hours.

## Access

| Capability | Owner/administrator | Employee | Employee without account |
| --- | --- | --- | --- |
| Shared schedule and timesheet | Separate view/manage permissions | No | No |
| Own published schedule | Yes | Yes | No sign-in |
| Self-service clock and correction request | If linked to an employee profile | Yes | No |
| Correct another person’s time | Separate permission, reason, and audit | No | No |
| Team-wide report | Reports permission | No | No |
| Money, cost, compensation | Financial permission only | No by default | No |

Proposed permissions: `shifts.view`, `shifts.manage`, `shifts.self.view`, `shifts.self.clock`, `shifts.time.correct`, and `reports.employees.view`. Owners and administrators receive shared permissions by default; employees receive only their own. Permissions for other roles are assigned explicitly. Permission to edit an employee profile does not itself grant permission to change a timesheet. The server always checks organization, linked employee profile, and action permission.

## Server contract

- Read the shared schedule by period and employee; read one’s own upcoming and current shift through a separate request. Employee responses contain no other people’s shifts.
- Create, bulk-copy, edit, and publish planned shifts; each change accepts Version and records an audit event. Conflicts are shown before a bulk action, and the confirmed set is saved atomically.
- Self-service commands for starting a shift, starting/ending a break, and ending a shift accept a command key for safe retry. Event time comes from the server; the client sends only the action and key.
- Correct actual time and create actual records for employees without accounts through separate manager commands with a required reason. An employee correction request does not change the timesheet before approval.
- Reports use a server-side time range and apply permission checks to each output column. Long periods are calculated on the server with pagination rather than loading all history into the browser.

## Reports and definitions

- “Employee work for a period”: employee, work order, operation, completion date, state, active time, corrections, and returns. For completed work, the period is based on the operation completion time. Historical lookup must be separate: current “My work” is limited to recently completed work orders.
- “Shift plan/actual”: planned hours minus planned breaks; actual attendance minus actual breaks; deviations and correction source. It does not count operation execution time as attendance.
- “Workload”: assigned hours and completed operation counts by employee and service. A returned operation is not counted as a separate work order.
- “Materials”: actual issues and returns by employee, operation, and work order. Cost and purchase prices require financial permission.
- Earnings are not included in these reports: service price and labor cost on a work order do not determine pay for a specific person. Payroll requires a separately agreed rate/commission and correction model.

The first report opens from “Period summary” in “Team”: current month and all employees by default, then filters by employee, service, and state. Each row links to its work order; CSV export is available after permission checks. A summary and list are enough on phones; the detailed table is intended for managers on desktop.

## Rollout phases

1. **Schedule without mandatory clock events.** Organization time zone, planned shifts, simple unavailable periods, weekly grid, week copy, publication, self-view, and assignment warnings. Existing execution workflows continue as before.
2. **Actual time and timesheet.** Shift start/end, breaks, manager entry for employees without accounts, correction requests, audit, and exception list. Self-service clocking is an organization setting. If actual-time tracking is off, schedules and work execution continue to work.
3. **Reports.** Employee work, plan/actual, workload, and quality. Financial columns are separately protected by financial permission.
4. **Additional convenience.** Repeating schedule templates, absence requests, optional specializations, and workload suggestions while booking a customer. Add these only after checking how teams use the first phases.

## Compensation as a separate phase

After business rules are agreed, add calculated compensation: effective-dated rules (hourly rate, fixed amount per operation, or percentage), manual corrections with a reason, period approval, and payment history. The calculation basis must be explicit: approved shift time or completed operations. Changing a rate does not recalculate an already approved period without a separate action. This is an internal organization calculation, not a replacement for HR or accounting. Until these rules exist, service price and labor cost are not shown to employees as their earnings.

## Verifiable scenarios

- A manager switches from the familiar employee list to the schedule, copies a week in one minute, changes two shifts, and publishes it; an employee sees only their new week.
- An employee starts a shift, takes two breaks, completes several operations, and ends the shift; the timesheet and operation time show different, correct totals.
- An overnight shift and an operation crossing midnight appear on the right dates and are not double-counted in reports.
- Retrying a clock action after a network failure does not create a second shift or break.
- An employee without a planned shift can perform urgent work; the manager sees an exception.
- An employee without an account appears on the schedule; the manager-entered actual time identifies its source.
- An incorrect time is corrected with a reason and visible history; an employee cannot change someone else’s shift.
- Older work orders appear in work reports, while the timesheet shows “no data” for periods before the feature launched.
