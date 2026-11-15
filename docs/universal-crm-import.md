# Importing data from another CRM

**English** | [Русский](universal-crm-import.ru.md)

## Modules and entry point

The wizard is in the selected organization's settings and uses the shared `packages/workspace` component for Detailing, Service Station, and Fleet.
The server-side package lifecycle, file parsing, matching, preparation, and application log belong to `OpenCRM.Modules.Imports`.
Historical orders, service lines, payments, and events are created through the dedicated `IImportBusinessRecordProcessor` contract in `OpenCRM.Modules.Maintenance`.
The HTTP controller and background handler are in `OpenCRM.WebApp`. Wizard contracts are currently in `OpenCRM.Services.Interfaces/Imports`.

## Supported data

- Customers and vehicles, including links by an external customer ID.
- Historical orders in `completed` and `cancelled` states. Active orders and their schedules are not imported.
- Service/work lines: name, description, integer quantity, unit price. Every order requires at least one line.
- Separate payments, including cancelled payments: amount, date, method, and cancellation date. Active payments cannot exceed the order total.
- History events with original date, text, and author. In OpenCRM they are explicitly marked as imported events.

Dates use `YYYY-MM-DD` or ISO 8601; a time without an offset is treated as UTC.
Amounts use no thousands separator, a decimal point, and cent precision; the limit is `numeric(18,2)`.
Service lines also follow the normal Maintenance rules: quantity from 1 to 10000, line total no more than 999999999, no HTML, and up to 1000 characters.
The order total must equal the sum of lines less discount. Inventory movements, current notifications, receipts, and banking operations are not triggered by historical import.

The order's buyer and payment payer are not inferred from the vehicle's current owner.
`partyName` stores the historical name. Optional `partyClientId` links it to an enabled customer in the same package or to a customer previously imported from the same source database; for this link, the historical name must be supplied explicitly.
If no source link exists, only the text snapshot or an unknown value is retained.

## Files and mapping

A new package accepts CSV, XLSX, ZIP containing CSV, or a restricted text SQL export up to 50 MB and 100,000 source rows total across tables.
There can be up to 100 columns per table, 10,000 characters per cell, and 200 MB of unpacked XLSX data. Excel formulas are rejected.
Legacy synchronous HTTP contracts for customers and vehicles remain with their previous limits of 10 MB/5000 rows; the form uses the new batch API.

ZIP combines up to 100 non-empty CSV files into one package with shared relationships. The total unpacked size is limited to 200 MB, each CSV to 50 MB, and archive entries including folders to 200.
Files are read directly from the archive without extracting to disk; nested archives, SQL inside ZIP, executables, and paths escaping the folder are rejected.

SQL files are always parsed as data. Only these formats are supported:

- `INSERT INTO table (column, ...) VALUES ('text', 123, NULL, ...);`, including multiple tuples.
- Text PostgreSQL `COPY table (column, ...) FROM stdin;` terminated by `\.`.

INSERT strings use doubled single quotes; COPY supports standard text escape sequences.
SQL execution, DDL, procedures, expressions, binary dumps, `.bak`, restoring another database, and unsupported dialects are not supported.
Use a data-only export with explicit columns; unknown commands stop preparation with a clear error.
This is not an adapter for every CRM schema: the user verifies each table type and relationship in the wizard.

Automatic mapping is limited to unambiguous known headers. Columns can be selected manually and combined.
Rebuilding prepared rows requires separate confirmation because it discards manual edits.
An external vehicle ID is needed to link orders; an external order ID is needed for lines, payments, and history.
A historical order is limited to 1000 child rows so it can be applied as a whole.

Each mapped field can have an explicit transformation: phone digits with an initial Russian 8 replaced by 7, a date in one of the allowed exact formats, a decimal number without spaces using a period, or a source/target dictionary.
A dictionary helps map statuses and payment methods from another CRM. An unknown dictionary value or a date in another format stops preparation and identifies the table, row, and field; arbitrary programs are not executed.
Transformed values are available in the regular editable review before confirmation.

A personal organization profile stores header structure, mappings, transformations, and source name, but not export rows.
A profile applies only to a compatible table structure and requires confirmation before rebuilding; column order is checked.
An author can have up to 50 profiles per organization; saving again under the same name atomically replaces the settings. Only the author can view or delete them.

Before confirmation, the wizard shows a preliminary reconciliation of new records, totals for completed orders, active payments, and outstanding balance.
Duplicates, excluded rows, cancelled orders, and cancelled payments do not increase financial totals. If errors exist, the totals are provisional; import is blocked until they are fixed.

## Processing and recovery

`OrganizationImportDrafts` stores package state and version. Source rows, candidates, and temporary files are stored separately in `OrganizationImportSourceRow`, `OrganizationImportRow`, and `OrganizationImportFile`.
JSON in these tables is the format for source data and preparation snapshots; working customers, vehicles, orders, lines, and payments are created in the regular relational model.
After successful parsing, the temporary file is atomically deleted while prepared tables are retained.

The form requests up to 100 candidates per page and previews up to 20 rows from source tables.
Search and filters run on the server. Only changed rows on the current page are saved; a background check then recalculates relationships and the package summary.
Confirmation and further editing are blocked until the check finishes.
Unsaved changes are also copied to `sessionStorage` for the current user, organization, and package version.
A local version that differs from the server cannot be applied automatically; it can be downloaded as CSV before explicit deletion.

