using Xunit;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Collection definition for CQRS integration tests.
/// Forces tests to run sequentially to avoid LiteDB concurrency issues.
/// </summary>
[CollectionDefinition("Sequential CQRS Tests", DisableParallelization = true)]
public class CqrsTestCollection
{
}
