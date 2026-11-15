# OpenCRM server-side AI assistant

**English** | [Русский](ai-assistant.ru.md)

This document is the entry point for new tasks: read it first, then the [command catalog](voice-ai-actions.md). It does not replace reading the code you intend to change.

## Status and boundaries

The assistant is enabled through installation-specific settings. External providers require your own keys and budgets. Primary scenarios are detailing and repair shops. Existing rules for personal vehicles and the hidden fleet scenario remain in force.

The CRM's `#assistant` section for detailing/repair shops and a separate staff Telegram bot use one server-side conversation and executor. The old `/assistant/{organizationId}` checks access and redirects to the appropriate CRM. The `/ai-pilot` demonstration page is not part of the base distribution. Part of the engine remains in `Infrastructure/AiPilot` for historical reasons; that name must not be used as a reason to move execution into the browser.

RouterAI transcribes audio, parses a phrase into an allowed command, and produces an audio response when speech output is enabled. The model receives a catalog of user actions and required fields, not code, database schema, cookies, application secrets, or an arbitrary HTTP request tool. Persisted messages and necessary context are sent to the provider for subsequent parsing.

## Access and budgets

In neutral mode, a commercial subscription is not required for CRM work. With commercial policy enabled, access considers `workspace.assistant` and `workspace.client_messaging`. The server checks membership, user permissions, channel switches, grant expiry, and budgets regardless of commercial mode.

`AssistantAccessService.EnsureAvailableAsync` checks availability before command processing and an AI call; draft confirmation repeats the server check. Channel configuration does not replace permissions. Access changes do not delete history or spending; reads obey permissions/retention rules and do not trigger AI calls.

Budget is an independent restriction. Before a call, the server reserves funds under current limits. The particular installation's application defines plans, promotions, and trial terms; the core defines neither a promotional code nor a seller offer.

Customer messages recheck access before sending to the provider. Queues must not bypass disabled access; previously delivered history is retained under organization rules.

## Administrative management

Desktop administration has a separate AI assistant section. `Admin.Configuration.Manage` grants access to the global switch, website/staff-Telegram channel switches, speech output, models, voice, daily/monthly budgets, reservations, and token limit. Global disablement blocks operation and hides the CRM assistant button after context refresh; channels can be disabled independently. Maximum values come from server `AssistantOptions`; the client blocks saving out-of-range values and the server performs final validation. Saving settings sends `Revision`; on conflict, the interface reloads the server version, displays a message, and does not automatically retry the write. Settings apply through the save button.

In the same section, an administrator with `Admin.Configuration.Manage` searches organizations and views/changes globally managed grants, channels, expiry, and budgets for an organization. Access requests and settings changes enter the server audit. The organization form intentionally contains only fields supported by the current admin API; there is no separate grant comment in the contract yet.

Viewing spending requires `Admin.Billing.View`; reconciling a completed request with `unknown` status requires `Admin.Configuration.Manage`. For reconciliation, the administrator supplies the actual cost and reason, which the server audits. Report dates use the shared desktop `AdminDateTimeField`; boundaries are sent as calendar dates. Global disablement does not erase history, budgets, or organization settings.

## Command path and mandatory checks

1. The server establishes the employee, organization, and channel, then checks membership, permissions, subscription, switches, and grant expiry. The client cannot select an arbitrary executor on another user's behalf.
2. Before an external call, the server reserves budget. Recognized speech follows the same conversation as text; users can correct the transcript on the website.
3. The engine validates the command against the catalog and asks for missing information. New appointments, intake, and completed visits require a work item; completion requires a price. An explicit zero price is valid. Services are matched against the catalog; unknown work becomes a free-form line, and ambiguity requires selection.
4. The handler reads the target, prepares changes, and saves a server-side draft with a data snapshot. Multiple vehicles/appointments or similar services require selection. The common policy normalizes registration plates; the employee still checks recognition.
5. Changes occur only through the confirmation button; deletion/cancellation require additional confirmation. Typing “Да” (Yes) does not execute an action. Confirmation rechecks access and the target snapshot and atomically moves the draft into execution.
6. Business services apply ordinary CRM rules. Repeating a completed draft returns its persisted result. Unknown/partial outcomes must not be retried automatically: the employee checks the CRM. There is no shared transaction between order creation and a separate payment.

