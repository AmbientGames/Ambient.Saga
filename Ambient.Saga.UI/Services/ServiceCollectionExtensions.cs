using Microsoft.Extensions.DependencyInjection;

namespace Ambient.Saga.UI.Services;

/// <summary>
/// Extension methods for registering Schema Sandbox services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register all Schema Sandbox services
    /// </summary>
    public static IServiceCollection AddSchemaSandboxServices(this IServiceCollection services)
    {
        // Core sandbox services
        ConfigureSandboxCoreServices(services);

        // Data and persistence services
        ConfigureSandboxDataServices(services);

        return services;
    }

    private static void ConfigureSandboxCoreServices(IServiceCollection services)
    {
        // Trigger and discovery management
        // Note: TriggerDiscoveryService is NOT registered in DI because it requires
        // ObservableCollections from MainViewModel. It's instantiated directly in MainViewModel.

        // Future services:
        // services.AddSingleton<IMapRenderingService, MapRenderingService>();
        // services.AddTransient<IValidationService, ValidationService>();
    }

    private static void ConfigureSandboxDataServices(IServiceCollection services)
    {
        // World state and persistence (created per-world, not registered in DI)
        // WorldStateDatabase and WorldStateRepository are instantiated in MainViewModel
        // when a world is loaded

        // Steam achievement service (created per-world, not registered in DI)
        // SteamAchievementService is instantiated in MainViewModel when world database is initialized
    }
}