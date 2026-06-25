using FluentValidation;
using MediatR;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Behavior de validation : exécute les FluentValidation enregistrés pour la
/// requête. En cas d'échec, court-circuite le handler et renvoie un Result
/// d'échec typé — pas d'exception qui traverse les couches.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return await next();
        }

        var message = string.Join(" | ", failures.Select(f => f.ErrorMessage));
        var error = Error.Validation("validation.failed", message);

        return CreateFailure(error);
    }

    private static TResponse CreateFailure(Error error)
    {
        // TResponse est soit Result, soit Result<T> : on fabrique le bon échec.
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        var valueType = typeof(TResponse).GetGenericArguments()[0];
        var failureMethod = typeof(Result)
            .GetMethods()
            .First(m => m is { Name: nameof(Result.Failure), IsGenericMethod: true })
            .MakeGenericMethod(valueType);

        return (TResponse)failureMethod.Invoke(null, new object[] { error })!;
    }
}
