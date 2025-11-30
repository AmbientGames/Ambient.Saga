using Ambient.Saga.Sandbox.WindowsUI;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Presentation.UI.Components.Modals;
using Ambient.Saga.Presentation.UI.Services;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Services;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Steamworks;

namespace Ambient.Saga.Sandbox.WindowsUI.Services
{
    /// <summary>
    /// Configures dependency injection for the Sandbox application.
    /// Mirrors the setup from Ambient.Schema.Sandbox/App.xaml.cs
    /// </summary>
    public static class ServiceProviderSetup
    {
        public static bool IsSteamInitialized { get; private set; }
        public static bool SteamStatsReceived { get; private set; }

        private static Callback<UserStatsReceived_t>? _userStatsReceived;
        private static System.Windows.Forms.Timer? _callbacksTimer;
        private static TaskCompletionSource<bool>? _statsTcs;

        /// <summary>
        /// Build the service provider with all dependencies
        /// </summary>
        public static ServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();

            // Configure application services
            ConfigureAppServices(services);
            ConfigureLogging(services);
            ConfigureGameplayServices(services);
            ConfigureSandboxServices(services);

            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        }

        private static void ConfigureAppServices(IServiceCollection services)
        {
            // Windows and ViewModels
            services.AddTransient<MainWindow>();
            services.AddTransient<MainViewModel>();

            // Archetype selectors - keyed services for WPF and ImGui
            //services.AddKeyedSingleton<IArchetypeSelector, WpfArchetypeSelector>("wpf");

            // ImGui archetype selector (registered separately to wire up circular dependency)
            services.AddSingleton<ImGuiArchetypeSelector>();
            services.AddKeyedSingleton<IArchetypeSelector>(
                "imgui",
                (sp, key) => sp.GetRequiredService<ImGuiArchetypeSelector>());

            // World content generator (mock implementation - WorldForge not included in open-source build)
            // To use real WorldForge, change to: services.AddSingleton<IWorldContentGenerator, Ambient.Saga.WorldForge.WorldContentGenerator>();
            services.AddSingleton<IWorldContentGenerator, MockWorldContentGenerator>();

            // Modal manager for ImGui archetype selector (with circular dependency resolution)
            services.AddSingleton(sp =>
            {
                var selector = sp.GetRequiredService<ImGuiArchetypeSelector>();
                var mediator = sp.GetRequiredService<IMediator>();
                var worldContentGenerator = sp.GetRequiredService<IWorldContentGenerator>();
                var modalManager = new ModalManager(selector, mediator, worldContentGenerator);
                selector.SetModalManager(modalManager); // Wire up circular reference
                return modalManager;
            });

            // World Map UI for Tab 3
            services.AddTransient<WorldMapUI>();
        }

        private static void ConfigureLogging(IServiceCollection services)
        {
            services.AddLogging(configure =>
            {
                configure.AddConsole();
                configure.AddDebug();
                configure.SetMinimumLevel(LogLevel.Information);
            });
        }

