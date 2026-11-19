# Employee Workspace

**English** | [Русский](employee-workplace.ru.md) | [简体中文](employee-workplace.zh-CN.md)

Implementation plan and verifiable scenarios: [Employee workspace and administrator oversight](employee-workplace-implementation.md).

Shift scheduling, time tracking, and team reports design: [Employee shifts and time tracking](employee-shifts-design.md).

## Primary priority

The employee workspace is OpenCRM’s primary product priority. It connects administrator planning with the actual execution of work in detailing, auto service, and fleet organizations.

The solution is implemented in the existing application and uses the shared server, authentication, and business model. Employees get a separate compact interface optimized for phones and limited to the permissions granted in the selected organization. Separate clients for different organization types are not created.

## Goals

- Administrators can see when work starts, pauses, and finishes without relying on verbal updates from employees.
- Employees can see their queue, active work, and only the data and actions needed for that work.
- Actual execution is tracked per individual service operation.
- Employees record consumed materials in units that make sense for the operation.
- All significant actions are recorded in history and cannot be silently replaced.
- The shared workflow is consistent across detailing, auto service, and fleet organizations; industry-specific capabilities belong to the specific work item.

## Team and access

The organization settings interface has one section, **“Team.”** Users should not need to understand the distinction between employees and organization members.

The business model preserves the distinction:

- an employee represents a worker, assignments, and completed-work history;
- an organization member represents an account and its access permissions;
- an employee may be linked to an organization member;
- revoking access does not delete the employee or their associated history.

From “Team,” an administrator can:

- create an employee without OpenCRM access;
- grant access to an existing employee;
- invite a new person and create their work profile at the same time;
- assign a role and permissions;
- revoke access without deleting history;
- deactivate an employee for new assignments.

One account may have different roles in different organizations. Selecting an organization opens its corresponding workspace.

## Work execution model

The state of an individual operation is separate from the state of the overall service visit.

Operation lifecycle:

1. Assigned.
2. In progress.
3. Paused.
4. Completed.

Service visit lifecycle:

1. Scheduled.
2. In progress.
3. Awaiting administrator review.
4. Ready for handover.
5. Handed over.

Starting the first required operation moves the service visit to in progress. Completing one operation does not complete the entire visit. Once all required operations are complete, the visit awaits administrator review. Employees do not confirm payment or hand over the vehicle.

Each start, pause, resume, and completion creates a separate event. Actual active time is calculated from work intervals. A correction to a mistaken action is recorded as a separate adjustment with a reason, author, and timestamp, rather than by silently changing the original record.

## Employee workspace

Primary views:

- today’s work;
- active work;
- assigned work;
- available work, if the organization allows employees to claim it themselves;
- work completed during the shift.

A work card shows:

- the vehicle or other equipment and its registration number;
- the bay or work location;
- planned time;
- operations assigned to the employee;
- intake details, notes, and “before” photos;
- approved additional work;
- required and already consumed materials.

Available actions depend on state and permissions:

- start work;
- pause with a required reason;
- resume;
- add a comment and photos;
- report a defect;
- request additional work;
- record consumed materials;
- complete the employee’s operation.

Additional work requested by an employee does not automatically increase the price. It goes to an administrator for approval and is added to the service visit only after approval.

## Materials and units of measure

Employees record actual material consumption in a unit convenient for the particular operation. The system automatically converts the quantity to the inventory base unit.

Supported groups:

| Group | Units | Constraint |
| --- | --- | --- |
| Counted items | pcs. | Whole quantities only |
| Mass | g, kg | Automatic conversion |
| Volume | ml, l | Automatic conversion |
| Packages | bottle, canister, and others | A conversion factor is configured per material |

Examples:

- receiving 3 kg of detergent creates a base balance of 3,000 g;
- consuming 300 g leaves 2.7 kg;
- receiving 200 l of oil creates a base balance of 200,000 ml;
- consuming 3 l and 200 ml leaves 196.8 l.

Quantities are stored as `decimal`. Each inventory movement preserves the entered quantity, entered unit, converted base quantity, and applied factor. This keeps the history understandable if the material settings change later.

Counted items accept only whole quantities. Values in the interface omit insignificant trailing zeroes.

Employees may consume materials only for active work they can access. They cannot receive stock, change purchase costs, or delete movements. Consumption is corrected by a reversing or adjusting movement recorded in history.

A standard material composition may be configured for a service. When work starts, OpenCRM suggests the standard amount, and the employee confirms or changes the actual consumption. Operation cost is calculated from the actual quantity consumed and the configured inventory valuation method.

## Permissions and audit

By default, employees have no access to finances, payments, plans, organization settings, member management, purchase prices, or other employees’ work.

The server checks permissions, organization membership, the account-to-employee link, assignment to the operation, and whether the state transition is allowed. Client-side restrictions are not a security boundary.

Significant actions retain:

- organization and service visit;
- operation;
- employee;
- authenticated member;
- old and new states;
- date and time;
- reason or comment;
- version of the changed record.

Commands must be idempotent and handle optimistic concurrency so a repeated click or two concurrent requests cannot create duplicate transitions or stock deductions.

## Administrator workspace

The service visit card shows the actual state of its operations:

- who is assigned;
- who started work and when;
- active work duration;
- pause state and reason;
- completed and remaining operations;
- additional-work requests;
- material shortages;
- readiness for review and handover.

Updates are delivered to the workspace automatically. The first version may use a shared polling mechanism, with a later move to SignalR. Kafka is not required for this workflow.

## Implementation phases

### 1. Business model and access foundation

- unify people management in the “Team” interface;
- link an employee to an organization member;
- define employee roles and permissions;
- add execution states and events for individual operations;
- add actual work intervals and adjustments;
- enforce permissions and state transitions on the server;
- add a migration and validate it against project rules.

### 2. Employee workspace and administrator oversight

- implement a responsive employee workspace;
- implement start, pause, resume, and completion actions;
- show operation progress in the administrator workspace;
- add a review step before work can be marked ready for handover;
- add automatic refresh and clear notifications.

### 3. Materials

- add unit groups and conversions;
- migrate inventory movements to normalized quantities;
- support receiving and consuming stock in compatible units;
- reject fractional quantities for counted items;
- add standard materials for services;
- support actual consumption from the employee workspace;
- preserve costs, balances, and history during conversions.

### 4. Production workflow

- add defects and additional-work requests;
- add employee photos and comments;
- add administrator approval;
- add reports for actual time, materials, and cost;
- if needed, add a shared-terminal mode as an additional access method.

## First working release acceptance criteria

- an employee accepts an invitation and signs in to the correct organization;
- the employee can access only the workspace and permitted operations;
- an administrator assigns the employee to an individual operation;
- the employee starts, pauses, resumes, and completes it;
- the administrator workspace shows updates without a manual page refresh;
- completing one operation does not close other operations or the whole service visit;
- actual time is derived from events and intervals;
- material can be received in kilograms or liters and consumed in grams or milliliters;
- a counted item cannot be consumed in a fractional quantity;
- inventory balance, cost, and movement history are calculated correctly;
- a repeated request does not create another transition or duplicate deduction;
- all changes appear in history with the employee and account identified.
