// This file defines a named xUnit collection for all integration tests.
//
// Why this matters:
// Integration tests share a WebApplicationFactory. Without a [Collection]
// attribute, xUnit may spin up multiple factories in parallel, which causes
// port conflicts and flaky test failures when tests try to bind to the same
// in-memory database or HTTP test server concurrently.
//
// All integration test classes that inherit IntegrationTestBase should carry
// [Collection("Integration")] — this file is the anchor that registers the
// collection name so xUnit knows to run those tests sequentially.
//
// Unit tests (which don't use the factory) are unaffected and still run in parallel.

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = false)]

namespace LedgerFlow.IntegrationTests;

[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<Infrastructure.LedgerFlowWebApplicationFactory>
{
    // This class has no code — it's just a marker that xUnit reads via reflection
    // to group tests into the "Integration" collection and share the factory.
}
