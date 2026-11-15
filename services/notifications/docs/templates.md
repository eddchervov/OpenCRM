# Razor system emails

**English** | [Русский](templates.ru.md)

NotificationService system emails are stored as .cshtml files in the repository. OpenCRM sends the template key, exact version, and Data through SendNotification. The service converts Data into a typed model, validates it, and renders the email. Before acknowledging queued, the rendered subject, body, and version are persisted with the notification and outbox. Delivery, deferred sending, and retries use this snapshot.

## Structure

System templates are shared between applications and intended for email. The application configuration profile determines access and the name displayed in emails.

~~~text
src/Notifications.Infrastructure/
  Templating/
    RazorSystemTemplateRenderer.cs
  Templates/OpenCRM/V1/
    _Layout.cshtml
    _ViewImports.cshtml
    Models/
      EmailConfirmationModel.cs
      PasswordResetModel.cs
      SubscriptionExpiringModel.cs
    EmailConfirmation/
      Subject.cshtml
      Body.cshtml
    PasswordReset/
      Subject.cshtml
      Body.cshtml
    SubscriptionExpiring/
      Subject.cshtml
      Body.cshtml
~~~

Subject.cshtml renders the subject without a layout. Body.cshtml uses the shared _Layout.cshtml. Models declare required fields and length limits. Razor supports standard @model, @if, @foreach, and partial views; current emails use a shared layout and typed data without excessive conditions.

The Razor SDK compiles templates during the build into the Infrastructure assembly. [Razor.Templating.Core](https://github.com/soundaranbu/Razor.Templating.Core) renders strings. Runtime compilation from incoming messages or the database is not enabled. The Worker Docker image uses aspnet:10.0 for Razor and provider callback webhooks. Applications submit notifications only through RabbitMQ.

## Application profiles

Configure RabbitMQ command recipients and system email profiles in appsettings.json:

~~~json
{
  "RabbitMq": {
    "Applications": ["opencrm", "catalog"]
  },
  "SystemTemplates": {
    "Applications": {
      "opencrm": {
        "BrandName": "opencrm.example.invalid",
        "Templates": [
          "system/email-confirmation",
          "system/password-reset",
          "system/subscription-expiring"
        ]
      },
      "catalog": {
        "BrandName": "Каталог",
        "Templates": ["system/email-confirmation"]
      }
    }
  }
}
~~~

Application IDs and names here are deployment settings. The engine and Razor templates do not check for a particular client's name. Add another application through configuration and a Worker restart, without changing or rebuilding code.

BrandName is used in the subject and shared layout. It is supplied through ViewData from the profile, not from the user's command, and Razor escapes it. It is required, limited to 200 characters, and may not contain control characters.

Templates is an explicit key allowlist. Unknown keys, duplicates, an empty name, or an empty list are rejected at startup. A missing profile disallows system emails for that application without changing access to ordinary messages or database templates.

RabbitMq:Applications still defines the incoming queues. A presentation profile neither creates a queue nor grants access to another application's data.

In Docker, override settings using standard .NET environment variables, such as SystemTemplates__Applications__catalog__BrandName and SystemTemplates__Applications__catalog__Templates__0. Also add the corresponding RabbitMq__Applications__N entry. A new profile requires both a name and a template list.

## Keys and models

| TemplateKey | TemplateVersion | Data fields |
| --- | --- | --- |
| system/email-confirmation | 1 | name, confirmationUrl |
| system/password-reset | 1 | name, resetUrl |
| system/subscription-expiring | 1 | name, subscriptionName, endDate, renewalUrl |

See [examples/system-emails](../examples/system-emails) for Data examples. endDate uses an ISO 8601 date, for example 2026-09-16. It is DateOnly in the model; the Razor template explicitly uses ru-RU for user-facing formatting.

System emails are available after Worker startup to applications with a corresponding profile. There is no need to send UpsertNotificationTemplate or wait for NotificationTemplateUpdated beforehand. The system/ prefix is reserved: templates under it cannot be registered or overwritten through RabbitMQ. Unknown keys, versions, applications, or channels are rejected; no fallback to another version or database template occurs.

## Sending from OpenCRM

~~~csharp
await endpoint.Send(
    new SendNotification(new CreateNotificationRequest(
        IdempotencyKey: $"email-confirmation:{confirmationRequestId}",
        Channel: "email",
        Recipient: userEmail,
        TemplateKey: "system/email-confirmation",
        Data: new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement(userName),
            ["confirmationUrl"] = JsonSerializer.SerializeToElement(confirmationUrl)
        },
        Subject: null,
        Body: null,
        ScheduledAt: null,
        TemplateVersion: 1)),
    cancellationToken);
