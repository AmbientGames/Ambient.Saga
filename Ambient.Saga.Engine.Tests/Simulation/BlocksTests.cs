using Ambient.Domain.Contracts;
using Ambient.Infrastructure.GameLogic;
using Ambient.Infrastructure.GameLogic.Loading;

namespace Ambient.Saga.Engine.Tests.Simulation;

public class BlocksTests : IAsyncLifetime
{
    private readonly IWorldFactory _worldFactory = new TestWorldFactory();
    private readonly IWorldConfigurationLoader _configurationLoader = new WorldConfigurationLoader();
    private readonly IWorldLoader _worldLoader;
    private IWorld _world;

    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public BlocksTests()
    {
        // Content/Schemas is copied to output directory by Ambient.Domain
        _definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Schemas");

        // Content/Worlds is at solution root (shared by all Sandboxes)
        _dataDirectory = FindWorldsDirectory();

        _worldLoader = new WorldAssetLoader(_worldFactory, _configurationLoader);
    }

    private static string FindWorldsDirectory()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        while (directory != null)
        {
            var worldDefPath = Path.Combine(directory, "Content", "Worlds");
            if (Directory.Exists(worldDefPath))
                return worldDefPath;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not find Content/Worlds directory");
    }

    public async Task InitializeAsync()
    {
        _world = await _worldLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Ise");

        //_world.AvailableWorldConfigurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

        WorldBootstrapper.Initialize(_world);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    //[Fact]
    //public void BlocksClassList_IsNotEmpty()
    //{
    //    Assert.NotNull(_world?.Simulation.Blocks);
    //    Assert.NotEmpty(_world.Simulation.Blocks.BlockList);
    //}

    [Fact]
    public void World_ShouldNotBeNull()
    {
        Assert.NotNull(_world);
    }


    //[Fact]
    //public void BlocksClassList_EachBlockClassHasNameAndId()
    //{
    //    Assert.NotEmpty(_world!.Simulation.Blocks.BlockList);
    //    foreach (var block in _world.Simulation.Blocks.BlockList)
    //    {
    //        Assert.False(string.IsNullOrWhiteSpace(block.RefName));
    //        Assert.InRange(block.OrdinalId, 0, 9999);
    //    }
    //}

    //[Fact]
    //public void BlocksClassList_EachBlockHasValidTexture()
    //{
    //    foreach (var block in _world!.Simulation.Blocks.BlockList)
    //    {
    //        Assert.False(string.IsNullOrWhiteSpace(block.TextureRef),
    //            $"Block {block.RefName} in category {block.Category} has empty Texture attribute");
    //    }
    //}
}
