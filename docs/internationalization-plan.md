# OpenCRM Multilingualism and Regional Settings Plan

**English** | [Русский](internationalization-plan.ru.md) | [简体中文](internationalization-plan.zh-CN.md)

> This is a machine translation of the Russian implementation plan. The branch named in the plan has since been merged into `main`.

The order of release of languages: Russian, English, then simplified Chinese for mainland China. The framework should allow the following languages to be added through dictionaries and language registration. Regional rules are connected separately: the employee’s language does not define the currency of the organization or the country of registration of the car.

Status: implementation in the branch `codex/internationalization-plan`. The basis of localization RU/EN/zh-Hans, separate language and format settings, currency settings and currency images, world time zones, country of registration of the car and free format of foreign numbers are ready. Translated web interfaces and desktop admin. The full international edition still requires verification of letters, printed documents and acceptance scripts below.

For the first currency implementation, RUB, USD, EUR and CNY are limited: all have two fractional digits. Currencies with other precision are not yet offered. Currency changes are allowed only before the associated data is available; there is no conversion of amounts. Subscription and payment integration remain in RUB.

Currency and time zone are selected when creating an organization, before filling out the catalog and schedule. The belt can be selected from hints or typed as an IANA ID; it is not derived from language or currency. New catalog system positions inherit the currency of the organization and do not receive fictitious prices. Historic orders in another currency remain readable with their currency code; finance editing, payment and warehousing operations are blocked. Moving a personal car to an organization doesn’t count old orders.

General elements and navigation, entry forms, organization creation, settings, catalog, employees, working day and master of initial setup, customers, equipment, basic order forms, payment and schedule are translated. Business, preview and communication dates use the selected regional format separately from the language. This list describes the finished areas, not the completion of the entire translation.

In addition, warehouse screens of three scenarios, execution and log of works, the general shell of sections, calendars with accessibility tips, support and personal password forms are translated. Older forms of the number of equipment received the choice of the country and retain the foreign spelling without a Russian mask. Individual browser bundles are checked without Node `process`The script of the Chinese number and the return to the Russian mask are checked in the browser.

The list, card, notes, history of system events and reminders on appeals, fault interfaces of the car service and fleet, export of JSON/1C and ZIP organizations, visualization of posts with 3D control, tariffs and promotional codes, a matrix of roles and all built-in permits are translated. Rights search uses translated signatures without changing the access codes. Calendar service date and event time in the history of malfunctions are formatted separately. Older Razor car creation/editing forms and personal list also use server resources and JavaScript signatures from the form.

Shift and load interfaces, auto service and fleet counters, import, review and detailed PRO capabilities, contextual help, correspondence and templates, notifications and repeated visits, statistics and knowledge base are completed. The reminder of the visit transmits the status code and time zone: the translation does not understand the Russian text of the answer. Historical amounts in the card are displayed in the order currency, including the transfer of the RUB order to the CNY organization. The main screens of user administration and payments, editors of tariffs, features and promotional codes, old car cards, photos, albums and a magazine, pages of organization selection, invitations, errors and payment result are translated. The built-in assistant interface and its JavaScript use server resources; the AI budget saves RUB regardless of the CRM currency.

The rest of the web administration service screens and old Razor service forms have been translated. Calendar "today" and latency in old forms of organization take into account its time zone; calendar dates are not shifted as time stamps. The country of registration goes through both types of import, preview, double check and preservation. For 109 additional system import errors, server transfers were added without changing HTTP statuses and codes. The Avalonia desktop client and the Windows launcher’s own messages use RU/EN/zh-Hans resources.

In the desktop admin, the language changes without restarting: open cards, filters, confirmations, calendar, diagrams and system signatures are updated. Selected codes, drafts and user names are saved. The calendar signature language is separated from the date format; the service date is not shifted as a time stamp. Server names of embedded statuses, roles, and scripts are translated by code, unknown values are stored. Administrative results of the organization are divided by currency, service amounts show a currency snapshot, and commercial payments keep RUB.

