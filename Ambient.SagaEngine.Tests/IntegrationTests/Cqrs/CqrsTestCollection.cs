using Xunit;

namespace Ambient.SagaEngine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Collection definition for CQRS integration tests.
/// Forces tests to run sequentially to avoid LiteDB concurrency issues.
/// </summary>
[CollectionDefinition("Sequential CQRS Tests", DisableParallelization = true)]
public class CqrsTestCollection
{
}
