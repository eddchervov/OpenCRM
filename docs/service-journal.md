# Detailing service journal

**English** | [Русский](service-journal.ru.md) | [简体中文](service-journal.zh-CN.md)

The `#work-journal` section shows organization appointments from all time. The default status is “Completed”. You can select all, planned, in-progress, or cancelled appointments. Day-by-day scheduling and the calendar remain in “Appointments”.

One row represents one maintenance record and includes all its services, vehicle, customer, status, planned and actual dates, and total after discount. Selecting a row opens `#maintenance/{id}`. Materials are included in the total but are not listed as services.

Search by service, customer, make, model, and license plate is performed in PostgreSQL before pagination. Service rows are in the `MaintenanceDetails` table; parameterized SQL is executed by `OpenCRM.Modules.Workspace/Queries/OrganizationMaintenanceJournalQueryService.cs`. `%` and `_` are searched literally. The response contains only the current page and the total number of matching records.

The date range is optional. Completed records use the completion date; other records use the planned date. If an old record has no such date, filtering uses the stored maintenance `Date`. The upper bound includes the entire selected day. Results are sorted from newest to oldest, then by planned time, creation time, and ID.

API: `GET /api/v1/organizations/{organizationId}/maintenance-journal`.

- `status`: `0` — all, `1` — planned, `2` — in progress, `3` — completed (default), `4` — cancelled.
- `dateFrom`, `dateTo`: optional `YYYY-MM-DD` dates.
- `search`: optional text, up to 200 characters.
- `page`, `pageSize`: page and page size; the server limits the size to 100.

Organization membership and `WorkJournalView` and `MaintenanceView` permissions are required. Without `ClientsView`, customer names are hidden and excluded from search. Deleted maintenance records and deleted vehicles are excluded; other organizations are inaccessible.

There are no migrations or data changes. The existing combined Service Station and Fleet journal retains its API and behavior.

`MaintenanceJournalTests` checks statuses, older records, pagination, exclusion of other organizations and deleted data, search by manual and catalog names, services and materials, customer access restrictions, date range, and ordering. Browser checks use test API responses in light and dark themes, including narrow screens, loading errors, and navigation to the record card.
