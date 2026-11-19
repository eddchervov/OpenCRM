# Organization Workday

**English** | [Русский](detailing-workday.ru.md) | [简体中文](detailing-workday.zh-CN.md)

This screen is the shared operations board for detailing, auto service, and fleet organizations. Statistical charts remain in “Statistics” and are not loaded with the initial workspace context.

## Contents

- “Planned” — appointments for the selected day and open appointments from earlier days.
- “In progress” — all work with status `InProgress`, regardless of the selected date.
- “Ready” — completed work awaiting equipment handover.
- “Handed over” — equipment physically handed over on the selected day.

Compact cards show time, vehicle, services, customer, phone number, bay, and employee. Users switch between “Stages” and “Bays”; the selection is saved in the browser. Wide screens show aligned columns, medium screens show two, and phones show one selected column with a switcher.

Stage headings use soft colors and icons: planned is gray-blue, in progress is orange, ready is yellow, and handed over is green. Cards for known makes use local SVG logos. The logo is selected using the linked make’s `Brand.LogoKey`, passed through the API as `vehicleBrandLogoKey`. The make is not inferred from a vehicle model name. If no key is set, the client does not know it, or the make was entered manually, no logo appears. The progress bar shows only operations actually completed. A ready status alone does not fill the bar to 100%, and missing operation data is not replaced with an assumed percentage.

A regular click on the vehicle name or open link displays the appointment in a modal over the board. It allows users to view services, intake photos, and the calculation, or open the full profile to edit the appointment and documents. Payments and status changes remain in the full profile. Escape, the close button, or clicking outside returns to the board without changing its date, grouping, or scroll position; focus returns to the link. On phones the dialog fills the screen. Links respect organization section access; Ctrl/Cmd-click keeps normal browser navigation.

Cancelled appointments, deleted records, and deleted vehicles do not appear on the board. The server returns the first 50 cards in each group and the full count. The interface initially shows five cards and lets users expand the rest; the complete list remains in the appointments section.

An organization with no work sees a compact first action. A load error does not clear cards already loaded and includes a retry action.

The “All appointments” link on the right and arrows on card-open actions were removed; the date is shown without an inner header, and arrows align with its heading. On narrow screens, the view switcher and refresh control share a row. Colored headings were restored only for stages; bay headings remain neutral.

## Implementation

- `packages/workspace/src/components/WorkspaceWorkdayBoard.vue` — shared board, date, loading, permissions, and responsive layout.
- `packages/workspace/src/components/WorkspaceWorkdayCard.vue` — shared compact work card.
- `packages/workspace/src/api/workday.ts` — contract and data request.
- `GET /api/v1/organizations/{organizationId}/workspace/workday?date=YYYY-MM-DD` — shared protected API.
- `OrganizationWorkspaceQueryService.GetWorkdayAsync` — shared server query for the workday across all operational scenarios.

The API checks organization mode, membership, work-module availability, and permission to view work. Customer name and phone are returned only when the customer module and corresponding permission are available. Create buttons reflect permissions and read-only mode.

Planned dates are sent as calendar dates without shifting. For actual handovers, the browser sends `timeZoneId`; the server converts the selected day boundaries to UTC.

“Bays” groups cards by the service’s saved bay field and does not fuzzy-match it against catalog entries. It shows bays in use and a “No bay” column; appointments within a bay are sorted by planned time. A separate ID-based link to the bay catalog remains a future model improvement.

## Checks

`Brand.LogoKey`, the workday’s `vehicleBrandLogoKey` field, and a migration that populates 20 makes were added. Inferring a logo from the vehicle name was removed. Keys are included in initial catalog data; administrators can assign or clear them through the admin API, while ordinary edits preserve existing keys.

All three clients build. The server query is covered by `DetailingWorkdayQueryTests`; the existing detailing-specific route by `DetailingWorkdayTests`. Tests for detailing, auto service, and fleet additionally cover the shared route using the same query and access rules. Visual checks cover both themes and widths from 320 to 2560 px.

## Readiness and handover

In shared detailing, auto service, and fleet cards, completing work is separate from handing equipment to the customer. The shared workday board is used in all three scenarios.

- “Ready for handover” completes the work and saves `CompletedDate` and `ReadyForHandoverAt`.
- “Hand over to customer” requires readiness and confirmation, then records `HandedOverAt`. If payment is outstanding, the form shows the balance; handover does not mark it paid automatically.
- “Cancel handover” requires a reason, records it in history, and returns the record to awaiting handover. Completion date, payments, mileage, and photos are preserved.
- To return a handed-over record to work, handover must first be cancelled. Returning to work clears readiness and actual completion date under the previous rules.
- Older completed records remain “Completed”; unknown handover is not filled in automatically. If the equipment is still at the organization, readiness can be marked explicitly.

Ready and handed-over records retain `MaintenanceStatus.Completed`, so financial reports and completed-work journals continue to include them as before. Handover and its cancellation do not recalculate mileage or change the planner task’s close date. Server permissions and a row lock within the transaction protect changes; repeated handover is rejected.

The detailing service journal offers “All completed,” “Ready for handover,” and “Handed over.” Its period filters by work completion date; the handover date is selected on the workday board. Handover filtering is applied before counting and pagination.

Migration `20260913150000_AddMaintenanceHandover` adds two nullable columns and a state-consistency constraint. It follows the intake migration; existing rows are unchanged. Apply it to the target database before running the new version. `Down` removes both columns and loses readiness and handover markers; it does not delete event history.

The EF model matches the migration; SQL `Up` and `Down` were checked.

## Bay visualization — prototype

“Visualization” is available beside the work screen in detailing, auto service, and fleet. All three scenarios use shared components in `packages/workspace` and the bays and maintenance records of the current organization.