System names of embedded directory positions, dynamic server messages, background letters, and printed documents require separate verification; user titles, template content, AI responses, and old history are not automatically translated. For payment errors, the first contract with the key and string parameters was added: the direct API saves HTTP 400 and `bad_request`and other users retain the original text of the exclusion.

Verified: server and client assembly, form and dictionary tests, application of new migrations to the temporary PostgreSQL, search for a Chinese number and change the language of the telephone field while saving the number and country in the browser. Recent checks: 962 client tests, 129 import server tests, 40 server localization tests, 71 desktop admin API tests and 319 tests of its models and windows. Eight targeted server tests tested administrative results with different currencies and currency snapshots. The real database hasn't been updated. Unfinished areas: full translation of dynamic server errors, background notifications and printed documents, as well as the remaining acceptance scenarios below. Changing the belt after a schedule appears is prohibited; APIs are protected from outdated preservation, and creating and modifying records check for missed and repeated time when the clock changes.

## Addition of the following language

1. Add the code and language name to `uiLanguages`culture in `localeCultures` and general vocabulary in `src/OpenCRM.Frontend/packages/ui/src/i18n.ts`. Language switch uses this registry automatically, type `UiLocale` It's coming out of it.
2. Add translation to all thematic files `*Messages.ts` into `packages/workspace/src` and the dictionaries of the other applications. Use stable keys and parameters of the whole phrase; do not translate customer names, user service names and old history texts.
3. Add a description to `SupportedUiLanguages.Languages`: code, proper name, culture, default format and valid pseudonyms. Server switches are built from this registry. Add `LocalizationStrings.<culture>.resx` and `IdentityErrors.<culture>.resx` in the authentication module. Add an independent regional format if necessary; the new language does not require a new currency or number mask.
4. Complete the Library Calendar Contract in `calendarLocale.ts` and telephone field adapter `UiPhoneInput.vue`. His translation table is checked by type `UiLocale`Language change updates library signatures without changing the number and country of choice. Launch. `node --test tests/translationCoverage.test.mjs` from frontend, dictionary server tests and application builds. The checks themselves take the list of languages from the registers, detect missing keys and parameters, and the client check also checks for the existence of explicit keys in the interfaces and APIs. Dictionaries for old JavaScript are transmitted from Razor over the keyspace via JSON, so adding a key does not require a second list of translations in the script. Verification of the server English dictionary separately detects Russian and Chinese meanings, except for their own language names.
5. For desktop admin add language, own name and HTTP tag to `AdminUiLanguage.Supported`. In the catalogue `src/OpenCRM.Frontend/Admin` translate `OpenCRM.Admin.App/Localization/Strings.<language>.resx`, `OpenCRM.Admin.Api/Strings.<language>.resx` and `OpenCRM.Admin.Launcher/Resources/Strings.<language>.resx`. Use the observed `AdminLocalization` for AXAML signatures; partition identifiers and rights codes remain unchanged. The API client transmits the selected language to `Accept-Language` and `X-OpenCRM-UI-Language`The latter has priority for a single API query and does not change the profile preferences or regional format. Check it out `NativeResourceCoverageTests` Switch the open form to Avalonia Headless.
6. Check before and after switching, letters, documents, long signatures, and IME input. Culture server names and client language codes should be harmonized in the context of localization.

## Customization boundaries

| Level. | Suggested settings | Appointment |
| --- | --- | --- |
| User | Interface language, regional format of dates and numbers | Personal display in all applications |
| Organization | Country of work, main currency, time zone | Financial accounting, details and working schedule |
| Personal Accounting Outside the Organization | Currency of personal accounting, belt for personal events | Cash and service without `OrganizationId` |
| A car | Country of registration, source number, search key | Checking and searching the room regardless of the language of the employee |
| Document or financial transaction | Currency; if necessary, language and details at the time of issue | Preserving the Meaning of Historical Data |
| Recipient of the notification | Preferred language | Emails and background messages outside of an HTTP request |

