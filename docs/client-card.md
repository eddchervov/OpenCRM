# Customer Profile

**English** | [Русский](client-card.ru.md)

## Comment

The customer profile and its form use one “Comment” field, limited to 4,000 characters. If the limit is exceeded, the server returns HTTP 400 with a field-level message; the rejected change is not saved. The legacy API parameter `Notes` is accepted for compatibility and merged into `Comment`. Search and export use the unified comment.

When the previous `Comment` and `Notes` fields are migrated, both texts are preserved with a separator if they differ. Identical values are not duplicated. Splitting a merged value back into the original fields is ambiguous, so a schema rollback must stop if merged data has already been written.

The comment can be edited separately through `PUT /api/v1/organizations/{organizationId}/clients/{clientId}/comment`. The operation checks the `ClientsEdit` permission and changes only the comment and update timestamp. An error preserves the entered draft; canceling does not change the data.

## Service history

History is loaded page by page through `GET /api/v1/organizations/{organizationId}/clients/{clientId}/maintenances`. Parameters are `page`, `pageSize`, `searchQuery`, and `status` (`upcoming`, `completed`, or no filter). Page size is limited to 1–100. Search and filtering are applied before counting and selecting records; ordering by date, creation time, and identifier is stable.

History is associated with the customer through `BuyerClientId`, including when a vehicle changes owner. Deleted work orders and vehicles are excluded; archived vehicles remain visible. Viewing requires permission to read work orders, and financial values are returned only when the user has financial access. Payments are loaded for the current page; the profile total is calculated by a database aggregate.

## Identity information

For an individual, surname, given name, and patronymic are stored separately. Each part is limited to 60 characters and the full name to 160. A given name is required when creating or updating an individual's details; surname and patronymic are optional. An organization or sole proprietor has one name field of up to 160 characters.

The fields are nullable and available in both customer list responses and organization exports. Existing `Name` values are not parsed automatically; the legacy API with a single `Name` field remains supported.

## Tax details

The organization type determines the tax detail format. A tax identification number (INN) contains 10 digits for a legal entity or 12 for a sole proprietor. A registration reason code (KPP) is available only for a legal entity and contains 9 characters; Latin letters are allowed in positions 5–6. Values may be pasted with spaces; leading zeroes are preserved. Format validation does not verify taxpayer registration.

For legacy API requests and records without a known organization type, the previous tax details field remains supported with a 20-character limit. Strict validation applies after an organization type is selected.
