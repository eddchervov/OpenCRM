# Service Station and Fleet development

**English** | [Русский](sto-fleet-development.ru.md) | [简体中文](sto-fleet-development.zh-CN.md)

This document describes the shared domain model for auto repair shops and vehicle fleets. Scenarios available to new organizations are determined by installation configuration; hiding a scenario does not delete existing organizations or their data.

This document combines the current implementation, a previously recorded plan, and a new product direction.

It is a detailed domain model and development sequence, not a second list of current tasks. The technical documentation map is in the [index](README.md).

## Product goal

Detailing is the primary development focus; Service Station is second. Fleet is currently maintained as a copy of Service Station with possible small scenario differences. Fleet-specific features for operations, components, maintenance schedules, and utilization are deferred. This document describes Service Station and Fleet but does not give them priority over Detailing. Service Station and Fleet features do not have to appear in Detailing.

Improvements to work, performers, materials, defects, and documents should use the shared Service Station and Fleet foundation. A Service Station handles a vehicle belonging to an external customer and that customer's order. Fleet uses this process for its own vehicles and may later extend it with operational tracking. Discuss Detailing tasks in their own context. When changing Service Station, ensure the shared module still works for Fleet. If Fleet needs a different module, do not build its custom version now; revisit it after Service Station has developed further.

## What exists today

| Area | State | Evidence |
| --- | --- | --- |
| Shared foundation | Separate Vue clients for Service Station and Fleet use shared workspace packages and one server. | `employee-workplace-implementation.md` |
| Vehicles | Vehicle cards, defects, history, documents, mileage or engine hours. A vehicle currently has one primary counter. | `Vehicle.cs` |
| Maintenance | Appointments with multiple services, materials, prices, statuses, and payments. A work row has a stable `DetailId`; the current Vue form loads and sends multiple services with `CatalogItemId`. | `MaintenanceDetail.cs`, `MaintenanceFormSection.vue`, `maintenance-services-payments.md` |
| Work execution | Performer assignment, operation statuses, time tracking, additional work, material usage, and administrator review. | `employee-workplace-implementation.md` |
| Readings | Reading history with date and source; Fleet allows one manual vehicle reading per day and a trend view. | `VehicleCounterEntry.cs`, `VehicleService.cs`, `CountersSection.vue` |
| Planning | Current Vue clients use the job calendar and tasks API; the summary contains transitional calculations by date, reading, and defects. The former MVC page query was unused and has been removed. | `SchedulerSection.vue`, `OrganizationWorkspaceApiController.cs`, `OrganizationWorkspacePlanningQueryService.cs`, `OrganizationPlanningPolicy.cs` |

## Limitations and unfinished work

1. The next service interval is calculated from the last maintenance record for the vehicle overall. The operation type and status do not determine the reference point. Another job or a planned/cancelled appointment can distort the due date. See the active `OrganizationWorkspacePlanningQueryService.cs` and B3 in the business-process plan. The unused former MVC page query was removed; its algorithm is not a current application entry point.
2. Auto-planning intervals are extracted from a rule's name and description. There is no structured schedule for a specific operation on a specific vehicle or component. See `OrganizationPlanningPolicy.cs`.
3. Vehicles have no component model, and work rows and defects are not linked to components. `Maintenance.Details` is stored in the `MaintenanceDetails` table with stable row IDs and explicit ordering. See `MaintenanceDetail.cs` and `MaintenanceEntityTypeConfiguration.cs`.
4. Engine hours support entering an overall integer reading for a date. There is no separate “worked during the day” workflow, shift log, or independent daily utilization record yet. See `VehicleCounterEntry.cs`, `VehicleService.cs`, and `CountersSection.vue`.
5. Outbound messaging exists, but automatic delivery of maintenance reminders specifically has not been confirmed. See `notifications.md` and section 4.6 of `plan.md`.
6. The end-to-end Service Station scenario in a running application is still identified as the next check. Code availability does not mean operation in a deployed version has been confirmed. See `plan.md`.

