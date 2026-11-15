# Organizations in the OpenCRM admin interfaces

**English** | [Русский](admin-organizations.ru.md)

The web and desktop administration clients list organizations across supported and legacy scenarios. Search and organization details include the owner, status, members, usage counts, and audit information. Reading requires `Admin.Users.View`; changing organization status also requires `Admin.Users.Manage`.

Administrators can force-archive or restore an organization. The action requires a reason of up to 2000 characters and explicit confirmation. Deleted organizations cannot be changed; restoration is available only from the archive. Administrative archiving is permitted even when user-facing `Organizations:ArchivingEnabled` is disabled, does not require the administrator to be a member, and does not replace the owner. The server performs the change in a transaction and records the administrator and reason in the organization audit events.

Archiving closes workspace access for members. Routine cleanup marks an archived organization as deleted after 30 days. Until cleanup, it can be restored with its data. An archived organization continues to occupy an organization-creation slot of its type.

The desktop API is `POST /api/v1/admin/organizations/{id}/archive` and `/restore`, with body `{ "reason": "..." }`. Empty or overlong reasons are rejected; invalid state returns a conflict. If the desktop client loses the response, it does not retry the write and blocks further status changes until the card refreshes successfully. The server API must be updated separately from the desktop client.

## Optional commercial information

`PlatformPolicy:Commercial:Enabled` defaults to `false`. When disabled, billing and subscription information is omitted from the administration interface and organization records. When enabled, active Detailing and Service Station organizations without a paid subscription use unlimited Free access; archived and deleted organizations keep their actual status. Commercial controls also require the relevant billing permission. See [subscription management](admin-subscriptions.md).