`OrganizationImportWorker` processes the persisted queue in small steps independently of HTTP requests.
A PostgreSQL advisory lock protects one package across processes. Applying each batch and its log markers is one transaction.
Order: customers → vehicles → complete order aggregates including lines, payments, and history.
A typical batch contains up to 250 customers/vehicles or 500 order rows; a large individual order is never split between transactions.
Each batch is applied atomically in a transaction under an organization lock. Before each page of new customers or vehicles, quotas are recalculated to account for records already created; Free limits include archived vehicles and customers removed from the interface.
If another operation consumes the remaining capacity between pages, the next page is stopped before any inserts; completed pages remain saved. The package enters a paused state with a reason and can resume after capacity is freed or data is adjusted; retry does not recreate already-applied rows.
Permissions, quotas, and the current state of customers/vehicles are rechecked before each batch. A changed result is returned for review.

After an application restart, unfinished jobs are resumed from the database. Repeating confirmation or resuming one package does not duplicate its records.
Pausing occurs between batches. Already-applied rows are read-only; remaining rows can be corrected and checked before continuing.
Historical orders require a stable name for the source CRM database (`SourceKey`), shared by all its exports. The name is normalized by trimming and case-insensitive comparison.
Different databases for the same CRM product must use different names. A filename is not a source identifier; the source cannot be changed after import starts.
The permanent relational registry `OrganizationImportIdentity` stores a unique organization, source, record type, and external-ID key, a typed link to customer/vehicle/order, and a SHA-256 fingerprint of prepared data.
External IDs are matched case-insensitively. Customers and vehicles imported in source mode require external IDs; a directory CSV without IDs can be imported without a source, using ordinary phone/VIN/plate checks and without a cross-file registry.

A matching key and content link to the existing record. Changed source data or a deleted/inaccessible target produces a conflict without rewriting current cards or history.
A historical order is compared as a whole with included jobs, payments, and events, preserving job order. A matching aggregate is skipped as a whole, even if it is larger than a normal batch.
New orders can refer to customers and vehicles previously imported from this database. Adding payments or history alone to a previous order is unsupported; the full aggregate must be reconciled through a separate update process.
Registering an external ID, creating the business object, and recording the log marker occur in one transaction; the check is repeated under the organization lock before application.
Packages imported earlier without a source are not automatically added to the registry: their origin cannot be reliably determined from filenames.

There is no global rollback across already completed batches. Partial results are shown explicitly; a full CSV report includes source links, amounts, dates, target IDs, and application time for every row.
Packages and logs are not automatically deleted, preserving continuation and provenance of created records.

## Access and migrations

The base wizard requires profile-management, customer/vehicle view and creation, and relationship-management permissions, plus the relevant subscription modules only when `PlatformPolicy:Commercial:Enabled=true`. In the default base mode, no paid plan is required; membership and permissions are still enforced.
Applying orders requires order view/create permission and the orders module; payments require finance view/manage permissions and the finance module.
An author can read and prepare their own file with base access. Another user's package may contain financial information not yet categorized, so viewing or changing it additionally requires full order and finance access.
Every route is scoped to the selected organization; the action author comes from the current authenticated account. Write HTTP requests are protected by antiforgery.

Required migrations: `AddOrganizationImportDrafts`, `AddOrganizationImportBatchStorage`, and `AddImportProfilesAndSourceIdentity`.
Rolling back the third migration deletes profiles and the cross-file source registry while retaining working records and packages. This removes cross-file protection; a separate reconciliation is required before importing again.
Rolling back the second migration deletes row-level drafts and their log; automatic conversion back to the old JSON format is not possible.
Created business records remain; a schema rollback does not undo an import that already completed. Back up packages and make a separate decision about partial results before rollback.
Check migrations on temporary PostgreSQL, not on a working database.

Pilot the first real export from every new CRM: check relationship structure, statuses, historical buyers, order totals, and payment totals.
The 100,000-row limit is a safety bound; it does not guarantee the same processing time for every file or server configuration.

## Administrative CRM import — integrity fix

This is a separate administrative path from the universal wizard: `/api/v1/admin/crm-import/*`. Its service and file parsing now belong to `OpenCRM.Modules.Imports.Crm`; the interface and existing DTO fields remain in `OpenCRM.Services.Interfaces.Imports`. `ManagePermissions` policy, routes, CSV/JSON/XML, `CrmImportBatch`, and `RowsJson`/`ResultJson` formats are preserved.

Preview still selects a user and their organization. On confirmation, the selected `batch.UserId` is used to validate membership; the actual administrator is taken from the authenticated `NameIdentifier` and passed separately. Customer and vehicle history retains the administrator, `EventType=imported`, `RelatedEntityType=crm-import`, and `RelatedEntityId=batch.Id`. Events are not backfilled into historical packages.

Confirmation locks the organization first, then the package, and rechecks source rows and allowed vehicle count. Vehicle creation uses the organization's shared `IVehicleLimitQueryService`: missing `VehiclesView` for the selected member does not change the admin workflow. A customer-only import does not require vehicle capacity and does not call this check. No admin exception to limits was added.

Customers, vehicles, history, and package result are saved in one transaction; a final save failure rolls back earlier inserts. Repeated confirmation remains a conflict and does not create records again. Separate packages compete for the same organization capacity; the limit is checked after acquiring the lock. The database schema and public JSON were not changed.
