using Ambient.Domain.DefinitionExtensions;
using Ambient.Infrastructure.GameLogic;
using Ambient.Saga.Engine.Infrastructure.Loading;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Tests for world bootstrapping functionality using the real WorldBootstrapper.
/// This includes derived block processing, texture atlas creation, block distribution generation,
/// and configuration-specific settings application.
/// </summary>
public class WorldBootstrapperTests : IAsyncLifetime
{
    private World? _world;

    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public WorldBootstrapperTests()
    {
        var domainDirectory = FindSandboxDirectory();
        _dataDirectory = Path.Combine(domainDirectory, "WorldDefinitions");
        _definitionDirectory = Path.Combine(domainDirectory, "DefinitionXsd");
    }

    private static string FindSandboxDirectory()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        while (directory != null)
        {
            var domainPath = Path.Combine(directory, "Ambient.Saga.Sandbox.WindowsUI");
            if (Directory.Exists(domainPath))
                return domainPath;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not find Sandbox directory");
    }
    public async Task InitializeAsync()
    {
        // Load the world data
        _world = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Ise");

        //_world.AvailableWorldConfigurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

        // Select the first configuration (simulating user selection)
        if (_world.AvailableWorldConfigurations?.Length > 0)
        {
            _world.WorldConfiguration = _world.AvailableWorldConfigurations[0];
        }

        WorldBootstrapper.Initialize(_world);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }


    [Fact]
    public void Bootstrap_ShouldInitializeWorld()
    {
        Assert.NotNull(_world);
        Assert.NotNull(_world.WorldConfiguration);
        Assert.NotNull(_world.WorldTemplate);
    }

    //[Fact]
    //public void Bootstrap_ShouldCreateDerivedBlockList()
    //{
    //    Assert.NotNull(_world);
    //    Assert.NotNull(_world.DerivedBlockList);
    //    Assert.NotEmpty(_world.DerivedBlockList);
    //}

    //[Fact]
    //public void Bootstrap_ShouldMaintainLookupDictionaries()
    //{
    //    Assert.NotNull(_world);
        
    //    // Verify that lookups are still populated after bootstrapping
    //    Assert.NotEmpty(_world.BlocksLookup);
    //    Assert.NotEmpty(_world.SubstancesLookup);
    //    Assert.NotEmpty(_world.SagaArcLookup);
        
    //    // Test that lookups work correctly
    //    var firstBlock = _world.Simulation.Blocks.BlockList.First();
    //    var foundBlock = _world.GetBlockByRefName(firstBlock.RefName);
    //    Assert.Equal(firstBlock.RefName, foundBlock.RefName);
    //}
}