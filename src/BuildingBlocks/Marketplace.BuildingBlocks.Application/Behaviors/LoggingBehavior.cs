using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Behavior de logging : trace l'entrée/sortie de chaque requête, sa durée, et
/// distingue succès/échec métier sans bruit d'exception.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogInformation("Traitement de {RequestName}", requestName);

        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        if (response.IsSuccess)
        {
            _logger.LogInformation(
                "{RequestName} traité en {ElapsedMs} ms",
                requestName, stopwatch.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogWarning(
                "{RequestName} en échec ({ErrorCode}) en {ElapsedMs} ms : {ErrorMessage}",
                requestName, response.Error.Code, stopwatch.ElapsedMilliseconds, response.Error.Message);
        }

        return response;
    }
}
