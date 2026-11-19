# Manual Inquiries Before Booking: Implementation

**English** | [Русский](inquiries-implementation.ru.md) | [简体中文](inquiries-implementation.zh-CN.md)

Phase two of [CRM development](README.md) is implemented. The original [inquiries design](inquiries-design.md) preserves the scope and decisions.

## User workflow

The “Inquiries” section is available under “Customers.” The board shows four open stages; the list supports search, source, assignee, stage, and next-action due date. Closed and converted inquiries are available through a filter. The server calculates counts and data pages; the initial mobile view is a list.

An inquiry can be created without a customer profile, vehicle, phone number, bay, or appointment time: a contact and description of the customer’s interest are required. The user can set an assignee, a vehicle description provided by the customer, and one next action with a due date in the organization’s time zone. Changes and notes are recorded in history. Completing an action clears its current due date; closing an inquiry requires a reason. Workday includes a “Contact customers” panel with actions due on the selected day and overdue actions.

“Create appointment” opens the existing form for the selected scenario and carries over confirmed links, interest, and the vehicle description. An employee confirms the service, vehicle, time, and bay. An existing customer is selected explicitly; creating a new customer requires a separate checkbox and occurs in the same transaction. A matching phone number does not select a person automatically: the form offers the existing customer for selection. The ordinary customer creation flow keeps its existing rules.

Conversion creates a planned appointment in both operating modes. Repeating a request returns the previous result. The inquiry moves to “Booked,” gains a link to the work order and a history event, and its next action is cleared. Cancelling or handing over the work order does not rewrite the inquiry history.

In a service card, “Service history” opens a modal with events, details, author, and time. The existing creation event appears as “Appointment created from inquiry” with a link to its source: Maintenance reads the stored relationship through the Clients module’s `IOrganizationInquiryOriginReader`. This supports older conversions without duplicate events or a migration. If `inquiries.view` is missing, the feature is disabled, or the scenario is unavailable, the source link is hidden while service history remains available. Time uses the organization’s `ShiftTimeZoneId`; if it is unset, the browser’s local zone is used.

## Architecture and access

- `OpenCRM.Modules.Clients/Inquiries` owns inquiries, history, stages, and next actions. Public DTOs and interfaces are in `Clients.Contracts`.
- `OpenCRM.Modules.Organizations/Bookings/OrganizationInquiryBookingService` coordinates conversion through client contracts and the existing quick-booking services. Controllers handle HTTP and the user context.
- `OrganizationInquiries` and `OrganizationInquiryEvents` use relational foreign keys and indexes; a non-empty result link is unique. EF entities are not exposed through cross-module contracts.
- Inquiry locking, version checks, customer/vehicle/work-order creation, relationships, and history are handled in one `IApplicationUnitOfWork` transaction with an explicit save. Input copies for each attempt protect transaction retries from DTOs already modified by an earlier attempt. Existing Maintenance safeguards check bay availability.
- `inquiries.view` and `inquiries.manage` are registered in the permission catalog. Management requires view access; conversion additionally requires permission to create a service. Subscription and read-only mode are checked on the server. Fleet gets no new section or workflow API.
- Inaccessible relationships are hidden from the response and preserved when other fields are edited. Existing references to soft-deleted profiles remain; new references are checked for organization ownership and vehicle/customer consistency. A version conflict returns HTTP 409.

## Interface improvements

The profile separates the inquiry and customer contact, work actions, and a grouped-field editor. Optional customer and vehicle links expand separately. The bottom of the profile shows the three latest events and notes, with an inline option to add a note. Full history opens in a modal `dialog`, loads when opened, and has its own errors and pagination. Shared `InquiryTimeline` and `InquiryNoteComposer` provide consistent rendering and note entry in both places. Escape closes the dialog and restores focus; opening the note editor moves focus to the text field. A version conflict preserves the note draft. Completing an action shows the completed task text in history instead of an empty value; closing shows the reason without the legacy stage code. The corrected display also applies to previously saved events.

When changing an open stage, the user may provide an optional comment of up to 1,000 characters. It is saved in `Description` on the same `stage_changed` event, in the transaction that changes the stage and checks the version. Old and new stage codes remain separate values; the timeline shows both the transition and the comment. The old contract method is retained as an overload; the HTTP request adds a nullable `comment` field. An empty comment is allowed. Unknown or closed stages, choosing the current stage again, and comments that are too long are rejected without recording an event. Closing still requires its own action and reason. No migration is needed because the `Description` column already existed. Refreshing data after HTTP 409 preserves the main editor draft and stage-change comment; saving an ordinary note also leaves the selected stage unchanged.

The board has redesigned stage headers, counters, cards, and empty states. Filters expand from a button showing the active-condition count and a reset option; views share a single switcher. The list has column headings and a mobile layout showing stage, assignee, and due date.

Vehicle search in the editor uses `InquiryVehiclePicker`, shared by detailing and auto service. Results appear directly below the search field, selection is made from a row, and the selected relationship is displayed separately and removed through an explicit action. Enter starts a search without saving the inquiry. The first result receives focus and is brought into view; empty results, errors, and loading are shown explicitly. Changing the query or customer invalidates a stale response; a new search does not clear an already selected vehicle. Results are limited to five rows per page, with server-side pagination.

Due dates use the shared `UiDatePicker` and `UiTimePicker`. The time picker gained an optional `align` setting for popup alignment; the narrow inquiry field uses right alignment. `Organization.ShiftTimeZoneId`, when configured, defines the organization’s local time. If it is not configured, the API returns `timeZoneId: null`; the interface clearly shows the user’s local zone and sends the browser’s IANA zone in board, list, and Workday requests. The server uses it for day boundaries only when the organization has no configured zone; unknown zones are rejected. Instants are stored in UTC and shown and edited in the selected local zone. An unchanged due date retains its original instant, including during the ambiguous hour when clocks move back in autumn.

No load test was run against a large customer catalog; explicit customer creation reuses the existing potential-duplicate search.

## Migration and local environment

Migration `20261023100103_AddOrganizationInquiries` was created with EF Core 10.0.7. The `DbContext` and `Migration` attributes in Designer, class/identifier match, both configuration files, and the snapshot were checked; `has-pending-model-changes` reports no discrepancies.

`Down` removes empty tables. If inquiries exist, rollback deliberately stops before deletion: the data must be preserved and restored in a coordinated way. There is no automatic rollback that destroys history.

The standard local HTTPS profile uses `https://localhost:7142`; setup steps are in the [README](local-run.md). Notification delivery is disabled by default. The installation owner configures external integrations separately.

![Inquiry profile in the local application](images/screenshots/inquiry-card.jpg)

![Updated inquiry board](images/screenshots/inquiries-board.jpg)