### First-version rules

- A work order “In progress” shows its vehicle on the assigned bay regardless of planned date.
- A planned work order turns red when its date and time have passed. Past-date appointments are also red; a today appointment without a time is not considered overdue.
- Future appointments appear in the selected bay’s list, but the vehicle is not shown on the diagram in advance.
- A paused operation is marked “Paused”; this does not mean all work on the order is stopped.
- Multiple started or already-due orders on one bay are shown as a conflict. The panel lists every appointment, with started work first.
- Ready orders have a separate list; cancelled and handed-over vehicles are absent from the diagram. Physical placement is not tracked separately: the model intentionally relies on status and schedule.
- Bays are matched against the current `Location` field, case-sensitive and with surrounding whitespace trimmed. Unknown and disabled bays with work remain on the diagram with a warning. Records without a bay appear in a separate list.

### Data and access

`GET /api/v1/organizations/{organizationId}/workspace/visualization?date=YYYY-MM-DD` uses the same membership, module, and work-view checks as the workday. It returns all records from the three non-terminal groups without the 50-card limit. The regular workday API retains its limit. The menu item requires permission to view work and the bay catalog. A link to a work card appears only if its navigation section is available.

Data refreshes every 15 seconds in a visible tab; errors offer a retry. On failure, the last successful view remains with a warning. Switching organizations clears previous data. Migration `20261017104518_AddBrandLogoKey` adds an optional `Brands.LogoKey` field, up to 64 characters, for logo links. It assigns keys for 20 makes with local SVGs using an exact match on catalog name (including known Russian variants), without changing makes or vehicle relationships. Normal catalog initialization assigns keys to new known makes from built-in data and does not overwrite existing keys. A manually created make has an empty key unless one is explicitly provided through the admin API. To add a new logo, include an SVG in the client asset set and assign its key to the make record. In `AdminSaveBrandDto.LogoKey`, null or an omitted field during editing preserves the old value; an empty string clears it; maximum length is 64. `AdminCatalogBrandDto` returns the saved key. `Down` removes only the field and its values; the make table and relationships remain.

### Preview the prototype

Demonstration data is available only in a separate local preview. Run from `src/OpenCRM.Frontend`:

```powershell
npm ci
npm run preview:visualization
```

Open `http://127.0.0.1:5178/tests/visualization-preview.html#visualization`. This is the real detailing shell with test API responses; no account or server database is required. Mutating requests are blocked on the demo page. The page under `tests` is not included in the production build.

Display-rule checks: `npm run test:visualization` (Node.js 24). Server checks: `OrganizationWorkdayTests` and `DetailingWorkdayQueryTests`; the latter require Docker running PostgreSQL and RabbitMQ.

### Visualization interface adjustments

The “Workday” link was removed from the header; the refresh button uses the compact workday icon and sits beside the last-refresh time. Navigation arrows, the counter, and the technical ID were removed from the side panel. Bay selection is limited to 360 px and separated from the diagram by spacing; “Clear selection” clears a bay or collection. A bay is not selected automatically when opened; manual and automatic refresh preserve an empty selection. Selecting a bay on the scene opens its appointments; “View work order” respects section access. The “Waiting to start” counter opens appointments whose time has arrived, with vehicle, services, date and time, and a link to the assigned bay. Data refreshes automatically; retry is available after an error.

## Service card — local variant

The shared card for detailing, auto service, and fleet preserves CRM styling. The header shows the logo from `Brand.LogoKey`, appointment stages, and the actual count of completed service operations. Materials are not included in progress; if work state is unavailable, the card reports that instead of showing zero. Older completed records without a readiness marker keep the label “Completed.”

Services show the worker, operation state, active time, and line amount. The calculation, outstanding balance, customer, and documents remain available beside the content. On narrow screens, “Calculation” scrolls to and focuses that block. Materials, cost, and work adjustments are collapsed; intake and the full photo gallery are on a separate tab, while a few photos appear beside services. The “Notifications” tab shows existing customer notifications. Permissions for changing services, intake, payment, and adjustments remain in effect.

For a local preview, run `npm run preview:visualization` from `src/OpenCRM.Frontend` and open `http://127.0.0.1:5178/tests/maintenance-card-preview.html#maintenance/1`. The `?readonly` parameter enables read-only mode. It uses demo API responses and blocks mutating requests; the page is not included in the production build. No migration is required for this change, but the server and client must return and read the vehicle’s new optional `brandLogoKey` field.

Review comments were addressed: the preliminary report is visible outside the collapsed block, and an error loading additional reports does not reset successful progress.

The primary visualization remains a three-dimensional bay diagram; there is no separate card view because the workday handles that use case. Markers show the vehicle and registration number without duplication. The summary includes occupied/free bays and groups for waiting-to-start work, paused orders, records without a bay, and ready-for-handover work; a paused individual operation does not mean the whole order is stopped. Assignment conflicts are shown separately. The selected-bay panel shows the make logo from its key, real completed-work counts, schedule, and worker. A regular “View work order” click opens the existing modal; Escape returns to the selected group, and Ctrl/Cmd-click keeps link navigation. Viewing permission is determined by the scenario menu. Automatic refresh and retention of the last data after an error are unchanged. A simple bay list is available when 3D is unavailable.

For local checks, `/tests/visualization-preview.html#visualization` accepts `scenario=detailing|autoservice|fleet`, `extended` (conflict, no-bay appointment, and future appointment), `empty`, `error`, and `noaccess`. The demo uses test API responses and disables writes. Checks for groups, progress, preview, organization switching, and stale responses are in `src/OpenCRM.Frontend/tests/visualizationSection.test.mjs`.

At 320 px, the existing 320 px shell minimum remains; with a classic scrollbar, usable width is reduced by the scrollbar width.