Plans, roles, website administration, code/SQL execution, arbitrary URLs, and external campaigns are outside the assistant catalog. Reads are also permission-limited. The assistant does not guarantee accurate recognition of registration plates or amounts and never bypasses domain validation.

Assistant tabs do not scroll horizontally: three columns on wide screens; up to 560 px, two columns with the last item spanning the row. Conversation remains on the main page. New message opens a native `dialog` for speech/text; a prepared action opens a separate review window. Closing does not execute the action or delete the draft; a conversation banner reopens it. Restoring a conversation shows that banner without automatically opening the draft. Closing the message window is blocked during recording, finalization, recognition, and sending; mode changes, another audio input, and sending also wait for current input. Recording can be stopped or microphone waiting canceled even after assistant access is disabled. Successful sending closes the dialog; an error stays inside with text preserved. Speech/text share one editable field; changing input mode does not clear it. Example phrases are collapsed; clarification selections belong to the conversation. The prepared action appears separately for confirmation; sending a message does not execute it.

## Data, history, and spending

| Data | Storage and purpose |
| --- | --- |
| Conversations and messages | `AssistantConversations`, `AssistantMessages`: user, organization, channel, context, and messages. The website shows web/Telegram history; only organization owners/administrators can read other team members' history. |
| Drafts | `AssistantDrafts`: target, snapshot, expiry, execution state, and result; duplicate protection resides on the server. |
| Spending | `AssistantUsage`: employee, organization, channel, model, tokens, cost/reservation, and request status. Conversation cleanup does not remove spending records. |
| Settings and audit | `AssistantSettings`, `AssistantOrganizationAccess`, `AssistantAudits`: global/organization switches and budgets, grant expiry, and administrative changes. |
| Telegram | `AssistantTelegramBindings`, `AssistantTelegramLinks`, `AssistantTelegramUpdates`: employee binding, one-time sign-in, queue, and duplicate-update protection. |

`Assistant:ConversationHistoryRetentionDays` defines history retention: the current initial value is 90 days after the conversation's `ExpiresAt`. Telegram technical records have a separate `HistoryRetentionDays`. `AssistantCleanupService` deletes expired records; there is no permanent archive or recovery of deleted conversations. Only the user's own active web conversation can resume on the website; the archive returns messages without internal state or commands.

Budgets are global and organization-specific, daily and monthly; website and Telegram share the organization's budget. Budget resets and remaining-balance cards use `Assistant:BudgetTimeZone`, as does pre-request server validation. Selected report dates use the organization's `ShiftTimeZoneId`, falling back to `Assistant:DefaultTimeZone`; the API returns `timeZone` and `budgetTimeZone` separately. Local day boundaries account for skipped/repeated midnight during clock changes. Limits and usage shows spending, reservations, remaining amounts, and breakdowns by employee/channel. Team-wide breakdowns require both `members.view` and `finance.view`; otherwise users see only their own details. Individual employee limits are not yet available.

A zero budget does not mean unlimited. Before a request, reservations count alongside actual spending; exceeding any budget blocks a new call. Unknown cost continues to occupy its reservation. Actual response cost may exceed the preliminary reserve: budgets stop subsequent calls and do not guarantee a precise ceiling for one request. Reconcile data against the provider account; administrative adjustment of unknown cost requires a reason.

The assistant uses the CRM's `opencrm.workspace.theme` selection: shared `wwwroot/js/organizations-entry.js` initializes before CSS, with palette selected by `data-crm-theme`. Light is the default; OS dark mode does not override it. The `storage` event synchronizes theme choices between tabs.

## Configuration and operation

