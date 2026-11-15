# Vehicle Intake and Before/After Photos

**English** | [Русский](detailing-intake.ru.md)

Damage numbers in PDFs are rendered as native QuestPDF text over the diagram. SVG no longer depends on a system font to render digits; Linux rendering has confirmed a visible “1” for a chip. Previously downloaded PDFs must be generated again. Damage types and stored coordinates are unchanged.

The shared detailing, auto service, and fleet work-order card provides intake and two photo groups. The vehicle condition is stored separately from the general work-order comment. Users can record damage, paint/coating details, and accepted equipment, or explicitly select “No remarks.”

- The first intake save records its time in UTC. Later updates do not change that time; the author and changes are recorded in the existing service history.
- Intake does not start or complete the work order. Status is changed through the card’s existing actions.
- Photos are uploaded to “Before work” or “After work.” Their stage can be changed without uploading again or changing the photo ID.
- Older photos remain “Uncategorized”; the application does not guess when they were taken.
- An image opens in a modal viewer. Stage selection and deletion are below the photo; deletion requires confirmation.
- The work-order editor also lets the user select the upload stage. Each of the three scenario editors has stage selection. Personal vehicles retain the previous upload flow without a stage.
- Changes require permission to edit maintenance and current access to write to the organization. The server checks organization ownership of the work order and photos.
- “PDF report with photos” includes photos with stage captions and intake notes. The regular PDF without photos also includes intake text.

## Damage markers on the diagram — implementation

A shared body inspection was added to the existing “Intake and photos” tab ([preserved concept](mockups/intake-workflow-concept.png)):

- Four detailed views appear at once. Selecting a view opens a larger diagram in a separate dialog. Clicking a point opens the damage editor and sets relative coordinates. On phones the views stack vertically. “Change location” opens a larger view without losing the description or photos; arrow keys and Enter allow point selection without a mouse.
- A damage record has a stable ID, number, type (“Scratch,” “Chip,” “Dent,” “Other”), description up to 1,000 characters, and photos. Each record has one primary point on the selected view. Visible numbers for active damage run from 1 and are recalculated after deletion—the same on the diagram, in the list, and in newly generated PDFs. Immutable UUIDs and internal creation order preserve photo relationships; internal sequence values are never reused. Existing history events and previously downloaded PDFs are not rewritten. New events link to damage by UUID in `PayloadJson`; their text does not depend on the mutable visible number.
- The user selects active “Before work” photos from the same work order. Upload from the editor uses existing storage; the uploaded photo must be selected explicitly. A photo linked to active damage cannot be moved to another stage until unlinked. Deleted photos are not shown in inspection.
- The server checks read/edit permission, organization access, and ownership of the work order and photos. Changes run in a transaction with a work-order lock and xmin check. A concurrent change returns 409; entered text remains in the editor. Replacing it with the newer server version requires an explicit user choice.
- The first inspection marker records the intake time; later edits preserve it. Status, old text remarks, mileage, and equipment are not overwritten. Adds, edits, and deletions are recorded in existing history. A successful save closes the editor and enlarged view; if the response is lost, the dialogs close only after the server confirms the saved record. A failed save leaves the draft open. A successful delete closes the confirmation and editor, closes the enlarged view, and clears the draft; a deleted marker cannot be saved again. A delete error remains visible in the confirmation; on a version conflict the old draft is not automatically given a newer version.
- Damage records and photo links use `MaintenanceIntakeDamages` and `MaintenanceIntakeDamagePhotos` tables. Composite foreign keys ensure linked rows belong to the same work order; these relationships are not stored in JSON.
- The PDF work order shows the same views and numbered markers, damage list, and captions for linked photos. The diagram is shared and follows the concept’s visual direction; vehicle body shape is not selected automatically from the model.

API: `GET /api/v1/maintenance/{id}/intake/damages` returns the work-order version and list; `PUT` and `DELETE /api/v1/maintenance/{id}/intake/damages/{damageId}` check that version. Every request sends `organizationId`; changes use the existing CSRF protection.

