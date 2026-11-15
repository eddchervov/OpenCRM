# Shared simplified Detailing and Service Station workflow

**English** | [Русский](simple-maintenance-workflow.ru.md)

Implemented and checked locally. Development of the second-stage CRM inquiries feature has not started.

## Behavior

In “Work and schedule” settings, the “Simple / By stages” mode is available for Detailing and Service Station. A future visit in simple mode is saved as planned and occupies a bay. The card shows one primary “Hand over” button; intermediate stages, service-line statuses, and the overall completion indicator are hidden. Rescheduling, cancelling, and restoring an appointment remain available through actions.

“Hand over” opens a confirmation and then completes the work and records the handover in one request. Any outstanding balance is displayed separately; handover does not create a payment or change the amount due. Real tracked unfinished operations and unresolved additional work continue to block handover. Assigning an employee without operations does not require fictitious stage completion. If actual operations or findings exist, their section remains available to resolve the restriction.

When handing over a previously completed record again, its actual completion date and previously recorded readiness are preserved. Reaching the planned date does not itself complete the work. The existing undo-handover action remains available and requires a reason.

Calendar forms for both scenarios send an explicit intent to create a planned appointment. For legacy API clients, the absence of the new `IsPlannedBooking` field safely means a planned appointment. Explicit `false` in simple mode is intended for entering work that has already been completed; in staged mode, quick appointment creation remains planned. The form also selects the planned scenario when a future date is entered.

Fleet retains its previous workflow. The existing Detailing exception in the old-work validation was not tightened; changing an organization's mode does not migrate statuses or history of previous orders.

## Architecture

There are no new tables or migrations. The implementation uses the existing `MaintenanceStatus`, `CompletedDate`, `ReadyForHandoverAt`, `HandedOverAt`, history, services, and payment log fields.

The `simple_handover` command runs through the existing transactional Maintenance service with checks for organization, current mode and scenario, `maintenance.edit`, organization and record locks, and record version. Closing related defects, synchronizing the counter on actual completion, and scheduler synchronization are preserved. No false “ready for handover” message is queued after the atomic handover.

## Automated checks

- Targeted Maintenance checks cover transitions, handover, historical date preservation, access, version, operation guards, and side effects.
- Organizations quick-create HTTP checks cover Detailing and Service Station, both modes, explicit intent, and absence of the new field.
- The full frontend suite covers shared-mode availability, actions, a single handover request with version, errors, and write restrictions.
- Detailing and Service Station client builds and the WebApp build succeed; existing NuGet and compiler warnings remain.

## Browser and database verification

Verification used the full WebApp with real APIs and PostgreSQL on a separate test database with migrations applied; production databases were not used. Environment parameters (run address, container, and database) came from the test setup's own configuration. Shared notifications used an in-memory transport; external client bots and analytics were disabled; payment and S3 settings were test values. Photos, external messages, and acquiring were not checked in this pass.

A synthetic owner and test plans were prepared with a temporary tool. Both organizations, bays, services, schedules, and appointments were created through the real interface. This was the full WebApp with real APIs and PostgreSQL, not a frontend preview with a demo API.

| Browser scenario | Result |
| --- | --- |
| Detailing: enable simple mode | Setting saved; simple actions available after the card loads. |
| Detailing: future appointment, interval, bay, priced service | Appointment is “Planned”; the interval is occupied and excluded from available starts. |
| Detailing: reload and handover | One button is shown after reload; confirmation saves handover immediately without separate start/completion/readiness actions. |
| Detailing: reload again | “Handed over”, actual time, 0 paid, and the service balance remain. |
| Service Station: simple mode, future appointment, service | Planned status and occupied time confirmed in the schedule. |
| Service Station: handover after reload | One operation; after reloading, “Handed over”, 0 paid, and balance equal to the service price remain. |
| Service Station: switch to “By stages”, separate appointment | Four stages and the “Start work” button remain. Starting work is saved; after reload, “In progress” is active and readiness for handover is available. A full repeated run of performer operations was not performed. |

Detailing was viewed in light and dark themes, and Service Station was also viewed in a narrow browser area. This is not a complete matrix of every screen width and role. Role restrictions and error paths were additionally checked with automated tests.
