# CRM Navigation: Phase One

**English** | [Русский](navigation.ru.md) | [简体中文](navigation.zh-CN.md)

The transition from concept to phase one is complete. Detailing and auto service are the priorities.

## Changes

In the detailing and auto service server menus, existing pages are organized into groups: “Daily work,” “Customers,” “Resources,” “Reports,” and “Help and access.” Daily work starts with Workday and Appointments, followed by auto service work orders when applicable, then the service journal and visualization. “My work” remains available under its existing rules. Page codes, business section names, URLs, and permission checks are preserved. Menus for existing fleet organizations were not regrouped.

The existing shared navigation component is used: on desktop it appears on the left, and on phones in a modal drawer. No global tabs or new business sections were added.

When returning to detailing or auto service appointments, the selected date, list range, view, selected bay, available-slot duration, and appointment search in bay view are restored. Calendar navigation preserves the current date for its view. The bay filter is shared between “Bays” and “Slots”; duration applies to slots.

Context is stored in `sessionStorage` for the current user and organization. The view preference in `localStorage` is also scoped to the user; a legacy key shared by an organization is no longer applied to a new user. If the browser blocks storage, the screen works but does not preserve state after navigation. Invalid values are validated, and an excessively long period cannot produce an invalid end date. A deleted bay is removed from the filter after the catalog loads; if the catalog request fails, the filter is cleared so it cannot hide the schedule.

Customer, vehicle, and service journal lists already had their own filter persistence; those mechanisms were not rewritten. Browser-wide history and breadcrumb URLs were not changed. There is no new universal “return to the original appointment” action from every profile: profile return links use the existing links and history, while opening “Appointments” restores the schedule view.

## Verification

- Frontend: the full `node --test tests/*.test.mjs` run covers context restoration, organization/user isolation, unavailable and invalid storage, excessive date ranges, and catalog load failures.
- Server: the full `OpenCRM.Modules.Workspace.Tests` run includes shell checks and a permission matrix for detailing, auto service, and preserved scenarios.
- Detailing, auto service, and fleet CRM clients build successfully. Dependency audit and analyzer settings were not changed; dependency audit, compilation, and client bundle size warnings remain.
- Browser checks against the demo API at a wide range of viewport widths found no page-wide horizontal overflow in the three CRM clients. Light and dark themes were reviewed.
- In detailing, the interface was used to open customers and return to the saved date, “Slots” view, selected bay, duration, and restored search in both bay view and calendar.
- In mobile auto service with restricted permissions, available groups and the absence of financial items were checked; creating an appointment is unavailable in read-only mode. Escape closes the menu and returns focus to its trigger.

There are no database changes or migrations.

## Local preview

Run from `src/OpenCRM.Frontend`:

```powershell
node tests/serve-visualization-preview.mjs
```

Detailing: `http://127.0.0.1:5178/tests/scheduler-preview.html?navigation&empty&posts=3#scheduler`.

For auto service, add `scenario=autoservice`; use `readonly` for read-only mode and `restricted` for the limited demo menu. The `navigation` parameter enables the new menu only in the test harness; the production client receives grouped navigation from the server DTO without this parameter.

The harness uses real Vue components and test API responses. It verifies the interface; it does not run the full application against a working database. Some neighboring pages in the calendar harness have no test API responses: opening customers verifies screen navigation and return, but is not a full business test of customer and vehicle profiles. No production data was used.

Phase one of the [concept](README.md) is complete in the local implementation and checks. The next product task—specifying manual inquiries through appointment booking—has not started.
