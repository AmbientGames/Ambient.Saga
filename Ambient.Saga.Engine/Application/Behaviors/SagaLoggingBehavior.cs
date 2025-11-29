using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;
using System.Diagnostics;

namespace Ambient.Saga.Engine.Application.Behaviors;

/// <summary>
/// Pipeline behavior that logs all Saga commands and queries.
/// Tracks execution time, success/failure, and transaction IDs.
/// </summary>
public class SagaLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
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
            //Debug.WriteLine($"[Saga CQRS] Executing {requestName}");

            var response = await next();

            sw.Stop();

            // Log result details if it's a SagaCommandResult
            if (response is SagaCommandResult commandResult)
            {
                if (commandResult.Successful)
                {
                    Debug.WriteLine($"[Saga CQRS] {requestName} succeeded in {sw.ElapsedMilliseconds}ms - " +
                                  $"SagaInstance: {commandResult.SagaInstanceId}, " +
                                  $"Transactions: {commandResult.TransactionIds.Count}, " +
                                  $"Sequence: {commandResult.NewSequenceNumber}");
                }
                else
                {
                    Debug.WriteLine($"[Saga CQRS] {requestName} failed in {sw.ElapsedMilliseconds}ms - " +
                                  $"Error: {commandResult.ErrorMessage}");
                }
            }
            //else
            //{
            //    Debug.WriteLine($"[Saga CQRS] {requestName} completed in {sw.ElapsedMilliseconds}ms");
            //}

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Debug.WriteLine($"[Saga CQRS] {requestName} threw exception after {sw.ElapsedMilliseconds}ms - " +
                          $"{ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}
