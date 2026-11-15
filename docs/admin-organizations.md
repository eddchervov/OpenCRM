# Organizations in the OpenCRM desktop admin interface

**English** | [Русский](admin-organizations.ru.md)

The “Organizations” list contains organizations owned by any user, including existing organizations in the hidden Fleet scenario. Search by organization and owner, cards, statistics, member lists, and audit history are retained. Read access requires `Admin.Users.View`; changing a status also requires `Admin.Users.Manage`. System administrators follow the existing server access rules.

The card provides “Force archive” and “Restore from archive”. The action requires a reason of up to 2000 characters and a separate confirmation checkbox. Before execution, the selected organization's name, owner, and ID are shown. Deleted organizations cannot be changed; restoration is available only from the archive.

Administrative archiving works when user-facing `Organizations:ArchivingEnabled` is disabled, does not require the administrator to be a member of the organization, and does not replace the owner. The user-facing restriction remains in effect. The server performs the operation inside a transaction under the existing organization lock and records the actual administrator and reason in `organization.admin_archived` / `organization.admin_restored` events.

Archiving closes workspace access for members. The existing retention period applies: after 30 days, routine cleanup marks the organization as deleted. Before then, it can be restored with its data and current subscription terms. An archived organization continues to occupy a slot for creating an organization of its type.

API: `POST /api/v1/admin/organizations/{id}/archive` and `/restore`, body `{ "reason": "..." }`, successful response `{ "message": "..." }`. An empty or overly long reason is rejected; an invalid current status returns a conflict. If the desktop client loses the response, it does not retry the write and blocks further status changes until the card refreshes successfully.

Publishing the desktop client and publishing server APIs are separate operations. The server must contain this update for the new actions to work; copying the desktop build does not update the server.

## Web list and Free/PRO

The web admin interface adds a separate organization list, search, server-side pagination, mobile cards, and navigation to an owner's organizations. In both clients, active Detailing and Service Station organizations without a subscription show unlimited Free; archived and deleted organizations retain their actual state. Server cards return the system Free name without an artificial end date. Subscription management details are in the [subscription guide](admin-subscriptions.md).
