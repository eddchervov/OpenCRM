# Rescheduling appointments from the calendar

**English** | [Русский](calendar-rescheduling.ru.md)

In Detailing’s “Appointments” section:

- In the calendar, an appointment with a start and end can be dragged to another time or day. Its duration is preserved, including across midnight and multiple days.
- In “Slots” mode, the initial appointment card can be dragged to another time and bay cell.
- The “Reschedule” button opens the same form without dragging; it is available in the calendar and initial slot cards. Untimed appointments are rescheduled through the calendar button with an explicit time selection.
- Dragging opens a confirmation dialog. The appointment stays in its original position until saved. The form shows the old interval and allows new dates, time, and bay. Changing the start date shifts the end date by the same number of days.
- Completed and cancelled appointments cannot be rescheduled. The action is hidden in read-only mode and without edit permission.

The existing `reschedule` action in `POST /api/v1/maintenance/{id}/workflow` is used; no new save route was created. The server rechecks permissions, status, and the entire planned interval inside a transaction. When changing bays, it checks the active directory of the current organization; an unchanged legacy name remains compatible. An empty string removes the bay assignment; an omitted field leaves its value unchanged.

The calendar response contains a numeric status, `version`, and `canReschedule`. The form sends the displayed appointment version: after locking the row, the server rejects a stale version. The version field remains optional for existing clients. Concurrent bay occupancy is protected by the existing transactional bay lock and overlap check.

History records the old and new dates, time, and bay. Services, payment, actual date, and status do not change during rescheduling. The schedule is reloaded after saving. Resizing an event by dragging is not included; the end can be changed explicitly in the form.

The new interface is enabled only in the Detailing calendar. The shared server contract was extended compatibly. Public pages and the Service Station and Fleet schedules were not changed. Rescheduling itself requires no migration.

Verification includes building WebApp and all three clients; server tests for moving to another bay, multi-day intervals, preserving services and history, stale versions, occupied/foreign/inactive bays, and closed appointments. Browser checks cover actual dragging in slot, week, and month views; cancellation, validation/server errors, permissions, read-only mode, light/dark themes, and widths of 320–1500 px.