Old items in section 4.6 of `plan.md` about a missing row ID and loss of multiple services in the Vue form need review: `MaintenanceDetail.DetailId` and `MaintenanceFormSection.vue` now address the basic problem described there. Before closing the item, check saving an existing appointment with multiple services and compatibility with old data. Update the status of the other items in section 4.6 after implementation and verification as well.

## Shared domain model

Vehicles, maintenance, individual jobs, defects, performers, materials, documents, and history are shared. One order can contain several jobs, each of which may later refer to its own component. Completing a particular job changes only the due date of its corresponding schedule.

| Service Station | Shared foundation | Fleet |
| --- | --- | --- |
| External customer, intake, appointment, approval, payment, and handover. | Vehicle → defect or planned need → job → performer and materials → completed operation → history. | Owned vehicle, department or responsible person, daily utilization, maintenance plan, and downtime. |

Fleet does not require an external customer. Future links to a department, driver, branch, and responsible person should not be disguised as an extended Service Station customer card. This is already noted in section 22 of `plan.md`.

## Determining the next maintenance reference point

Use the existing service catalog for individual operations such as an engine-oil change, filter replacement, or brake inspection. Represent TO-1 and TO-2 as templates of these operations. A set may include operations from another set, but the actual performed composition must be saved on the maintenance record. A set name alone does not confirm that all of its operations were performed.

Link a schedule to a specific operation and, when needed, to a vehicle component and type/model. It defines an interval by calendar, mileage, or engine hours, a warning threshold, and an initial point for a vehicle without history. For example, “change engine oil every 250 engine hours or once a year”. A TO-2 template can include this operation and additional jobs with their own intervals.

After a confirmed operation, save its date and the relevant counter reading at completion. That pair becomes the reference point for the next due date for that operation on that vehicle. Completing TO-2 updates the schedule only for operations actually performed; a cancelled order, unfinished job, or different service does not change due dates. If the required reading is missing, do not calculate an exact counter-based due value. For the first record, allow an initial date and reading or an explicitly specified upcoming threshold.

For Fleet, support two interval types: from the last actual completion and at fixed thresholds from an initial reading. For example, TO-1 at 250, 500, and 750 engine hours and TO-2 at 1000 engine hours. When TO-2 is completed at 1000 hours, the included TO-1 operations also count as completed, and the next fixed TO-1 threshold is 1250 hours. The interval type must be an explicit schedule setting, not inferred from the names “TO-1” or “TO-2”.

## Next-maintenance calculation module — design proposal for B3

Create a maintenance-planning domain module inside the monolith, shared by Service Station and Fleet: an application service and calculator in `OpenCRM.Infrastructure.BL/MaintenancePlanning`, with the result contract in `OpenCRM.Services.Interfaces`. A new project, separate process, HTTP API, and table of stored “next due dates” are not needed for the first stage. Revisit this proposal during B3 implementation based on the actual data model.

**Module boundary:** loading organization and vehicle data remains in the application service; a pure calculator receives a structured schedule, confirmed completion facts, current readings, and calculation date. It does not access the database, send messages, or build interface cards. The application service validates organization ownership and returns one result for the vehicle card, summary, scheduler, and future reminders.

**Input:** vehicle and catalog-operation IDs, optional component; a rule with date and/or counter interval, interval type, and warning threshold; initial values; confirmed operation completions with date and reading snapshot; and current reading with date. A service name or maintenance-set name is not an operation ID and is not a source for an interval.

**Completion fact:** stable `MaintenanceDetail.DetailId` already exists and `MaintenanceWorkOperation.Id` corresponds to it, but the operation currently stores status and `CompletedAt` without a reading snapshot; `Maintenance.Mileage` applies to the whole order. For a reliable reference point, provide a separate confirmed event for a particular row: organization, vehicle, order, `DetailId`, catalog operation, date, and reading snapshot with unit. Record the event consistently with confirmation of the result; determine the confirmation point for orders without a performer workplace during B3 implementation. Corrections to a fact must leave an auditable history. `Maintenance.Status == Completed` alone does not prove that every row was completed. For old orders without such a fact, use only a confirmed link or an explicit initial point, never infer from a name.

