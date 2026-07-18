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

    // Propriétés d'identité reconnues sur les commandes/queries, mappées vers des
    // champs de log normalisés. Elles alimentent la CORRÉLATION dans Loki : tous les
    // logs émis pendant le traitement d'une commande portent user_id/order_id/…,
    // requêtables en LogQL (`| json | order_id="…"`). Poussées via BeginScope (MEL)
    // → captées par Serilog en CHAMPS JSON, jamais en labels (cardinalité maîtrisée).
    private static readonly (string Prop, string Field)[] CorrelationKeys =
    {
        ("UserId", "user_id"),
        ("OrderId", "order_id"),
        ("SellerId", "seller_id"),
        ("ProductId", "product_id"),
        ("PaymentId", "payment_id"),
    };

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var correlation = BuildCorrelationScope(request);

        using (correlation is null ? null : _logger.BeginScope(correlation))
        {
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

    /// <summary>
    /// Extrait par réflexion les identifiants de corrélation présents sur la requête.
    /// Renvoie <c>null</c> si aucun (pas de scope inutile).
    /// </summary>
    private static Dictionary<string, object>? BuildCorrelationScope(TRequest request)
    {
        Dictionary<string, object>? scope = null;
        var type = request.GetType();

        foreach (var (prop, field) in CorrelationKeys)
        {
            var value = type.GetProperty(prop)?.GetValue(request);
            if (value is null)
            {
                continue;
            }

            // On ignore les identifiants « vides » (Guid.Empty) : ils n'apportent rien.
            if (value is Guid guid && guid == Guid.Empty)
            {
                continue;
            }

            (scope ??= new Dictionary<string, object>())[field] = value;
        }

        return scope;
    }
}
