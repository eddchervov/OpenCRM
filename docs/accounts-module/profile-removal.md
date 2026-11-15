# Removing account scenarios and capabilities

**English** | [Русский](profile-removal.ru.md)

## Current model

An account has no mode, organization type, recommendations, or enabled capabilities. Accounts owns registration, sign-in, user deletion, and personal records. Personal vehicles are available to every user. Access to an organization is determined by membership, permissions, and the subscription of the selected organization.

Organizations owns `OrganizationTypeDefinitions` and `IOrganizationTypeCatalog`. Supported codes are fixed: `detailing`, `service-station`, and `fleet`. A database constraint ties `IsSupported` to these three codes. Creation and mailing-filter options use only `IsSupported && IsActive`; reading names of existing organizations includes historical records. An archived type record does not create a new workflow. `OrganizationScenarioProcessCatalog` retains descriptions of historical processes and uses shared `OrganizationTypeCodes`.

Migration `20261015071909_RemoveAccountProfiles` moves all names, descriptions, activity flags, order, IDs, and dates from the old catalog. A standard record is created for any supported code that is missing. Existing organization codes and saved campaign values are not rewritten. A code without a directory record is displayed as the original code.

An activity value from old migrations must not be substituted for the current value. The migration preserves old names and settings, but historical types do not become available for new creation.

The following were removed: API routes `/api/v1/account-profile` and `/api/v1/admin/account-profiles`, profile services and DTOs, mode fields in registration and sign-in requests, profile fields in the admin interface, and five tables: `UserAccountFeatures`, `UserAccountProfiles`, `AccountScenarioFeatures`, `AccountFeatureDefinitions`, and `AccountScenarioDefinitions`. The “Workspaces” page remains on its previous MVC route `Personal/AccountProfile` but no longer stores a profile; empty POST handlers for mode selection and profile saving were removed.

## Before release

Prepare the selected server revision for release using the shared deployment instructions:

1. Check server and external integration logs for calls to the APIs being removed. No consumers were found in the source of the four Vue clients. Compare the log period with actual usage; absence of external client source does not prove there are no calls.
2. Save a consistent full `pg_dump` of the production database before applying the migration. Check readability and perform a trial restore to a separate database. The backup contains personal data; keep it outside the repository and public directories under the established process.
3. Run and save the results of the queries below, and export all five tables being removed alongside the backup. Check unknown codes, non-standard names/activity/order, and the presence of the three supported rows. Do not correct codes automatically or move the historical profile type into an organization.
4. Apply the migration to a restored copy and check that names/codes and working scenarios are preserved. Only then include the migration in a release.

```sql
SELECT "Code", "Name", "Description", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt"
FROM "AccountScenarioDefinitions" ORDER BY "SortOrder", "Code";

SELECT "PrimaryScenarioCode", COUNT(*) FROM "Organizations"
GROUP BY "PrimaryScenarioCode" ORDER BY "PrimaryScenarioCode";

SELECT "PrimaryScenarioCode", COUNT(*) FROM "Organizations"
WHERE "PrimaryScenarioCode" NOT IN ('detailing', 'service-station', 'fleet')
GROUP BY "PrimaryScenarioCode";

SELECT "AccountMode", "PrimaryScenarioCode", COUNT(*) FROM "UserAccountProfiles"
GROUP BY "AccountMode", "PrimaryScenarioCode";

SELECT "FeatureCode", COUNT(*) FROM "UserAccountFeatures" GROUP BY "FeatureCode";

SELECT scenario."Code", feature."Code", link."SortOrder"
FROM "AccountScenarioFeatures" link
JOIN "AccountScenarioDefinitions" scenario ON scenario."Id" = link."AccountScenarioDefinitionId"
JOIN "AccountFeatureDefinitions" feature ON feature."Id" = link."AccountFeatureDefinitionId"
ORDER BY scenario."Code", link."SortOrder", feature."Code";

SELECT "TargetOrganizationTypeCode", COUNT(*) FROM "EmailCampaigns"
GROUP BY "TargetOrganizationTypeCode";
```

## Rollback

`Down` recreates the schema of the five tables and migrates all organization type names back, with IDs and settings. It does **not** restore profiles, features, or relationships; a backup taken before `Up` is required for those.

When rolling back, stop application writes and first test the sequence on a separate copy. After `Down`, the scenario tables already contain the migrated entries: before restoring the five tables from the consistent backup, clear these entries together, then restore the data and sequence values. Load data in this order: feature/scenario definitions, profiles, scenario links, and user capabilities. Do not selectively reload `AspNetUsers`, organizations, or other production tables. Compare counts, IDs, and relationships with the backup. Return to the old application version only after checking the compatible schema. Full database restore is a separate decision because it loses changes made since the backup; do not perform it automatically.

The local integration test uses a separate PostgreSQL instance and checks `Up`, `Down`, restoring all five tables from consistent SQL copies, and a repeated `Up`. It does not replace validation of the production database backup and server consumers.