~~~

confirmationRequestId is the persisted identifier of a particular OpenCRM job. Retrying that job uses the same IdempotencyKey. A new user request for an email receives a new key. OpenCRM builds the tokenized URL; the service only inserts it into the email.

Data is converted into a model with required-field, type, and length validation. Unknown fields are also rejected so typos do not go unnoticed. JSON field names are case-insensitive; examples use camelCase.

URLs must be absolute HTTP(S), without credentials or control characters. Razor escapes model values: a string containing HTML or C# remains text and is not executed. Html.Raw is not used for user data. The rendered subject must not exceed 1000 characters or contain control characters.

Invalid data produces NotificationCommandRejected with the original MessageId and ErrorCode=invalid-command before notification creation or a provider call.

## Versions and changing emails

The RazorSystemTemplateRenderer catalog selects the view path. An incoming command path is not used to locate files. The profile is selected by ApplicationId derived from the incoming queue and validates allowed keys.

V1 includes email text, models, and the shared layout. When changing content or a model:

1. Add a new version with its own layout and models, retaining the previous one.
2. Add the exact application/key/channel/version mapping to the catalog.
3. Validate Razor rendering and delivery through the broker.
4. Deploy the Worker with the new version.
5. Switch OpenCRM to the required TemplateVersion.

Do not edit the published V1 layout to release V2: accepted deferred emails are protected by snapshots, but new commands explicitly selecting V1 must preserve its behavior. Do not remove old templates from the image while clients or old jobs might use them. A new version requires a Worker build and deployment.

## Editable campaigns and SMS

These retain database templates with {{name}} and {{url:link}} substitutions. Their keys must not start with system/. This is a separate mechanism without Razor execution.

UpsertNotificationTemplate registers an immutable version by (ApplicationId, Key, Channel, Version). Repeating the same content is acknowledged; changing an existing version is rejected. Before sending, receive NotificationTemplateUpdated with the corresponding Key, Channel, and Version.

An administrative campaign can store its final markup as template campaign-123 version 1. Recipients receive the key, version, and personal parameters; no separate template is needed for each recipient.

Database templates allow only flat string, numeric, and boolean Data values. Objects, arrays, and null are rejected. HTML parameters are escaped; store email markup in BodyTemplate. Loops, expressions, and raw HTML parameters are unsupported in this mechanism. SMS templates use plain text.

Old commands without Version/TemplateVersion use version 1. Old email-confirmation and subscription-expiring keys without system/ still refer to the database; the built-in Razor catalog does not override them.

## Snapshot and database update

Subject, Body, IsHtml, TemplateKey, and TemplateVersion are stored at command acceptance. Repeating IdempotencyKey returns the previous state; new Data does not replace the first email. Old Worker records with IsHtml=null receive a snapshot before contacting the provider.

Snapshots still require the previously prepared 20260909054007_VersionedTemplateSnapshots migration. Razor requires no additional migration. Existing database templates remain version 1; the initial migration is unchanged.

1. Back up the notifications database.
2. Stop the old Worker.
3. Apply migrations through --migrate from the new image.
4. Start the new Worker, then switch clients to system keys.

The old and new Worker must not process jobs concurrently during transition. Down is allowed while all database templates are version 1 and no notifications have TemplateVersion > 1. Otherwise, rollback stops before deleting data; a separate data preservation plan or backup restoration is required.

provider-accepted means provider acceptance. The Worker receives Unisender reports and forwards normalized delivery events to OpenCRM through RabbitMQ.

## Emails from the editor

system/custom-email accepts subject and htmlBody. The compiled template inserts trusted editor HTML into the shared layout; that string is neither compiled nor executed as Razor. Ordinary parameters of other system templates remain escaped. OpenCRM does not register a separate database template for each such email.

NotificationStatusChanged.Content returns the subject, HTML, and plain-text snapshot to the owning application's queue so OpenCRM can show the rendered email in its log. The separate ProviderWebhookEvents migration concerns provider events, not Razor.
