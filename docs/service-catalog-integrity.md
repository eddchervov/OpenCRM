# Linking services to an organization's catalog

**English** | [Русский](service-catalog-integrity.ru.md)

An organization record can combine catalog services and bundles with several arbitrary jobs. An arbitrary job requires a name, quantity, and price; `CatalogItemId` remains empty and no catalog item is created automatically. If an ID is supplied, the server validates that the service exists, belongs to the organization, has the correct type, and is available. Existing personal-account records without an organization retain their previous behavior.

Quick Detailing and Service Station appointments send their work composition in `Services`; the old `ServiceId` is supported for compatibility when the list is absent. For a row with `CatalogItemId`, the server uses the catalog name. For an arbitrary job, it saves the entered name. The appointment card returns both the ID and the text snapshot of the name. The price remains the agreed price for that particular job and is unaffected by future catalog changes.

When editing an existing row with the same ID, the server preserves its historical name. An archived service is allowed only on an existing row; an archived service cannot be added as an additional row. Changing the link when names are identical is also persisted and recorded in history.

Deleting services archives them, including services that have never been used. IDs are never reused. Archived services remain available in history and can be restored. Rules for other catalog types are unchanged.

## Forms

- “Select services” opens templates from `services` and service bundles. “Custom job” adds a row for manual input and can be used multiple times. `workTypes` contains process stages and is not a substitute for services.
- Initial services are still created in the database through `OrganizationScenarioCatalogService.EnsureDefaultsAsync`; forms do not contain built-in fallback lists.
- The full form edits all services on an appointment, considers the total of every row, and does not lose the second or later services.
- An empty `CatalogItemId` means a custom job. A supplied but invalid ID is still rejected. Adding templates preserves all previously entered rows.
- “Edit” in the selection dialog opens the relevant service or bundle directory in a new tab while keeping the form open. The link is available when the user can access the directory.
- An archived item is marked “archived”; after a rename, both its historical and current names are shown.

## Existing data

After custom jobs were allowed, a missing `CatalogItemId` alone is not an error. Legacy restoration scripts must not be applied again to all unlinked rows.

The database schema did not change for normalization: `CatalogItemId` already existed in JSON rows in `Maintenance.Details`; moving rows into a table was done by the normalization migration below.

1. Back up the database and run `scripts/maintenance/audit-service-links.sql`. It is read-only and includes historical and deleted organization records; personal records and materials are excluded.
2. Review statuses: `can_link` means one match, `not_found` means no match, `ambiguous` means multiple services, `invalid_link` means the ID does not point to a service in that organization, and `linked` / `archived` mean a link exists.
3. The script links only rows without an ID that have exactly one same-name service in the same organization, ignoring case and surrounding whitespace. Archived items count as historical matches. Names, prices, descriptions, and row order do not change. The script is transactional and repeatable; existing invalid links are not overwritten.
4. Run the audit again. Resolve remaining cases by explicitly selecting a service in the editor. Do not guess based on similar names or automatically turn stages such as “Reception” into services.

Use the backup to undo a link restoration: the script does not store the original links in a separate table.

“Maintenance” describes a one-time normalization of test data, not application behavior. At the time of that normalization the API required an explicit service selection; custom jobs are now allowed too.

## Verification

`MaintenanceServiceCatalogTests` checks saving through the service on PostgreSQL: nonexistent, foreign, and archived IDs and stages; custom jobs alongside templates without creating catalog items; historical names and prices; multiple services; changing only an ID; and preventing vehicle creation when a service is invalid. SQL checks cover archived items, ambiguous names, another organization, stages, invalid links, preserving other data, and repeat execution.

Browser checks cover all three clients, creation and editing, preserving IDs, multiple rows, archived items, an empty directory, light/dark themes, and widths of 320–1440 px.

## Bundle composition and other structured fields — requirements

### Bundle composition

