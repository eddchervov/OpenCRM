using OpenCRM.Testing;

namespace OpenCRM.Modules.Support.Tests.Integration;

[CollectionDefinition(nameof(SupportDatabaseCollection))]
public sealed class SupportDatabaseCollection : ICollectionFixture<NotificationFixture>;