EF Core 10.0.7 migration `20261024101148_AddMaintenanceIntakeDamages` follows the latest Communications history migration. `Up` adds damage and photo-link tables, a schema version, integrity checks, a trigger, and transfers compatible historical data. The model, Designer, and snapshot were compared. `Down` deletes data and objects created by the migration; no automatic database rollback was run. A rollback image was prepared for the already-updated schema, so rolling back the application does not require downgrading the database.

Diagram previews: `Scenarios/detailing-client/intake-damage-preview.html`; the 390 px mobile iframe is `intake-damage-mobile-preview.html`. Full card preview: run `node tests/serve-maintenance-card-preview.mjs` from `src/OpenCRM.Frontend`, then open `http://127.0.0.1:5179/tests/maintenance-card-preview.html#maintenance/1`. The browser harness uses demonstration data. [Dark theme](mockups/intake-damage-dark.png), [tab in the full card](mockups/intake-damage-card-dark.png). Checks use a local demo environment.

Fuel, new fields for cabin items/customer requests, mandatory overview photos for each view, and separate confirmation steps from the concept require their own decision. They were not added in this change.

Damage and photo counts appear beside headings in orange circular badges.

## Inspection diagram version

An inspection uses a fixed diagram version; the published version is `sedan-v1`. It consists of `front.png`, `rear.png`, `left.png`, `right.png`, and `diagram.json` in `packages/workspace/src/assets/intake/sedan-v1/`.

One drawing and geometry version is pinned to the entire inspection in `Maintenance.IntakeDiagramVersion`. Before the first save, GET suggests the current version without writing to the database. Saving a marker must include the version of the diagram shown to the user: while holding the work-order lock, the server pins a supported version or rejects a mismatch with the already pinned one (409). The first save of intake text also pins the current version. Later edits and deleting all markers do not change it; a database trigger prevents changing or clearing the pinned value.

The version field, backfill of compatible existing intake records, and trigger protecting the pinned version are included in migration `20261024101148_AddMaintenanceIntakeDamages`.

Versioning supports future changes after launch: if inspections are already saved, a new drawing or geometry receives a new identifier and separate assets. Assets for used versions remain available to the client and PDF renderer. There is no earlier version yet. An unknown version is never replaced with the current one: the client retains access to the text list and disables coordinate edits, while PDF generation returns an error. A conflict with a different diagram requires reloading the inspection and selecting a point again.

Rolling back the combined migration removes its tables, version field, triggers, and data migrated by it. The database was not rolled back; the rollback image is designed to preserve the new schema. A backup/export of inspections is required before separately deciding to downgrade the schema.

## Diagram assets and checks

Each view in the first version has a transparent 1619 × 971 canvas. In the browser it occupies the full coordinate area without background shifts or cropping neighboring views. The frame is drawn as an inset shadow and does not reduce the image area. Light and dark themes use the same pixels; a filter changes outline color without moving it.

`diagram.json` defines the version, dimensions, file names, and SHA-256. The client and PDF use the same assets and manifest. PDF generation verifies checksums and dimensions when loading. Tests pin checksums for all four images, geometry, and exact relative coordinates, including edge positions. If an image is unavailable, point selection is disabled and a retry is shown. An unknown version is not replaced with the current one.

The original combined sheet is kept in Git at `scripts/assets/intake-body-projections.png` only to reproduce asset generation. `dotnet run --file scripts/build-intake-projections.cs -- <repository-root>` verifies the source SHA-256, doubles it using nearest-neighbor scaling, extracts four equal canvases, and clears the upper 20 rows of the side views where the neighboring view intrudes. The combined sheet is not imported by the client or embedded in the PDF. Re-running generation produces byte-identical PNGs and manifest.

Chrome and the embedded browser were checked: both themes, 390 px iframe width, zooming a view, adding and reopening damage, moving a marker with arrow keys while preserving description and photo, closing both dialogs after saving or deleting, and read-only mode. On a separate demo URL, `?imageError=rear` tested an actual image error and retry: the empty view remains disabled while others are available. The check uses the page’s in-memory demo API; the real API is covered by server integration tests. Firefox, Safari, a physical phone, and an end-to-end browser run with the real API/S3 were not checked.