`OrganizationScenarioCatalogItem.ServiceIds` was stored as `uuid[]`, not JSON. These are relationships between catalog items; composition has been moved to `OrganizationServiceBundleItems`. `ValidateServiceSettingsAsync` in [CatalogCommandService.cs](../src/OpenCRM.Modules.Catalog/CatalogCommandService.cs) already validates 1–100 distinct active services in the organization. Missing foreign keys on array elements are a storage-model limitation, not an absence of service-level validation. The migration must not weaken current rules. Currently, archiving a service through the catalog leaves a bundle containing it unavailable in the editor, while organization setup prevents disabling a service included in an active bundle. This difference between entry points is not fixed by adding a foreign key; before implementation, decide whether to preserve each entry point's rules or agree on a separate task to unify them.

Implementation criteria:

1. A relationship contains `BundleId`, `ServiceId`, and a position preserving the original array order. `(BundleId, ServiceId)` and position within a bundle are unique; bundle lookup by `ServiceId` has a suitable index. Do not add quantity for now: the current model includes a service once, while order quantity is a separate concept.
2. References point to existing catalog records. The bundle and service belong to the same organization and have the expected `KindCode`. Foreign keys on ID alone do not enforce both rules: validate the schema protection approach during design and retain service validation.
3. Archiving a service or bundle preserves relationships. New use of a bundle with an unavailable service remains prohibited, and existing order rows, names, prices, and quantities are not recalculated. Changing existing rules for editing a bundle with an archived service requires a separate domain task.
4. DTO/API responses continue to return `ServiceIds` in the original order. Update bundle reading, creation, and editing, service usage checks during organization setup, and accounting-data export. Check shared consumers in all three CRM clients: display, selection, availability, duration, and prevention of duplicate rows. Do not expand the separate Detailing process.
5. Before migration, check saved arrays for empty bundles, duplicates, missing IDs, another organization, incorrect type, and data on records of another type. An archived service alone does not mean a damaged link. Do not delete or replace invalid rows automatically; record them and decide how to resolve them before applying constraints.
6. Migrate all valid relationships while preserving positions; compare composition and ordering before and after, including archived bundles. Define compatibility with the old application version and release order before removing the array. Down must restore arrays in the same order; explicitly describe any data loss on rollback. Check EF migration attributes, Designer, snapshot, and the last valid neighboring migration according to project rules.
7. Build, run existing `BundlesReferenceOnlyActiveServicesOfTheirOrganization` and duration/catalog PostgreSQL checks, and build affected clients. Without migration verification on PostgreSQL, the item remains unverified; comparing SQL and building are not substitutes for migrating data.

`OrganizationServiceBundleItem` and `OrganizationServiceBundleItems` preserve composition and position. Foreign keys over `(Id, OrganizationId, KindCode)` and fixed-type checks enforce organization boundaries. Position uniqueness uses a deferred PostgreSQL constraint so ordering can be changed in one `SaveChanges` without intermediate conflicts. EF retains a regular index; the migration maintains the SQL constraint. No new data-access abstractions are needed for this one migration.

### Moving order rows

`MaintenanceDetails` contains the `(MaintenanceId, DetailId)` key, position, and all previous row fields. A foreign key to the order deletes child rows only on physical order deletion; soft delete retains them. Names, prices, performers, and materials remain historical snapshots. Nullable catalog IDs remain as they were: existing audit and restoration allow legacy invalid links. A foreign key on these historical IDs is not introduced now; it requires separate agreement and validation of old data. Links to completed operations, photos, and reports by `DetailId` are preserved.

Collection updates are reconciled by `DetailId`, preserving tracked EF rows and order-version checks. `Position` defines ordering; position uniqueness uses a deferred PostgreSQL constraint. Search and reporting read the row table; validation and unambiguous link-restoration scripts are updated for the new schema. The API continues to return the previous DTOs.

### Migration and release

Migration `20261009011254_NormalizeServiceBundlesAndMaintenanceDetails` creates both tables in one transactional transition. Up validates bundle composition, reference types and organization ownership, JSON row structure, unknown fields, and empty or duplicate `DetailId` values. An error stops the migration: data is not deleted and IDs are not replaced automatically. Fields and order are migrated with `WITH ORDINALITY`; row counts are checked before old columns are dropped. Unrecognized fields require expanding the migration schema.

