using Ambient.Saga.Engine.Application.Handlers.Loading;
using Ambient.Saga.Engine.Application.Queries.Loading;
using Ambient.Infrastructure.GameLogic.Loading;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Ambient.Domain.Contracts;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for world loading queries through CQRS.
/// These tests verify that the CQRS boundary works for infrastructure operations.
/// </summary>
public class WorldLoadingQueryTests : IDisposable
{
    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;
    private readonly IMediator _mediator;

    public WorldLoadingQueryTests()
    {
        // Content/Schemas is copied to output directory by Ambient.Domain
        _definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Schemas");

        // Content/Worlds is at solution root (shared by all Sandboxes)
        _dataDirectory = FindWorldsDirectory();

        // Setup MediatR with world loading handlers
        var services = new ServiceCollection();
        services.AddSingleton<IWorldFactory, TestWorldFactory>();
        services.AddSingleton<IWorldConfigurationLoader, WorldConfigurationLoader>();
        services.AddSingleton<IWorldLoader, WorldAssetLoader>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(LoadWorldHandler).Assembly));
        var serviceProvider = services.BuildServiceProvider();
        _mediator = serviceProvider.GetRequiredService<IMediator>();
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

    [Fact]
    public async Task LoadAvailableWorldConfigurationsQuery_ReturnsConfigurations()
    {
        // Arrange
        var query = new LoadAvailableWorldConfigurationsQuery
        {
            DataDirectory = _dataDirectory,
            DefinitionDirectory = _definitionDirectory
        };

        // Act
        var configurations = await _mediator.Send(query);

        // Assert
        Assert.NotNull(configurations);
        Assert.NotEmpty(configurations);
        Assert.All(configurations, config =>
        {
            Assert.False(string.IsNullOrEmpty(config.RefName));
            Assert.False(string.IsNullOrEmpty(config.Template));
        });
    }

    [Fact]
    public async Task LoadWorldQuery_WithValidConfiguration_LoadsWorld()
    {
        // Arrange - First get available configurations
        var configQuery = new LoadAvailableWorldConfigurationsQuery
        {
            DataDirectory = _dataDirectory,
            DefinitionDirectory = _definitionDirectory
        };
        var configurations = await _mediator.Send(configQuery);
        var firstConfig = configurations.First();

        var loadQuery = new LoadWorldQuery
        {
            DataDirectory = _dataDirectory,
            DefinitionDirectory = _definitionDirectory,
            ConfigurationRefName = firstConfig.RefName
        };

        // Act
        var world = await _mediator.Send(loadQuery);

        // Assert
        Assert.NotNull(world);
        Assert.NotNull(world.WorldConfiguration);
        Assert.Equal(firstConfig.RefName, world.WorldConfiguration.RefName);
        Assert.NotNull(world.WorldTemplate);
        Assert.NotNull(world.Gameplay);
    }

    [Fact]
    public async Task LoadWorldQuery_WithInvalidConfiguration_ThrowsException()
    {
        // Arrange
        var query = new LoadWorldQuery
        {
            DataDirectory = _dataDirectory,
            DefinitionDirectory = _definitionDirectory,
            ConfigurationRefName = "NonExistentWorld"
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _mediator.Send(query));
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}
