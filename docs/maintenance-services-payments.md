# Services and payments on an appointment

**English** | [Русский](maintenance-services-payments.ru.md)

Shared components are used by Detailing, Service Station, and Fleet. Public pages are unchanged.

## Working in the interface

- “Select services” opens a search with tabs for services and service sets. Multiple items can be selected and added together. Already-added items are marked and cannot be added twice.
- A service row shows quantity, unit price, and total. Quantity is a positive integer. The description can be expanded as needed.
- A service can have a price in the directory. A set references regular services belonging to the organization; prices are taken from those services when the set is added. A set containing archived services cannot be added until its composition is fixed.
- Editing composition on the card saves services only. Materials and their recorded cost, appointment state, dates, and payments remain unchanged.
- “Accept payment” appears next to the outstanding balance. The full balance is suggested, and a partial payment can be accepted. An amount above the current balance is rejected.
- History shows the amount, time, employee, and comment. An accounting error can be reversed with a required reason. This is not a bank refund.
- Payment changes require `finance.manage`; service changes require `maintenance.edit`. Read-only mode blocks changes.

## Cancelled appointments and financial totals

A cancelled appointment does not create accruals or a debt for work not performed. Recorded payments are not removed automatically. The user can separately cancel an incorrectly recorded payment through history with a required reason; this is not a bank refund. When the appointment is reopened, accrual and debt are recalculated from the saved estimate and active payments.

The local fix separates financial projection visibility (`IsVisible`) from inclusion in accruals (`IsAccruable`). For a cancelled order, `NetAmount` and `DebtAmount` are zero; original `GrossAmount`, discount, and order lines are retained. Actual recorded materials and labor are not erased; profit reflects the remaining costs as a negative amount. Active payments remain in the summary. Cancelled work is excluded from order count, accruals, and popular services, while the record remains visible in the financial list with a “Cancelled” label. Reports retain the previous grouping of payments by order date; this is not a new cash report grouped by the date money was received.

Migration `20261030224000_AddMaintenanceAccrualFlag` recalculates old financial rows and the hourly summary in one transaction and updates covering indexes. `Down` restores the previous derived calculations from orders and lines; source orders, payments, and their history are unchanged. Stop writes by the old application during the switchover. If only the application is rolled back, the new flag is not supported by the old writer; before returning to the new version, a coordinated recalculation of the projection and hourly summary from source data is required. Neither automatic database rollback nor automatic full-database recalculation at application startup is performed.

Check the production database separately by following the deployment instructions.

## Shared simplified Detailing and Service Station mode

Organization settings provide a shared “Simple / By stages” selector. In simple mode, a future appointment is saved as `Planned` and reserves the time; the primary “Hand over” button completes the work and records actual handover in one action. Intermediate stages are hidden on the card. Assigning an employee without starting operations does not require fictitious completion marks; existing checks still protect real unfinished tracked work and unresolved additional work. Payment remains a separate operation.

Previously completed appointments retain their original completion date and readiness when handed over. The staged workflow and hidden Fleet scenario keep their rules. `IsPlannedBooking` defaults to `true` for quick appointments, including old API clients that omit the field; explicit `false` allows completed work to be entered in simple mode. The database schema was not changed. See [implementation and automated and live browser checks](simple-maintenance-workflow.md).

## Data integrity

`MaintenanceDetail.Price` remains the line total, including quantity. This preserves compatibility with calculations, reports, and documents. `Quantity` defaults to 1; changing the catalog does not recalculate previously saved lines or rename them. Documents show quantities greater than one next to the service name.

`MaintenancePayments` stores operations, including cancelled ones. `Maintenance.PaidAmount` remains an aggregate for existing reports; normal organization editing no longer overwrites it. A new organization accepts payment for an appointment through its card after the appointment is created.

Payment operations run in a transaction with a record lock. The request ID protects against resubmission; the expected paid amount protects against a stale card. Repeating a cancellation does not reduce the amount twice. Service changes and the updated editor send the PostgreSQL `xmin` version so they cannot overwrite newer data.

## Migration

`20260913163729_AddMaintenancePaymentsAndServiceBundles` adds a payment log, service price, and service-set composition. Positive amounts previously stored in an organization's `PaidAmount` are migrated as an opening balance marked “Previously recorded”. The source amount is unchanged; the actual receipt date is unknown and is not shown as the date of a new payment. Personal vehicles are not included in the migration.

`Down` deletes the payment log and new catalog fields. Once they are in use, this loses payment detail and service-set composition, although the `PaidAmount` aggregate remains. A full data rollback requires a backup taken before migration.

## Verification

Integration checks cover repeated payment, concurrent requests, stale cards, cancellation with a reason, preventing payment above the debt, access to another organization, finance permissions, preservation of old prices and costs, valid service-set composition, and protection against the old editor. The interface is checked in three scenarios, two themes, and widths of 320–1500 px.

## Order-change transactions

Orders, lines, history, inventory, readings, and scheduler synchronization are saved in a coordinated scenario transaction. Commands are in `OpenCRM.Modules.Maintenance/Commands`; `ApplicationUnitOfWork` is in Infrastructure.Common. Transitions, versions, and locks retain their checks; this is not a general guarantee that every request executes exactly once.

The narrow rescheduling command changes dates and times, checks the current status after locking, and preserves other fresh fields. Concurrent scenarios are checked against PostgreSQL.

The [original transaction map](README.md) preserves the analysis of previous entry points; [current architecture](architecture-current.md) describes ownership. The historical map is not an instruction to call removed methods.
