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
    private readonly IWorldFactory _worldFactory = new TestWorldFactory();
    private IWorld _world;

    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public WorldBootstrapperTests()
    {
        // DefinitionXsd is copied to output directory by Ambient.Domain
        _definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefinitionXsd");

        // WorldDefinitions is at solution root (shared by all Sandboxes)
        _dataDirectory = FindWorldDefinitionsDirectory();
    }

    private static string FindWorldDefinitionsDirectory()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        while (directory != null)
        {
            var worldDefPath = Path.Combine(directory, "WorldDefinitions");
            if (Directory.Exists(worldDefPath))
                return worldDefPath;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not find WorldDefinitions directory");
    }
    public async Task InitializeAsync()
    {
        // Load the world data
        _world = await WorldAssetLoader.LoadWorldByConfigurationAsync(_worldFactory, _dataDirectory, _definitionDirectory, "Ise");

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