        private static void ConfigureGameplayServices(IServiceCollection services)
        {
            // MediatR for CQRS
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining<UpdateAvatarPositionCommand>();

                // Register pipeline behaviors (run in order)
                cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
                cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
                cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
            });

            // Repository factory (creates repositories when world loads)
            services.AddSingleton<IWorldRepositoryFactory, WorldRepositoryFactory>();

            // Saga repositories
            services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();

            // ISagaInstanceRepository factory - will be configured by MainViewModel when world loads
            services.AddSingleton<SagaInstanceRepositoryProvider>();
            services.AddSingleton(sp =>
                sp.GetRequiredService<SagaInstanceRepositoryProvider>().Repository);

            // World factory - will be configured by MainViewModel when world loads
            // Returns null until world is loaded - handlers must check for null
            services.AddSingleton<WorldProvider>();
            services.AddSingleton(sp => sp.GetRequiredService<WorldProvider>().World);

            // IGameAvatarRepository factory - will be configured by MainViewModel when world loads
            services.AddSingleton<GameAvatarRepositoryProvider>();
            services.AddSingleton(sp =>
                sp.GetRequiredService<GameAvatarRepositoryProvider>().Repository);

            // IWorldStateRepository factory - will be configured by MainViewModel when world loads
            services.AddSingleton<WorldStateRepositoryProvider>();
            services.AddSingleton(sp =>
                sp.GetRequiredService<WorldStateRepositoryProvider>().Repository);

            // Avatar update service (depends on IGameAvatarRepository)
            services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();
        }

        private static void ConfigureSandboxServices(IServiceCollection services)
        {
            // Register sandbox-specific services using extension method
            services.AddSchemaSandboxServices();
        }

        /// <summary>
        /// Initialize Steam API (called at application startup)
        /// </summary>
        public static void InitializeSteam()
        {
            try
            {
                IsSteamInitialized = SteamAPI.Init();

                if (IsSteamInitialized)
                {
                    var appId = SteamUtils.GetAppID();
                    var steamUserName = SteamFriends.GetPersonaName();
                    System.Diagnostics.Debug.WriteLine($"[Steam] Init OK | AppID: {appId} | User: {steamUserName}");

                    // Register stats callback
                    _userStatsReceived = Callback<UserStatsReceived_t>.Create(OnUserStatsReceived);

                    // Request current stats (asynchronous)
                    var statsRequested = SteamUserStats.RequestCurrentStats();
                    System.Diagnostics.Debug.WriteLine($"[Steam] RequestCurrentStats: {statsRequested}");

                    // Pump callbacks regularly using WinForms timer
                    _callbacksTimer = new System.Windows.Forms.Timer
                    {
                        Interval = 100
                    };
                    _callbacksTimer.Tick += (_, __) => SteamAPI.RunCallbacks();
                    _callbacksTimer.Start();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Failed to initialize Steam.\\n\\n" +
                        "Make sure:\\n" +
                        "1. Steam client is running\\n" +
                        "2. steam_appid.txt exists in output directory\\n" +
                        "3. You're logged into Steam\\n\\n" +
                        "Application will continue without Steam features.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Steam initialization error: {ex.Message}");
                IsSteamInitialized = false;
            }
        }

        /// <summary>
        /// Shutdown Steam API (called at application exit)
        /// </summary>
        public static void ShutdownSteam()
        {
            if (IsSteamInitialized)
            {
                _callbacksTimer?.Stop();
                _callbacksTimer?.Dispose();
                SteamAPI.Shutdown();
            }
        }

        private static void OnUserStatsReceived(UserStatsReceived_t cb)
        {
            if (cb.m_eResult == EResult.k_EResultOK)
            {
                SteamStatsReceived = true;
                _statsTcs?.TrySetResult(true);

                var numAchievements = SteamUserStats.GetNumAchievements();
                System.Diagnostics.Debug.WriteLine($"[Steam] Stats received | Achievements available: {numAchievements}");

                // Enumerate achievements
                for (uint i = 0; i < numAchievements; i++)
                {
                    var name = SteamUserStats.GetAchievementName(i);
                    SteamUserStats.GetAchievement(name, out var achieved);
                    var displayName = SteamUserStats.GetAchievementDisplayAttribute(name, "name");
                    var desc = SteamUserStats.GetAchievementDisplayAttribute(name, "desc");
                    System.Diagnostics.Debug.WriteLine($"[Steam]  - {name} ({displayName}) [{(achieved ? "UNLOCKED" : "locked")}] | {desc}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Steam] UserStatsReceived failed: {cb.m_eResult}");
                _statsTcs?.TrySetResult(false);
            }
        }

        /// <summary>
        /// Optional helper: await stats readiness from any UI code
        /// </summary>
        public static async Task<bool> WaitForSteamStatsAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            if (!IsSteamInitialized) return false;
            if (SteamStatsReceived) return true;

            if (_statsTcs == null || _statsTcs.Task.IsCompleted)
                _statsTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await using var _ = cts.Token.Register(() => _statsTcs.TrySetResult(false));

            return await _statsTcs.Task.ConfigureAwait(false);
        }
    }
}
