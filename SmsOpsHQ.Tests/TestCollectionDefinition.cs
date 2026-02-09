using Xunit;

namespace SmsOpsHQ.Tests;

// All integration tests that share the IntegrationTestFixture run serially
// to prevent shared database state race conditions.
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationTestFixture>
{
}
