namespace Ambient.Rpg.Engine.Domain;

/// <summary>
/// Canonical tool-sharpening pricing. Server-authoritative: SharpenToolHandler
/// rejects any client-supplied cost that disagrees, so a crafted command can
/// neither mint credits (negative cost) nor sharpen for free. UI reads the same
/// constant, so the displayed price can never drift from the enforced one.
/// </summary>
public static class ToolSharpening
{
    public const int CostCredits = 50;
}
