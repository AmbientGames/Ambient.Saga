using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;
using System.Diagnostics;

namespace Ambient.Rpg.Engine.Application.Behaviors;

/// <summary>
/// Pipeline behavior that logs all arc commands and queries.
/// Tracks execution time, success/failure, and transaction IDs.
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        try
        {
            //Debug.WriteLine($"[Arc CQRS] Executing {requestName}");

            var response = await next();

            sw.Stop();

            // Log result details if it's a ArcCommandResult
            if (response is ArcCommandResult commandResult)
            {
                if (commandResult.Successful)
                {
                    Debug.WriteLine($"[Arc CQRS] {requestName} succeeded in {sw.ElapsedMilliseconds}ms - " +
                                  $"ArcInstance: {commandResult.ArcInstanceId}, " +
                                  $"Transactions: {commandResult.TransactionIds.Count}, " +
                                  $"Sequence: {commandResult.NewSequenceNumber}");
                }
                else
                {
                    Debug.WriteLine($"[Arc CQRS] {requestName} failed in {sw.ElapsedMilliseconds}ms - " +
                                  $"Error: {commandResult.ErrorMessage}");
                }
            }
            //else
            //{
            //    Debug.WriteLine($"[Arc CQRS] {requestName} completed in {sw.ElapsedMilliseconds}ms");
            //}

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Debug.WriteLine($"[Arc CQRS] {requestName} threw exception after {sw.ElapsedMilliseconds}ms - " +
                          $"{ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}