Back up the database and test Up/Down on a PostgreSQL copy before applying. Stop old application instances and background handlers, then run the migration and start the new version; versions cannot run together after the columns are removed. Down restores `ServiceIds` arrays and JSON `Details` ordered by `Position` before dropping the tables. Domain values are restored, not original JSON formatting; JSON null is rejected before migration. Internal `MaintenanceId`/`Position` fields are not included in restored JSON.

### Other structured fields

| Field | Current decision | When to reconsider |
| --- | --- | --- |
| Maintenance.Details | Moved into `MaintenanceDetails`. | Revisit the schema for a concrete domain need, preserving snapshots, order, and IDs. The normalization migration has been checked. |
| CrmImportBatch.RowsJson | Keep JSONB for the existing batch import. `CrmImportService` loads and deserializes the whole list; the file is limited to 10 MB. This does not prove sufficient performance for all data sizes. | Measured memory/time limits, paged processing, separate row statuses, or retrying individual failures. Then design `CrmImportRows` and processing state. |
| CrmImportBatch.ResultJson | Keep as the final batch snapshot. | If processing moves to individual rows, move needed row results with them; the overall summary can remain JSON. |
| MaintenanceHistoryEvent.PayloadJson | Keep JSONB with variable event details; events themselves already have separate rows. | Extract concrete fields used for frequent filters/reports, preserving the historical payload. |
| EmailMessage.TemplateDataJson | Keep JSON template parameters for a specific message and template. | A change to the external template contract or proven need to query particular parameters, without broad normalization. |
| Organization.WorkingDays | A separate model for days and breaks is implemented; `integer[]` and shared hours remain as compatible summary fields for now. | Migration and saving were checked on PostgreSQL; date exceptions require a separate decision. See [organization schedule](organization-setup.md). |

The initial inventory changed neither application code nor database schema. The day-by-day schedule and breaks, bundle composition, and order rows were implemented in separate changes. Applying and rolling back normalization were checked on PostgreSQL; this stage did not add import changes.

## Work bays, closure, and history preservation — verification

An appointment is currently linked to a bay using text in `Maintenance.Location`. Therefore, a never-used custom bay can be renamed or deleted, while a bay that has ever been used can only be closed and reopened. A historical name cannot be assigned to a different new bay. A used bay is never physically deleted; closing it does not automatically cancel or move its appointments. Before closure, the interface lists unfinished assignments and warns that they must be moved manually.

The following were fixed based on an independent audit:

- Two active new bays with the same name used to be separate rows but represented one schedule capacity. Creation, renaming, and restoring now check occupied active and historical names case-insensitively and ignoring surrounding whitespace, under an organization lock. Concurrent creations with the same name keep only one bay. Existing duplicates are not automatically merged or deleted.
- Closed bays with previous appointments remain in Detailing and Service Station schedules with a “Closed” mark, but no new slots are offered for them. An active legacy bay with the same name as a closed bay remains available under the previous string-based contract.
- A closed bay can no longer be booked again by rescheduling an existing appointment. The shared check applies to full edits, appointment rescheduling, separate date updates, and returning a cancelled/completed appointment to a status that occupies a slot. Rejection does not change dates, status, version, rows, or history. Editing a comment/price without a new booking, starting and completing existing work are allowed; an appointment can be moved to another active bay.

Services retain their previous contract: changing a directory name, price, or duration does not recalculate a saved order or schedule; archiving prevents new service rows and preserves old rows. Deleting a service means archiving it, including unused services; IDs and historical snapshots remain. A bundle containing an archived service must have its composition fixed before it can be used again.

Real PostgreSQL checks covered the catalog, rescheduling restrictions across all routes, and preservation of historical service rows; the interface was checked separately against a demo API. One old catalog test container lacked `VehicleRegistrationPlatePolicy` configuration; the real configuration was registered in the test fixture, with no production configuration change.

Renaming a used bay remains a limitation of the current model. Safe support requires a stable `WorkPostId`, a separate historical name, and checks for old ambiguous assignments.

Coverage includes positive checks for renaming and deleting an unused bay, changing only the time, and reopening through the shared form. A complete end-to-end workday with real Billing is not included in these results.
