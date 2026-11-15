# Organization setup

**English** | [Русский](organization-setup.ru.md)

The setup wizard opens automatically on the workspace when a user first visits a new Detailing or Service Station organization. Labels match the selected scenario. If dismissed, the wizard can be reopened with “Continue setup”. Once completed, the panel and button disappear from the workspace. After completion, service bays and services can be edited in “Directories”, and working hours in “Organization settings”. Fleet organizations do not show the wizard. Organization settings include a separate “Working hours and breaks” section. Settings storage and the API are shared; public pages are unchanged.

## Steps

1. **Work bays:** existing entries are preselected. You can enable the ones you need, rename unused entries, and add your own.
2. **Services:** choose active entries and prices. An empty price remains unknown rather than becoming zero. Saved maintenance records and their prices are not recalculated.
3. **Working days and hours:** set opening and closing times and any number of breaks for each day. The schedule highlights working hours in calendars; the range fields at the top and the slot grid use the selected day's hours. Appointments outside the schedule remain accessible. A working day cannot run past midnight.
4. **Finish:** go to the first appointment or return to the workspace. Saved data can be changed in directories and organization settings.

“Later” closes the wizard. Completed steps are stored in the database; unfinished input is stored separately in the browser for each organization. A draft from an outdated version does not overwrite server data. “Skip” does not save changes from the current step.

## Data integrity and access

Setup requires the `workspace.profile.edit` permission and write access under the subscription. The page does not show the wizard to employees who cannot manage settings or when the organization is in read-only mode. Server-side authorization is required.

Each step is saved in a single transaction. Organization locking and a revision number protect against concurrent changes and resaving an outdated step. Items retain their IDs and system codes: normal directory initialization does not recreate disabled entries. Foreign IDs, duplicate names, invalid prices, and invalid schedules are rejected.

A service in use, a member of an active set, or a bay with an existing appointment cannot be disabled. A bay in use cannot be renamed in the wizard because existing appointments are linked to it by name.

## Migration

`20260916100913_AddOrganizationSetup` adds only fields to `Organizations`: wizard step, revision, completion status, workday start and end, and weekdays. Existing organizations receive an incomplete setup with no schedule configured. The migration does not change appointments, payments, or the catalog.

`Down` removes wizard progress and the schedule; services and bays added through the wizard remain in the existing directory. Consider the loss of schedule settings before rolling back.

## First open

`SetupAutoOpenPending` is set when an organization is created. Existing organizations receive `false` during migration; an update does not force them to use the wizard. Automatic display is disabled for employees without settings-management permission and in read-only mode.

After directories load, the client atomically records the first open through `POST /setup/open`. Only one of several simultaneously open windows is allowed to display the wizard automatically. The flag is stored on the organization and is independent of the browser. Closing with the X, Escape, or “Later” does not complete setup, but automatic display will not happen again. Until setup is complete, users with permission can reopen it with “Continue setup”.

`20260916102622_AddOrganizationSetupAutoOpen` adds one Boolean column, with `false` for existing rows. `Down` removes only the first-display flag.

## Working hours by day and breaks

In Service Station and Fleet settings this section is called “Working hours and breaks”; in Detailing it is the third step of the existing wizard. The shared editor covers all seven days. Each day can be marked as a day off or assigned opening and closing times, with breaks added or removed without an artificial limit. The server validates weekdays, required workday hours, positive duration, time bounds, and overlapping breaks. The existing requirement for at least one working day remains. Overnight schedules, holidays, and date-specific exceptions are outside this version. These are organization hours, not employee shifts.

The weekly schedule is stored in `OrganizationWorkingDays`; breaks are stored in `OrganizationWorkingDayBreaks`. The workday composite key is organization/day; the break key also includes its start time. Updating the entire week runs under an organization lock and `SetupRevision` in the existing transaction. The setup API adds `WeeklySchedule`; legacy `WorkingDays`/`WorkdayStart`/`WorkdayEnd` fields remain as compatible summary fields for now. The legacy write format is rejected if it could lose different hours or breaks from a saved schedule.

Calendars in all three CRM clients show working intervals with breaks removed. The slot grid displays the selected day's hours and “Outside working hours” markers. When a working day is selected, the top range and grid use the same hours; the range can be changed manually for viewing without changing the organization's saved schedule. Service Station and Fleet, like Detailing, show a full-day calendar range of 00:00–24:00. Orders are not hidden or prohibited solely because they fall on a day off or during a break.

### Weekly schedule migration

`20261009005033_AddOrganizationWeeklyWorkingHours` adds two tables with foreign keys and time checks, then migrates each existing working day using the old shared hours and no breaks. Duplicate selected weekdays are collapsed into one row. If selected days contain an invalid weekday number or missing/reversed hours, the migration stops with an error instead of guessing the schedule. An organization with no selected days remains without a configured schedule. Old columns and orders are not deleted.

`Down` removes the new tables only if organizations have no breaks and no different hours by day. Otherwise rollback stops because the old schema cannot preserve that data. Before such a rollback, export the full schedule and decide separately whether to convert it to shared hours or restore a backup. No automatic data-losing conversion is performed.

## Service Station integration

The wizard is connected to the Service Station workday in the same way as to Detailing. The scenario code comes from `context.shell.scenario.code`; Service Station uses `service-station`. Wizard labels and badges match the auto repair scenario. The server-side first-open marker and access checks are unchanged; no migration is required. The scenario is covered by `src/OpenCRM.Frontend/tests/workspaceSetupWizard.test.mjs`.
