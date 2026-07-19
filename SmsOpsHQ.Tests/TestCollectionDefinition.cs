using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SmsOpsHQ.Tests;

// Integration tests share one SQLite WebApplicationFactory database. Keep all test
// collections serial so unrelated classes cannot race authentication or database setup.
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationTestFixture>
{
}
