namespace Ambient.SagaEngine.Domain.Rpg.Trade;

/// <summary>
/// Result of a trade operation indicating success or failure with a message.
/// </summary>
public class TradeResult
{
    public bool Success { get; init; }
    public string Message { get; init; }

    private TradeResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public static TradeResult Succeeded(string message) => new(true, message);
    public static TradeResult Failed(string message) => new(false, message);
}
