# Subscription management in the OpenCRM admin interfaces

**English** | [Русский](admin-subscriptions.ru.md)

This is an optional module controlled by `PlatformPolicy:Commercial:Enabled`, which defaults to `false`. When disabled, commercial administration sections are hidden and a subscription is not required to use the CRM. The installation owner enables the module and configures prices, offers, and trial terms.

## Organizations and access

A PRO subscription belongs to an organization. Detailing and Service Station organizations without an active subscription continue basic operation on Free with no expiration. Personal vehicles are available to every account; personal plans and subscriptions from the previous model remain for compatibility.

The web admin interface has a separate “Organizations” list, including Free organizations without paid subscription records. Access requires `Admin.Users.View`. The server searches by name/TIN and owner/member; each page contains up to 25 organizations. Cards are used on narrow screens. “Organizations and subscriptions” opens the corresponding owner tab. Desktop uses the existing organization list. Archived, deleted, and inactive organizations are not shown as continuing basic operation on Free.

The web admin “Payments and subscriptions” section opens on organization subscriptions. Free does not create a synthetic subscription record and is excluded from paid-subscription statistics. Cards show status, term, source, and history; search and filters by status, plan, and source remain available.

## Administrative actions

An administrator with `Admin.Billing.Manage` can extend a subscription by 1–366 days, grant a regular paid plan without payment, or end active subscriptions. A reason is required; extension and cancellation validate the term shown when the form was opened. The audit log stores the action, author, reason, and dates. A manual grant is labeled PRO for Detailing and Service Station.

Regular demo grants for Detailing and Service Station are hidden in both clients and forbidden by the server. Organizational promo codes with bonus days are used for PRO trials. The code, term, plan, scenarios, and limits are configured in the editor; `30days` is not a system requirement. Revoking a previously issued demo remains supported. When PRO expires or the last demo is revoked, the organization continues basic operation on Free and its history is preserved. Unused paid time is not restored automatically.

Previous personal subscriptions and the hidden Fleet scenario retain their rules. Granting and revoking the old demo remains available for them. Revoking a demo ends only the demo subscription; stopping a subscription ends all active subscriptions for the selected target. These actions do not rewrite payments.

## Plans and limits

System Free is identified by the `workspace.free.v1` group. It is shown as free and unlimited in duration and is not offered for purchase, promotional grant, disabling, or deletion. Its system group, price, and availability flags are protected. Technical `DurationDays` does not set a Free access period.

Free quotas and capabilities are editable. The `workspace.clients.limit` and `workspace.vehicles.limit` assignments cannot be removed, but their numeric values can be changed. The vehicle limit is synchronized with the plan form; after saving capabilities, the editor reloads them. The web editor stays open after saving a plan and ignores a late capability response for a previously selected plan. Capabilities are not saved until loading succeeds.

PRO options set the actual price, term, and capabilities. Previous base, demo, and personal plans are explicitly identified; their data and paid terms are preserved. Free is excluded from organizational promo-code plan selection.

## Existing records

Disabling commercial mode hides its administration controls and omits commercial details from current admin responses. It does not delete historical plans, subscriptions, payments, or audit records. Legacy sources such as `paid`, `demo`, `manual`, and `unknown` remain as stored; records are not reclassified automatically.