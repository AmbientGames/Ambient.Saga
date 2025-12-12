using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Infrastructure.GameLogic;
using Ambient.Infrastructure.GameLogic.Loading;

namespace Ambient.Saga.Engine.Tests.Presentation;

public partial class TexturesTests : IAsyncLifetime
{
    private readonly IWorldFactory _worldFactory = new TestWorldFactory();
    private readonly IWorldConfigurationLoader _configurationLoader = new WorldConfigurationLoader();
    private readonly IWorldLoader _worldLoader;
    private IWorld _world;

    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public TexturesTests()
    {
        // DefinitionXsd is copied to output directory by Ambient.Domain
        _definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefinitionXsd");

        // WorldDefinitions is at solution root (shared by all Sandboxes)
        _dataDirectory = FindWorldDefinitionsDirectory();

        _worldLoader = new WorldAssetLoader(_worldFactory, _configurationLoader);
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
        _world = await _worldLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Ise");

        //_world.AvailableWorldConfigurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

        WorldBootstrapper.Initialize(_world);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    //[Fact]
    //public void BlockTexturesGenerated_IsNotEmpty()
    //{
    //    Assert.NotNull(_world?.Presentation.BlockTextures);
    //    Assert.NotEmpty(_world.Presentation.BlockTextures.Generated);
    //}

    //[Fact]
    //public void BlockTextures_ShouldNotBeNull()
    //{
    //    Assert.NotNull(_world?.Presentation.BlockTextures);
    //}

    //[Fact]
    //public void BlockTextures_ShouldGenerate()
    //{
    //    ValidateTextureSet(_world!.Presentation.BlockTextures.Generated);
    //    ValidateTextureSet(_world.Presentation.BlockTextures.System);
    //    ValidateTextureSet(_world.Presentation.BlockTextures.SeasonalLeaves);
    //    ValidateTextureSet(_world.Presentation.BlockTextures.SeasonalGroundCover);
    //}

    //private static void ValidateTextureSet(BlockTexture[] textureSet)
    //{
    //    foreach (var texture in textureSet)
    //    {
    //        var textureImage = TextureLoader.GetTextureImage(texture);

    //        Assert.NotNull(textureImage);
    //        Assert.Equal(32, textureImage.Width);
    //        Assert.Equal(32, textureImage.Height);
    //    }
    //}


    //[Fact]
    //public void BlockTextures_GeneratedShouldContainAtLeastOnePalmTexture()
    //{
    //    Assert.NotNull(_world?.Presentation.BlockTextures.Generated);
    //    Assert.Contains(_world.Presentation.BlockTextures.Generated, t => t.RefName == "Palm");
    //}

    //[Fact]
    //public void BlockTextures_NoTextureHasEmptyName()
    //{
    //    foreach (var tex in _world!.Presentation.BlockTextures.Generated)
    //        Assert.False(string.IsNullOrWhiteSpace(tex.RefName));

    //    foreach (var tex in _world.Presentation.BlockTextures.System)
    //        Assert.False(string.IsNullOrWhiteSpace(tex.RefName));

    //    // etc. for SeasonalGroundCover, SeasonalLeaves...
    //}



}
