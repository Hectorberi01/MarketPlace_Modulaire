using Marketplace.BuildingBlocks.Domain.Results;

namespace HbaExpress.Dispatch.Api.Extensions;

/// <summary>
/// Traduit un Result métier en réponse HTTP. Repris de Marketplace.Api.Extensions : le
/// domaine partage déjà Result et Error avec le monolithe, la traduction n'a aucune
/// raison d'en différer, et un coursier ne doit pas lire un code différent selon le
/// service qui lui répond.
/// </summary>
public static class ApiResults
{
    public static IResult Match<TValue>(this Result<TValue> result, Func<TValue, IResult> onSuccess)
        => result.IsSuccess ? onSuccess(result.Value) : Problem(result.Error);

    public static IResult Match(this Result result, Func<IResult> onSuccess)
        => result.IsSuccess ? onSuccess() : Problem(result.Error);

    private static IResult Problem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(title: error.Code, detail: error.Message, statusCode: statusCode);
    }
}
