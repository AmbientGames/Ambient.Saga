// REMOVED: using Ambient.Game.Gameplay.Rpg.Instances - no longer using mutable instances

namespace Ambient.Saga.Sandbox.WindowsUI.State;

/// <summary>
/// Simplified game data state for the Schema Sandbox.
/// Only implements features needed for 2D map visualization.
/// Character state is now derived from SagaState (event-sourced), not stored here.
/// </summary>
public class SandboxGameDataState
{
    //// REMOVED: SpawnedCharacters list
    //// Characters are now tracked via SagaState (event-sourced from transactions)
    //// Query characters using WorldStateRepository.GetAllAliveCharacters()

    ///// <summary>
    ///// Other players in multiplayer (not based on NPC templates).
    ///// Tracked separately from NPC spawns.
    ///// </summary>
    //public List<PlayerInstance> MultiplayerPlayers { get; set; } = new();

    //#region Interface Requirements (Minimal/No-op implementations for sandbox)

    //// Legacy Actor list - not used in sandbox, but required by interface
    //public List<Actor> Actors { get; set; } = new();

    //public GameAvatar Avatar { get; set; }
    //public GameWorld World { get; set; } = new GameWorld();
    //public IModGameplay ModGameplay { get; set; }
    //public IModSimulation ModSimulation { get; set; }
    //public IModPresentation ModPresentation { get; set; }
    //public bool ShuttingDown { get; set; }
    //public Shape GameActorRepresentation { get; set; }
    //public int ChunkInitializationsReceived { get; set; }
    //public IRenderingSettings RenderingSettings { get; set; }
    //public double PingTime { get; set; }
    //public IStatisticsTracker Statistics { get; set; }
    //public ISpatialLodManager SpatialLodManager { get; set; }

    //// 3D rendering not used in 2D map sandbox
    //public bool IsInitialBuildComplete => true;
    //public bool IsInitialGenerationComplete => true;

    //ISpatialLodManager IGameDataState.SpatialLodManager { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    //public bool IsReadyToRender3D(IGameState gameState) => false;
    //public void DecreaseQuality() { }
    //public void IncreaseQuality() { }
    //public void InitializeQuality() { }

    //#endregion
}

/// <summary>
/// Represents another player in multiplayer (not an NPC).
/// Separate from CharacterInstance since players aren't based on character templates.
/// </summary>
public class PlayerInstance
{
    /// <summary>
    /// Unique player ID (from multiplayer server).
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>
    /// Player's display name.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Current latitude position on the map.
    /// </summary>
    public double CurrentLatitudeZ { get; set; }

    /// <summary>
    /// Current longitude position on the map.
    /// </summary>
    public double CurrentLongitudeX { get; set; }

    /// <summary>
    /// Current altitude/height.
    /// </summary>
    public double CurrentY { get; set; }

    /// <summary>
    /// Last seen timestamp (for timeout/disconnect detection).
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}