For example, an employee uses the English interface and the British date format, the organization works in RUB on Novosibirsk time, and the car is registered in another country. None of these settings automatically changes the others.

## Change Points in Current Code

| The Domain | Point of reference | What is required |
| --- | --- | --- |
| Common Vue components | `src/OpenCRM.Frontend/packages/ui`, `packages/workspace` | General translations and formatting for scripts and admins |
| Client applications | `src/OpenCRM.Frontend/Scenarios`, `Admin/admin-client` | Unified language connection, translation of menus, forms and errors |
| Server pages | `src/OpenCRM.WebApp/Views`, `Extensions/ServiceCollectionExtensions.cs` | Razor Localization and Validation, Dynamic HTML `lang` |
| HTTP conveyor | `src/OpenCRM.Hosting/OpenCRMApplication.cs` | Defining the culture before handling user errors and pages |
| Mistakes | `Infrastructure/Api` WebApp, `OpenCRMIdentityErrorDescriber.cs` Infrastructure.Auth | Translation to stable codes, preservation of HTTP statuses |
| Notifications | `MaintenanceNotificationContent.cs` Modules.Maintenance, Communications module and notification service | Recipient’s language and organization context in background tasks |
| Organizational settings | `src/OpenCRM.Domain/Organization.cs`, `OrganizationTimeZoneService.cs` Modules.Organizations | Use the existing belt, add currency and country |
| Vehicle plates | `VehicleRegistrationPlatePolicy.cs` in Modules.Vehicles, `vehicleRegistrationPlate.ts` in packages/workspace | Country format, Unicode, search and compatibility of old numbers |
| Payments | `src/OpenCRM.Infrastructure.Payments` | Separate CRM accounting currency from subscription currency and provider restrictions |

V. `Organization` used independently `ShiftTimeZoneId` and `CurrencyCode`Currency transactions retain their own currency code. The choice of belts supports the world IANA ID. Explicit RUB is retained by the commercial subscription and AI budget. `UiPhoneInput` Translates the library interface and country names, saving the number and selected country when switching the language. Calendar and manual date entry use a separate regional format.

The user `LastAccessLanguage` contains a browser header to account for the visit; it cannot be used as an explicitly selected language. The API already has common error codes, but subject messages are often transmitted in ready-made Russian text.

## Languages and dictionaries

Common client module `@opencrm/ui/i18n` uses Vue I18n; server pages and validation use ASP.NET Core localization through `.resx`. Desktop admin uses separate `.resx` and observable bindings. Resources correspond to different execution environments; the server transfers dictionaries to old JavaScript through JSON without manually duplicating keys in scripts.

The registry of languages specifies the code, proper name, default format, text direction and translation readiness. Initial set: `ru`, `en`, `zh-Hans`. Regional formats are defined separately, for example `ru-RU`, `en-GB`, `zh-CN`. Browser headers with `zh-CN` must be allowed in supported simplified Chinese; `zh-Hant` It is not declared a ready-made translation.

Dictionaries are divided into subject areas: general actions, accounts, customers, cars, schedules, jobs, warehouse, finance, administration. Keys describe meaning, for example. `clients.actions.create`. Sentences are translated in their entirety with named parameters; the forms of quantity are determined by the rules of the language. The user’s texts and the names of its entities are saved without translation.

Guest language selection: explicit choice in cookies, then supported browser language, then English. For the logged-in user, the saved explicit preference takes priority; changing the language updates the profile and cookies. For existing users, the Russian language is retained during the transition. The unsupported language is given to a known version or a reserve language according to the explicit rules of the registry.

The server transmits the permitted language to client applications before the first display. When transitioning between Razor, Vue and adminka, the language is retained. In the first step, changing the language can reload the current page: this makes it easier to reconcile server texts and downloaded data. Unsaved form requires standard protection against loss of input. All Vue instances, including the built-in fields on Razor pages, receive the same settings.