- `src/OpenCRM.WebApp/appsettings.json`: `Assistant`, `AssistantTelegram`, and shared `VehicleRegistrationPlates` policy. Models, voice, URLs, technical limits, and retention periods come from configuration.
- `/admin-panel#/assistant`: enablement/visibility, channels, models, budgets, organization access, and spending. Database settings survive restart; configuration supplies initial values rather than overwriting them every startup.
- RouterAI secret: `Assistant:RouterApiKeyFile`/`RouterApiKey`, with legacy `AiPilot:ApiKeyFile`/`ApiKey` compatibility. Telegram token and signing secret use separate files. Never place secret values in documentation, source, logs, or screenshots.
- Local validation uses your own isolated database and test values supplied outside the repository. The README describes general startup; set the provider key through `Assistant:RouterApiKeyFile`. Core instructions hard-code neither database URL nor organization ID or budgets.
- Local Telegram uses long polling only in `AiPilot`, without a tunnel. Normal servers use an HTTPS webhook with a secret header. Do not run two instances of the same bot. One-time links from the website bind employees; ordinary `/start` grants no access. This staff bot is separate from customer CRM conversation bots.
- The assistant's Telegram button opens Telegram connections: current Telegram ID, date, connection/replacement link, and disconnect. This is account binding, not a device list; Telegram names/phone numbers are not stored. Owners/administrators see connections of current members who selected this organization in the bot; bindings from other organizations are not exposed. Users can disconnect only their own account, simultaneously across all organizations; pending one-time links expire and history remains.
- Organization-specific API keys and URLs are not yet implemented. Supplying an arbitrary model URL cannot enable them; separate secure storage, address validation, and preservation of permissions/accounting are required.

## Code map

| Area | Main files relative to repository root |
| --- | --- |
| Web and administrative entry | In `src/OpenCRM.WebApp/Controllers/`: `AssistantController.cs`, `Api/V1/AiPilotApiController.cs`, `Api/V1/AssistantDashboardApiController.cs`, `Admin/Api/AssistantAdminApiController.cs`. |
| Conversation and confirmation | `src/OpenCRM.WebApp/Infrastructure/Assistant/AssistantService.cs`, `AssistantAccessService.cs`, `AssistantActionExecutor.cs`, `Actions/`. |
| Parsing, catalog, RouterAI | `src/OpenCRM.WebApp/Infrastructure/AiPilot/`: `AiPilotConversationService.cs`, `AiPilotActionCatalog.cs`, `RouterAiPilotClient.cs`. |
| Spending, history, settings | `Infrastructure/Assistant/AssistantUsageService.cs`, `AssistantDashboardService.cs`, `AssistantSettingsStore.cs`, `AssistantCleanupService.cs`, `AssistantOptions.cs`. |
| Telegram | `Infrastructure/Assistant/Telegram/`: polling, queue handler, service, API client, button signatures, and settings. |
| Model and migration | `src/OpenCRM.Domain/Assistant/`, `src/OpenCRM.Infrastructure.Data/EntityTypeConfigurations/AssistantEntityTypeConfiguration.cs`, migration `20261026135550_AddServerAssistant` with Designer and snapshot. |
| Interface | CRM entry: `src/OpenCRM.Frontend/packages/workspace/src/components/WorkspaceAssistantButton.vue`; administration: `Admin/admin-client/src/views/AssistantView.vue`. |
| Validation | `tests/OpenCRM.Assistant.Tests/Unit`, `Integration`; conversation and business regression in the respective test projects; frontend in `src/OpenCRM.Frontend/tests`. |

## Change validation and rollback

For affected scenarios, test clarifications, ambiguous targets, permissions/disablement after preparation, stale snapshots, repeated confirmation, unknown outcomes, budgets, and absence of leaks across users/organizations/channels. For Telegram, check repeated updates, signatures, binding expiry/single use; for the interface, history, detailed-view permissions, and mobile layout. Changes to ordinary business services require their own regression checks.

