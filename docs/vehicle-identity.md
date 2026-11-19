# Vehicles with an unknown make or model

**English** | [Русский](vehicle-identity.ru.md) | [简体中文](vehicle-identity.zh-CN.md)

Make and model are optional when creating a vehicle or a quick appointment. Only entered data is stored: unknown `BrandId`, `ModelId`, `ManualBrandName`, and `ManualModelName` remain `null`. A license plate, VIN, body number, engine number, registration certificate, or user-defined name must still identify the vehicle. A license plate is required for a quick appointment.

`Vehicle · A667PO142` is a label for the interface and documents. It is not stored as the make, model, or name. If only part of the make or model is known, that part is saved; a missing value is never invented. A genuine user-defined name is stored separately.

The same vehicle can later be completed manually in the editor or matched to a model in the catalog. Its vehicle ID, appointments, photos, and history do not change. A subsequent quick appointment reuses the existing vehicle by its normalized license plate within the organization. Edit vehicle details through the vehicle card.

A suggestion for the shared catalog is created only when both make and model were entered manually. Empty fields and the interface label do not become suggestions.

## Legacy placeholders

Use `scripts/maintenance/audit-vehicle-identities.sql` for review; it contains only `SELECT` statements, and a read-only connection is suitable.

Cleanup is prepared in `scripts/maintenance/clean-vehicle-identities.sql`. This is an operational `psql` script, not a schema migration. Apply it as follows:

1. Deploy the fix and stop instances of the old version that create placeholders.
2. Run the audit and a dry run of the script without the `apply` parameter.
3. Apply the script with `-v apply=true -v backup_file=<new-absolute-path.json>` using the configured connection. Specify a new backup file for every attempt and keep it outside Git: it contains original vehicle data.
4. Run the audit again and inspect the affected vehicle cards.

The script processes only vehicles without catalog links where the manual model exactly matches the license plate and the name matches the previous “make model license-plate” formula. It clears the generated model and name. The make is cleared only when it matches the former placeholder for the vehicle type; a real make is retained. User-defined names, ambiguous matches, and catalog vehicles are left unchanged.

Before making changes, the script saves the original rows as JSON. If writing the backup fails, cleanup is aborted. It removes related pending suggestions only if they are not used by vehicles outside the cleanup set. Approved suggestions and the shared catalog are unchanged. Maintenance and history links are preserved.

Cleanup supports a dry run, rollback on backup failure, preservation of a real make and history, and repeat execution without additional changes. To restore data after applying the script, use the saved values by `Id` after checking that users have not updated the cards since cleanup.