The reserve language after EN is ready is English. During the transfer of individual sections, allow Russian backup text, but the section is not considered translated. The assembly checks the completeness of the declared ready-made dictionaries, parameters and forms of quantity. The lack of translation should not show the user the internal key.

API errors receive stable subject code and parameters. The client does not compare the text of the message. The transition saves existing response fields and statuses; the server provides a localized message to other API users. Technical exceptions do not translate directly into user translation.

The history of the organization now contains ready `Title` and `Description` into `OrganizationAuditEvent`. New system events must maintain a stable type and structured parameters for display in the reader’s language. Old ready-made lines and user descriptions are saved without automatic translation; this is a clear exception to the full coverage of EN and Chinese. The monetary values and time in the event parameters retain the currency and context of the original transaction.

Letters and notifications are based on the language of the recipient, not the culture of the current stream. For an external client without a profile, their explicit preference or the language of the organization’s communications is used. User templates are not overwritten when changing the language. For repeat shipments, the selected language or finished content is recorded according to the existing delivery mechanism.

Full coverage includes public pages, login, registration, restoration of access, personal account, all CRM scripts, web and desktop admins, assistant with available integration, errors, tips, schedules, exports, printed forms, letters and notifications. Working components and built-in library dialogs are also included in the audit. The signature “OpenCRM-based” and its reference are retained in accordance with the current project rules; translation of the mandatory signature requires separate agreement of the terms of the project.

## Currency of organization

The first version supports one accounting currency per organization. Proposed field - `CurrencyCode` with a check on the supported directory of currency codes. The symbol is used only for display. The user region defines the separators and the location of the symbol, and the currency of the organization determines the unit of the sum.

Personal equipment and services can exist without an organization; `MaintenanceFinancialTotal` hollow `OrganizationId`. This area requires a separate personal accounting currency with the same shifting restrictions once the money data is available. The document retains its currency explicitly; it cannot be taken from the language or current open organization. Moving a car between your personal area and your organization does not change the currency of your historical documents.

Monetary documents and transactions retain their currency code; the lines of the document inherit it from the document if the model does not allow for currency mixing. The server checks the compliance of prices, warehouse cost, payments, returns and debts with the currency of the organization. Reports for several organizations group amounts by currency. The sums of different currencies do not add up to a single total without a separate conversion model.

Storage remains accurate decimal or in minimum units according to the current model. The number of fractional pieces of currency is taken into account in the payment and totals; the internal accuracy of the unit price and cost may be higher. The rounding rule is fixed for rows, taxes, totals, returns and exchanges with the provider. Replacing all the money fields with a new universal type is not a necessary part of the first phase.

Currency change is allowed as long as there are no dependent monetary data, including prices, cost balances and initial debts. Verification is performed by the server in a coordinated transaction with a change of setting. Once the data is available, the normal shift is blocked. Conversion of an existing organization, exchange rates and settlements in several currencies are a separate stage; replacing the RUB signature with USD is unacceptable.

The OpenCRM subscription fee retains the provider’s own currency and checks. The CRM currency does not change it automatically. Exports and imports transmit the currency code explicitly; a mismatch with an entity requires an error or a separate conversion scenario.

## Time and calendar

An existing IANA-identified organization belt is used, for example. `Asia/Shanghai`. New field duplicated `ShiftTimeZoneId`It is not created unnecessarily; the renaming of an existing field is evaluated together with the compatibility of contracts. The list of choices is expanded by world search zones. The UTC offset is calculated for the corresponding date, rather than being stored as a constant number.

Specific events are stored as moments of time in UTC; working hours and repetitions are set by local time and zone. Timeless dates retain the meaning of the calendar date. API formats remain invariant: ISO and JSON numbers are independent of the interface language.

Recording, shift and reporting day are all part of the organization’s belt. The time zone of the device does not shift the working schedule. Day/week limits, notifications, filters and financial statements are calculated in the same context. For ambiguous or absent local time in the seasonal clock transfer, an explicit check and clear choice or error are needed.

