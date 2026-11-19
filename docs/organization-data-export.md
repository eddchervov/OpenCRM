# Exporting organization data as ZIP

**English** | [Русский](organization-data-export.ru.md) | [简体中文](organization-data-export.zh-CN.md)

The current organization owner can export data regardless of Free/PRO. Authorization is checked when requested, before building, before publishing, and on every download.

## Preparing and downloading

`POST /api/v1/organizations/{organizationId}/data-export` with anti-CSRF protection queues a persisted job. A repeated request returns the same active job or a ready archive within the 24-hour period. `GET .../data-export/status` returns the state; `GET .../data-export/{jobId}/file` streams a ready ZIP. The legacy `GET .../data-export` downloads only an already-ready archive; it does not start a build.

The Organizations module owns the queue; WebApp runs the background worker. States are `queued` → `running` → `ready`; a failure results in `failed`, and deletion of a ready file results in `expired`. A unique partial index prevents two queued/running jobs for one organization. A short shared PostgreSQL transaction lock limits the overall queue size; a separate advisory slot permits one build across all application instances using that database. The slot is held until the archive upload to storage finishes.

ZIP format version 1 and the explicit field list are preserved. EF reads rows sequentially through a context without retry buffering, tracking, or AutoInclude. Metadata is collected under RepeatableRead; the transaction ends before photos and documents are accessed. File lists and missing attachments are stored in temporary JSONL files. Attachments are copied one at a time, without recompressing already-compressed media. The ZIP is written to temporary disk, not a MemoryStream. Storage receives the stream without an additional SHA-256 pass.

The client polls the active job state and asks the browser to download through a protected URL; it does not create a Blob for the entire file. Preparation and download errors are shown in settings. Closing the section stops polling but does not delete the server job; returning reloads its state. Automatic download after preparation occurs only if the section remains open after the user click.

The daily limit starts after successful completion, not when requested. The legacy `LastDataExportRequestedAt` is not used because it may contain failed attempts from the previous mechanism. A failed job permits another request after a short delay. Downloading a ready archive again does not rebuild the ZIP. If a ready object is missing, the job becomes failed and the daily limit is released. Publication results are committed with CAS and audit in one transaction: if the commit result is unknown, an already-ready archive cannot be revoked.

Queued jobs survive a restart; a previous `running` job is marked failed once its slot is released, without an automatic retry. The user can request it again. Normal failures do not hide history or modify organization business data.

## Resource limits

Settings under `OrganizationDataExport` (for the WebApp environment, prefix `OPENCRM_OrganizationDataExport__`):

| Parameter | Default |
| --- | --- |
| MaxArchiveBytes | 1 GiB completed ZIP |
| MaxSourceBytes | 4 GiB of JSON and attachments actually read |
| MaxRows | 1,000,000 rows total |
| MaxFiles | 20,000 attachments |
| MaxTextCharacters | 2,000,000 characters in a text field |
| MaxJsonFieldBytes | 8 MiB in maintenance-history JSONB |
| TimeoutSeconds | 900 seconds for preparation and upload |
| MinimumFreeDiskBytes | 1 GiB free; checked before building and every 16 MiB of ZIP output |
| QueueLimit | 20 queued/running jobs |
| RetryDelaySeconds | 60 seconds after a failure |
| RetentionHours | 24 hours for a ready file; 24–168 allowed |
| SpoolDirectory | separate `opencrm-exports` directory inside system temp |

Two shared PostgreSQL slots limit simultaneous downloads. The stream releases a slot on Dispose, including after an interrupted HTTP connection. Limits use actual byte counts; an unknown Content-Length does not bypass them. If a limit is exceeded, no partial ZIP is delivered: the job receives a clear failure and temporary files are deleted. Increase settings only after checking disk and runtime on the target server.

This limits the size of one operation; it does not guarantee zero load. EF materializes one row; before that, server validation rejects overly long exported strings and JSONB. PostgreSQL still processes the JSONB value itself during validation. Full load on the database, S3, and network must be measured separately at production scale; a local 32 MiB test confirms streaming copy but does not replace that measurement.

When idle, the worker performs only a lightweight database check every 15 seconds without creating an S3 client; while working, it pauses for 3 seconds. Ready/failed objects are cleaned in batches of 5; metadata older than 30 days without a file is cleaned in batches of 100. Old spool files with the service's own prefix are removed after twice the timeout interval; unrelated files and reparse points are not deleted. Audit data is retained.

## Private storage and deployment

Completed ZIPs require a **separate** `S3:PrivateBucketName` (`OPENCRM_S3__PrivateBucketName`). An empty name or one matching the regular bucket is rejected before a job is queued. Regular media continues to use the existing bucket. Archive keys and S3 URLs are never returned to the client; downloads go through the API, which rechecks ownership.

Before enabling export in your installation:

1. Prepare a separate private bucket and application access for put/get/delete. Add the private-bucket setting to protected configuration and explicitly pass the variable into OpenCRM Compose; changing `.env` alone is insufficient. Deny anonymous reads and listing in the bucket policy; a random GUID and private ACL alone do not guarantee this.
2. Test an actual anonymous GET for an exact test key: access must be denied, including through an external CDN/domain. A local S3 mock does not prove the production access policy.
3. Configure a lifecycle rule to delete objects under `organization-exports/` after 8 days as a safeguard against deleted organizations and lost database metadata; do not affect business media. Normal cleanup of ready files follows `RetentionHours`.
4. Allocate a spool directory with enough free space. Increasing the archive limit requires recalculating disk reserve. Do not put database or S3 data in this directory.
5. Check migration `20261030065903_AddOrganizationDataExportJobs` against the selected release and apply it through the standard process; then check owner/member access, queued/ready states, repeated download, failure, and retry after failure.

Up creates only the job table with an organization FK and indexes. Down removes the queue and ready-ZIP metadata without changing business data or audit; SQL rollback does not delete S3 files. Before an intentional rollback, stop the worker and save the list of unfinished/ready objects for cleanup in the private bucket; do not automatically roll back the database when rolling back the application. A subsequent Up creates an empty queue.

The package also includes `20261030073145_ExpandOrganizationAuditDescription`: audit `Description` changes from varchar(1200) to text because a full role-permission change exceeded the previous length and cancelled the save. Up preserves history. Down locks the table and refuses with an error if descriptions longer than 1200 characters exist; no automatic truncation occurs. In that state, a full schema rollback is intentionally unavailable without a separate decision about retained audit records.

Normal ready-archive retention is 24 hours; the safety lifecycle rule is 8 days. Photos remain in the existing bucket.
