# Multi-day appointments

**English** | [Русский](maintenance-periods.ru.md)

One appointment contains the planned period, services, vehicle, customer, photos, and calculation. A separate maintenance record for each day is not required.

## Dates and times

- `PlannedDate` + `PlannedStartTime` — planned start.
- `PlannedEndDate` + `PlannedEndTime` — planned end.
- `CompletedDate` — actual completion date; changing status does not replace the planned period with it.
- Dates are stored as calendar dates in the format used by the project. Separate time fields are not converted to another time zone.

By default, the start and end are on the same day. For example, you can set a start at 10:00 on one day and an end at 18:00 two days later. Both times are required for multi-day appointments. The end must be strictly later than the start; an invalid period is no longer silently changed to one hour.

A single-day appointment without a time retains its previous behavior: it appears among untimed appointments and does not reserve a bay. If only the start of a single-day appointment is provided, the server saves an end one hour later, including across midnight.

## Bay occupancy

The selected bay is occupied **continuously for the entire period**. Drying and storage days are included in this reservation. Separate work intervals on different bays within one appointment are outside this stage.

Overlap is defined by `startA < endB && startB < endA`. If one appointment ends at 11:00, another can start at 11:00. An end at 00:00 does not occupy the next calendar day.

Availability is checked when an appointment is created, edited, rescheduled, or resumed. The save API uses the existing `IApplicationUnitOfWork`. Inside the transaction, a PostgreSQL lock serializes availability checking and saving by organization and normalized bay name. This also protects against simultaneous requests on different days of an overlapping period.

Cancelled and deleted appointments do not block time. Completed appointments retain their planned occupancy in the schedule: actual completion does not rewrite the original plan. When editing, the appointment itself is excluded from conflicts.

The form checks the entire range through `occupied-slots?date=...&dateTo=...`. The response includes absolute start and end times for hints and minute offsets from the first day for time selection. Server-side validation remains mandatory even if the interface previously showed the bay as available.

## Interface and related data

- Quick Detailing and Service Station creation, the full editor, rescheduling, and legacy personal forms send the end date.
- Rescheduling preserves the number of days in the period. The end date can be adjusted separately.
- The calendar selects appointments that overlap the visible range, including those that began earlier.
- Slots clip only their display to the selected day's boundaries. The full period remains in the database.
- Appointment, customer, and vehicle cards, plus the schedule list, show multi-day periods.
- The workday view distinguishes continuing appointments from past ones; for a continuing job ending on the selected day, the corresponding label is shown.
- PDF and Word documents list the planned period separately from the completion date.
- Services, payments, and materials remain on one appointment. Calculation and financial accounting rules are unchanged.

The existing shared maintenance mechanism was extended; no separate multi-day appointment entity or new service was added. Public website pages were not changed.

## Migration

Migration: `20260913120000_AddMaintenancePlannedEndDate` in `OpenCRM.Infrastructure.Migrations`.

It adds nullable `Maintenance.PlannedEndDate` and populates it from the existing `PlannedDate`. Durations are not invented for old appointments. Migration metadata, Designer, and snapshot are consistent with Entity Framework Core 10.0.7.

Apply the migration to the database before running the new code. Old code is compatible with the added nullable column. `Down` removes the column and therefore loses end dates of new multi-day appointments; keep the column when rolling back the application.

## Verification

Integration tests use temporary PostgreSQL 15 and RabbitMQ through Testcontainers, not production databases or queues. They cover:

- single-day and multi-day intervals, including month and year boundaries;
- occupancy on an intermediate day and the exclusive end boundary at midnight;
- adjacent intervals and conflicts;
- rescheduling, editing without losing the end date, and rolling back a conflicting change;
- two simultaneous bookings for one bay;
- migration application and model/snapshot consistency;
- independence of actual completion and the planned period in the workday view.

TypeScript/Vue builds, time selection functions, and multi-day event conversion in all three calendars are also checked.