The first version blocks the usual belt change in future recordings and shifts until a separate consequence view is implemented. Completed events preserve the original points of time; documents and history must allow the original context of the zone to be recreated. The subsequent belt change offers a choice between saving UTC moment and saving local time for future events, with conflict checking. The current belt change capability needs to be aligned with this restriction at all API points.

## Countries and registration numbers

The country of the organization defines the initial values of new shapes, but does not define the country of each car. A separate country of registration is added to the machine; an unknown country is permissible, including for personal equipment outside the organization. A clearly unknown country appears as a code. `ZZ`It uses a free Unicode number and is different from the previous one. `null`. Old records and APIs with `null` or keep Russian rules empty; changing the language does not override this compatibility. The new forms show RU as the initial choice, and the transition to "Country Unknown" reports ZZ. Editing the remaining fields of the old machine does not replace it. `null` It's automatic on RU.

For the former Russian numbers, the existing server policy is maintained. Foreign numbers and obviously unknown country ZZ use common `InternationalRegistrationPlateRules`Free Unicode representation of up to 24 characters, removing extreme spaces and checking for inadmissible control characters. The Russian replacement of similar letters does not apply to them. Rules are used by conventional forms, quick entry and import; country verification is performed prior to the creation of a related client.

You need to separate the original representation of the number and the normalized search key. Russian comparison of similar letters applies only where it is provided for by the rule of the country. Hieroglyphs and foreign letters are not lost. The mask is a hint; for special, temporary and unknown formats, free input is provided with safe length and character limits.

The search and identification of duplicates takes into account the organization and country, and, if necessary, the region of registration and the category of the number. Coincidence of the line of an unknown country does not lead to automatic combination of cars. Quick recording, import, API, assistant, car editor and personal appliances all share the same server policy. The cache formats on the client are divided by country and version of the rules.

Country rules are added with datasets and tests without branching across the country inside screens. For CN, prior to rigorous verification, published sources by number category and specialist verification are required; universal input allows you to work until a full set of masks is ready.

A separate audit covers telephones, names, addresses, indices, tax and bank details, mileage and volume units, directories, document templates and integration. Language support does not mean automatic support for local taxation and payment systems. Chinese requisites should not be placed in the TIN and checkpoint fields. New units of measurement require an explicit unit for the value and the rules for recalculating history.

## Preparation of the Chinese interface

The target option is `zh-Hans` for mainland China. The dictionary of terms records the client’s translations, appeals, records, order-outfit, works, warehouse and payment before mass transfer. EN also goes through word editing so as not to transfer ambiguities to the next language.

Chinese is already tested on the pilot screens of the first stage: entrance, client, car and order-outfit. Transfers, button width, string height, search and input via IME are checked. Keyboard handlers should not keep a shape or change the search until the composite character is completed. Names do not require a Latin or space between parts.

Interface fonts and PDF generators must contain Chinese characters. Their availability and licenses are checked for self-installation; no mandatory download from an external CDN is introduced. Printed forms are checked visually, including table transfers and text highlighting.

## Transition of existing data

1. Make a list of money entities, time fields, error codes, directories, and user templates. Separate current restrictions and target rules.
2. Add compatible fields and read old data. Prepare background assignments, DTOs, administrative clients, import, export and restore backups.
3. Existing monetary data should be assigned RUB only after checking that they really relate to current ruble accounting. The numerical sums remain unchanged. Do not define the currency by the language of the browser.
4. Save your time zones. For the unspecified, do not assign a zone by server location or reinterpret historical dates. Before working the schedule, require an explicit adjustment where the belt is needed.
5. Do not assign all old cars RU by default. Save numbers, highlight confirmed formats, unknown countries and key collisions; perform mass correction with a separate verifiable operation.
6. Recalculate search keys without deleting originals; check link retention, history, and repeat entries. For the transition period, determine a compatible search for the old and new view.
7. Enable new settings only after an agreed update of all recording components. Older versions should not create amounts without currency or apply the previous normalization to foreign numbers.