Stock balance adjustments pass the read item's `UpdatedAt` to the business service. If stock changes between draft preparation and confirmation, the server rejects the stale adjustment without overwriting newer data.

Disabling the assistant blocks entry/new requests/confirmations while preserving data. Rolling back `AddServerAssistant` removes assistant tables with history/spending; orders, vehicles, customers, and payments already created by it are not automatically undone. Deploy with migration verification and backups; local startup/readiness do not replace production acceptance.

`src/OpenCRM.Frontend/tests/assistantDialogs.test.mjs` additionally checks dialog transitions: stopping recording after access disablement, the asynchronous stop → ASR gap, blocking Escape/sending/input changes, text preservation, reopening drafts, and mandatory dangerous-action confirmation. UI tests do not replace server authorization/idempotency checks.

Conversation history has separate pagination for conversation lists and messages; navigation hides when only one page exists. Telegram management is tested in `assistantTelegram.test.mjs` and `TelegramManagementIntegrationTests.cs`.

Razor period fields use actual shared `UiDatePicker` components from `@opencrm/ui`. The `packages/ui/src/razorDateTimeFields.ts` adapter also supports `UiTimePicker` for time fields. Hidden fields store ISO date / HH:mm for the API; `opencrm:field-sync` passes loaded values and disabled state to the component, while component selection updates the hidden field and emits change. `npm run build --workspace @opencrm/ui` produces `wwwroot/app/date-time-fields.bundle.js` and CSS; normal frontend builds and publishing include both, and publication checks require both files. Do not reintroduce native browser pickers here; root/frontend `AGENTS.md` enforce this rule.

Validate this integration with `razorDateTimeFields.test.mjs`, `vue-tsc` typechecking, WebApp build, and browser selection/manual entry/clearing/disabling, both themes, and the right-hand field at 320 px width. The limits page does not request time, so no separate time field is added there.

“ИИ - Голосовой помощник” (AI - Voice assistant) is the `#assistant` section of the shared detailing/repair-shop `WorkspaceCrmShell`. The header button opens it without leaving CRM; the left menu, profile, and notifications remain available. `WorkspaceAssistantSection.vue` checks availability and embeds the existing interface in a same-origin `/assistant/{organizationId}/embedded` iframe without duplicate headers, organization title, or footer. This isolates existing JS handlers/native dialogs without copying business logic. The embedded endpoint rechecks session, access, and organization, returns no-store/noindex, and X-Frame-Options/CSP allow only the same origin. Both parent CRM endpoints and the frame allow microphone=self; the browser still requests microphone permission. Result links default to target=_top so records open in CRM rather than the nested form. Theme uses the shared storage key. Leaving destroys the iframe/handlers; the server conversation/draft restores on return. Unsent local message text is not a server conversation. After a confirmed action, the child notifies CRM; the parent accepts events only from its own iframe, origin, and organization, then refreshes context. Local `/ai-pilot` remains a separate preview with the shared account layout. Assistant toasts use a separate `assistant-toast` class to avoid inheriting Bootstrap `.toast` rules.

On Free, the assistant button opens a static `#assistant` section inside CRM, where View features displays a description and demonstration draft in the shared PRO dialog. Availability, iframe, microphone, and external AI calls do not run in this mode. Plan navigation requires the relevant organization permission; server access, budgets, confirmations, and the separate `/assistant/{organizationId}` remain unchanged.

The static example/description explicitly cover text and voice commands on the website and staff Telegram bot, plus spoken replies. The assistant is marked BETA: capabilities are evolving and prepared actions must be checked. The description preserves bot connection conditions, administrator permission for speech output, and a separate AI budget. The demonstration playback control triggers neither audio, microphone, nor requests.

The global website switch is passed as modules.assistantVisible and hides the button/static Free preview after CRM context refresh. Direct navigation to #assistant with the assistant hidden shows a disabled message without availability or iframe. PRO checks, server permissions, and budgets remain enforced.
