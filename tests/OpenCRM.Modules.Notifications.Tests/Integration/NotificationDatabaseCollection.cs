using OpenCRM.Testing;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[CollectionDefinition(nameof(NotificationDatabaseCollection))]
public sealed class NotificationDatabaseCollection : ICollectionFixture<NotificationFixture>;