Each migration must match the EF Core version and neighboring project migrations: the main file, attributes, Designer and snapshot are checked together. Trial transition and re-start are checked on an impersonal base. Deleting new fields after foreign numbers or amounts appear in other currencies is not considered a safe rollback; you need a data retention plan and a compatible version of the application.

## Sequence of implementation

| Phase | The result | Completion condition |
| --- | --- | --- |
| 1. Contracts and inventory | Language, currency and country registers; list of consumers and tables | Described formats, default values and transition of old data |
| 2. Basis of localization | Preferences, cookies, server culture, shared client package | Entrance and transition Razor → Vue → adminka retain language; Chinese pilot tested |
| 3. Common fields and EN | Calendar, phone, money, dialogue, navigation, then CRM sections | All agreed surfaces are available on EN without Russian passes, except for the mandatory signature. |
| 4. Regional data | Currency of organization and documents, world zones, country of the car | History retains meaning; settings are verified by the server; import and export agreed |
| 5. Complete Chinese. | Dictionaries, fonts, letters, printed forms and editing | Basic scenarios tested with IME and native speaker |
| 6. The next language or country | Instruction and set of resources | New language added without changing business logic; new country without rewriting screens |

Regional changes can be done in parallel with the transfer after stage 1, but the inclusion of new currencies depends on the readiness of the entire financial chain. Translated pages are produced by matched scripts rather than a random set of buttons. Existing RUs are checked at each stage.

## Verification of the result

- Switching the language by the guest, entry, exit, new browser, outdated session, personal account and adminka. 401/403 rights and responses are retained; unconnected integrations are not required.
- All three CRM scripts, built-in Razor Vue fields, library calendars, availability signatures, update notification, confirmations and download errors.
- Check the full set of keys, parameters and forms of quantity. Separate verification of the absence of user strings outside resources with exceptions for data, tests and mandatory authorship.
- Two employees with different languages work in the same organization without changing the data; the cache does not give a translation of another user.
- New system events are displayed in the language of the reader; old history records retain the source code. Personal money documents without an organization have a clear currency and do not change when moving the car.
- RUB, USD, EUR and CNY: input, accuracy, rounding, payment, partial return, debt, warehouse, printing and export. Different currencies are not added together. Currency checks without a fractional part and with three categories will be required before expanding the current list.
- Currency change with an empty organization and failure after the creation of monetary data, including the competitive creation of a transaction. Payment integration does not change the subscription currency.
- `Asia/Shanghai`The existing Russian belt, UTC and the seasonal clock zone: midnight, week boundary, repeating shift, non-existent time, notifications and reports. Changing the belt of the device does not change the record.
- Russian and Chinese number, unknown country and free format: preservation, search, re-recording, import, editing and no erroneous association. Checking Cyrillic, Latin, hieroglyphics and collisions after normalization.
- RU, EN and Chinese on a narrow screen, in light and dark themes, with keyboard and IME; PDF contains readable and highlighted Chinese text.
- Migration, preservation of original amounts and numbers, backup, reading old data and recovery. Fixtures contain only fictitious information.

## Decisions before the implementation of the relevant stage

Regional formats are available for implementation `ru-RU`, `en-US`, `en-GB` and `zh-CN`Selected separately from the interface language. Supported currencies of current implementation are listed above. It remains to determine the language of the organization’s documents by default, the composition of mandatory Chinese number formats and the list of countries with strict verification of details. These questions do not preclude the start of inventory and the basis of localization.

Multi-currency settlements, exchange rates, conversion of an existing entity to another currency, compliance with tax rules of other countries, local payment providers and transfer of user data are not included in the first issue.

## Documentation of the mechanisms used

- [Vue I18n Composition API](https://vue-i18n.intlify.dev/guide/advanced/composition).
- [Localization of ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization?view=aspnetcore-10.0).
- [Selection of ASP.NET Core query language](https://learn.microsoft.com/aspnet/core/fundamentals/localization/select-language-culture).
