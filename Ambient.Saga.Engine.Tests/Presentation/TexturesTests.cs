using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Infrastructure.GameLogic;
using Ambient.Saga.Engine.Infrastructure.Loading;

namespace Ambient.Saga.Engine.Tests.Presentation;

public partial class TexturesTests : IAsyncLifetime
{
    private World? _world;

    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public TexturesTests()
    {
        // DefinitionXsd is copied to output directory by Ambient.Domain
        _definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefinitionXsd");

        // WorldDefinitions still lives in Sandbox source directory
        var sandboxDirectory = FindSandboxDirectory();
        _dataDirectory = Path.Combine(sandboxDirectory, "WorldDefinitions");
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
        _world = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Ise");

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
