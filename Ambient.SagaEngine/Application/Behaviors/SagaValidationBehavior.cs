using Ambient.SagaEngine.Application.Results.Saga;
using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Ambient.SagaEngine.Application.Behaviors;

/// <summary>
/// Pipeline behavior that validates commands before execution.
/// Uses DataAnnotations validation on command properties.
/// </summary>
public class SagaValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Validate command using DataAnnotations
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(request);
        var isValid = Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true);

        if (!isValid)
        {
            var errors = string.Join(", ", validationResults.Select(v => v.ErrorMessage));

            // If response is SagaCommandResult, return failure result
            if (typeof(TResponse) == typeof(SagaCommandResult))
            {
                var failureResult = SagaCommandResult.Failure(Guid.Empty, $"Validation failed: {errors}");
                return (TResponse)(object)failureResult;
            }

            // Otherwise throw exception
            throw new ValidationException($"Validation failed for {typeof(TRequest).Name}: {errors}");
        }

        // Validation passed, continue with execution
        return await next();
    }
}
