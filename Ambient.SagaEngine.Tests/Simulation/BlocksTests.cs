using Ambient.Domain.DefinitionExtensions;
using Ambient.Infrastructure.GameLogic;
using Ambient.SagaEngine.Infrastructure.Loading;

namespace Ambient.SagaEngine.Tests.Simulation;

public class BlocksTests : IAsyncLifetime
{
    private World? _world;

    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public BlocksTests()
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
        _world = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Lat0Height256");

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