## Compare and download

In the shared detailing, auto service, and fleet card, “Compare before/after” appears when at least one photo exists in each category. The dialog lets users independently select photos, view them side by side, or overlay them with a movable divider. The divider works with a mouse, touch, and a separate keyboard-accessible slider. Images fit in full without cropping; “Side by side” is preferable for different views. On phones, the images stack vertically in this mode. “Enlarge” opens the selected photo separately.

“Download PNG” saves a 2,000 px wide composition with the organization name and captions. “Side by side” exports both photos next to each other regardless of screen width; “Slider” preserves the divider’s current position. Buttons and internal work-order details are not included in the image. Export does not create a public link or publish photos.

Comparison is available to users permitted to view the work order, including read-only users. Image composition uses the authorized GET `/api/v1/media/maintenance/{maintenanceId}/photos/{photoId}/content?organizationId=...`: the server checks organization and work-order access and photo ownership. The existing storage service streams the response without caching. Therefore export does not depend on cloud storage CORS. Temporary image URLs are released when the pair changes and when the dialog closes.

Server tests for photo delivery (allowed request, someone else’s photo, inaccessible or missing work order, and unauthenticated user) and browser scenarios for pair selection, both modes, PNG content, mouse/touch/keyboard operation, load error and retry, zoom, closing, and focus return are covered. UI checks use test API responses. No new migration is needed for comparison.

## Storage and migration

The feature uses existing `Maintenance`, `MaintenancePhotos`, maintenance and media services, and change history. No new tables or services are introduced.

Migration `20260913140000_AddMaintenanceIntake` adds `IntakeNotes` (up to 4,000 characters), `IntakeRecordedAt`, and `MaintenancePhotos.Stage`: 0 — uncategorized, 1 — before, 2 — after. Existing data and files are unchanged.

Apply the migration before running the new version. `Down` removes the new fields and therefore loses saved notes and photo categories; the photos and maintenance records themselves remain. For an application rollback, it is preferable to leave the added database fields in place.

## Checks

Saving and updating intake, unchanged status and first-recorded time, text limits, access rights, organization ownership, photo categories, and migration/model consistency were checked. In the browser, errors and retry, upload, moving between groups, viewing, read-only access, both themes, and 390 px mobile width were checked.

## Check in an authenticated account

Run `scripts/maintenance/check-detailing-intake.js` in an authenticated OpenCRM tab using developer tools: Sources → Snippets → New snippet. Open the detailing workspace, paste the file content into a new snippet, and press Ctrl+Enter. Use an administrator account that owns the selected organization; the organization must permit changes.

The script uses the normal application API and current session. It creates a separate test service and bay, three vehicles, and three zero-cost work orders: planned, in progress, and completed across multiple days. Records are marked `ТЕСТ ПРИЁМКИ` (“INTAKE TEST”) and include a run identifier. For each record it creates intake notes and two synthetic images. No real customers or contacts are used.

The script checks rejection of empty intake, updates without changing the first-recorded time, photo categories, change history, status restoration, bay conflict, and PDF retrieval. Afterward, inspect PDF contents and card appearance separately: a PDF signature alone does not prove correct layout.

Results and links appear in the console. State is stored in localStorage under `opencrm:qa:intake:v1:<user>:<organization>`. A second run for the same account and organization is blocked, including after an error. If execution stops, inspect `error`, `pending`, and `records` in the saved state: a request may have reached the server even if its response was lost. Do not delete the key to rerun until you check for records already created. Test data is not removed automatically.

The script was checked in a separate browser with stubbed API responses. This does not replace running it in a real account; no result from that run is recorded here.

## Shared auto service and fleet intake

Intake, photo stages, comparison, and download are enabled in the shared card for all three scenarios. Upload-stage selection is also available in the maintenance editor. This extension adds no duplicated components, tables, or migrations. Scenario-specific headers, statuses, and linked defects remain distinct.
