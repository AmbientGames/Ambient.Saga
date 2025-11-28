using System.Collections.Generic;
using Ambient.Domain;

namespace Ambient.SagaEngine.Domain.Rpg.Battle;

public interface ICombatAI
{
    CombatAction DecideTurn(BattleView view);
}

public class BattleView
{
    public required Combatant Self { get; init; }
    public required Combatant Opponent { get; init; }

    public List<CombatEvent> History { get; init; } = new();

    public int TurnNumber { get; init; }
}

public class CombatAction
{
    public required ActionType ActionType { get; init; }

    public string? Parameter { get; init; }

    public override string ToString() => ActionType switch
    {
        ActionType.Attack => $"Attack",
        ActionType.CastSpell => $"Cast {Parameter}",
        ActionType.UseConsumable => $"Use {Parameter}",
        ActionType.Defend => "Defend", // active bonus to what is in left hand or just a bonus
        ActionType.Flee => "Flee",
        ActionType.AdjustLoadout => $"Adjust Loadout: {Parameter}", // moderate defense bonus
        ActionType.ChangeLoadout => $"Change Loadout: {Parameter}", // minor defenses bonus
        ActionType.Talk => "Talk",
        _ => ActionType.ToString()
    };
}

public enum ActionType
{
    Attack,
    CastSpell,
    UseConsumable,
    Defend,
    Flee,
    AdjustLoadout,
    ChangeLoadout,
    Talk
}