**Output:** next threshold date and/or counter value, remaining amount, state “normal/soon/overdue/insufficient data”, IDs of the rule and supporting fact, and the reason for uncertainty. For a counter-based due value, show the threshold and remaining amount; calculate an estimated calendar date from utilization rate later as a separately labeled forecast when the data supports a rate estimate. A calendar-based due date remains exact relative to the specified date and interval.

**Implementation:** first establish schedules and completion facts, then cover calculator cases for partially completed sets, a different job, cancellation, correction, and missing/stale readings. Next, move the scheduler and summary from their separate calculations to the shared service, connect the vehicle card, and only then add reminders. Do not store a calculated date as a second source of truth; any later cache must be recalculated from rules and facts.

## Proposed development sequence

### 1. Verify the Service Station workflow and strengthen shared work

- Walk through with a test organization: appointment → services → assignment → execution → review → payment → handover. Record actual UI and API gaps.
- Check preservation of multiple services, materials, prices, catalog links, and `DetailId` when editing old and new records.
- Fix maintenance scheduling to use the last operation of the required type that was actually completed. A planned or cancelled appointment, refueling, or a job under another schedule must not reset the due date.
- Use one due-date and urgency calculation for scheduler, summary, and vehicle card.

### 2. Add daily Fleet operations

- Give the responsible person a quick daily entry for each vehicle: a counter reading **or** engine hours worked that day. Confirm with pilot users which information they actually receive from operators.
- Store date, author, entry method, and corrections. When daily utilization is entered, calculate the resulting reading without losing the original entry.
- Show missed days and stale readings separately from zero utilization. Check duplicates, decreasing counters, and unusually large values while accounting for meter replacement/reset.
- Start with one overall vehicle utilization figure. Add individual component counters only for components that truly have their own service life.

### 3. Add components and operation schedules

- Introduce component templates by vehicle type and allow per-vehicle configuration. Distinguish a functional component from an installed individual part; full part-instance tracking is not needed in the first version.
- Link an individual job and, when appropriate, a defect to a component. Keep old records without components accessible.
- Configure a schedule for a specific operation: component, date and/or selected-counter interval, warning threshold, and reference point. Provide an explicit initial value for the first service.
- Calculate the due point at the first reached threshold; show uncertainty for missing or stale readings and label calendar forecasts as estimates.

### 4. Complete Fleet management

- Send reminders to the responsible person with duplicate-send protection and a clear action to create or open a job.
- Show queues of upcoming and overdue service, open defects, downtime, and expenses by vehicle and component.
- Build interactive component diagrams and advanced analytics after reliable operation-to-component links and verified due-date calculations exist.

## Criteria for the first useful result

1. Service Station mechanics and Fleet mechanics use the shared process; their cards show the fields relevant to each scenario.
2. Fleet users enter engine hours daily by vehicle and can see the source of the current reading.
3. A completed engine-oil change recalculates only its next due date; a hydraulic repair and a cancelled order do not change it.
4. The due date matches in the vehicle card, summary, and scheduler. If readings are insufficient, the system does not show a false exact date.
5. Historical work without a component remains readable, while new relationships and calculations do not expose another organization's data.

## Decisions to confirm during the pilot

- Which vehicles to start with: passenger cars, trucks, or special-purpose vehicles. This affects component templates and counter types.
- Who enters daily utilization: operator, mechanic, or dispatcher; whether approval and bulk shift entry are needed.
- Whether fractional engine hours and multiple independent counters on one vehicle are needed in the first version.
- Whether linking maintenance to an external contractor is needed in the first Fleet release.

## Source documents

- `plan.md` — overall roadmap, especially sections 4.6, 8.3, 8.6, 21, and 22; some statuses are outdated.
- `employee-workplace-implementation.md` — implemented performer process and scenario differences.
- `maintenance-services-payments.md` — multiple services, bundles, and payments.
- `notifications.md` — messaging infrastructure, separate from maintenance reminders.
