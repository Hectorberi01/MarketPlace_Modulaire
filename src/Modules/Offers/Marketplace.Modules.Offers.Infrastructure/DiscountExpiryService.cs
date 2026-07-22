using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Marketplace.Modules.Offers.Application.Offers.Commands.ExpireOfferDiscounts;

namespace Marketplace.Modules.Offers.Infrastructure;

/// <summary>
/// Retire périodiquement les remises échues (date de fin dépassée) : le prix vendeur
/// d'origine est restauré, le prix barré disparaît. Sans ce service, une promo « jusqu'au
/// X » resterait active indéfiniment.
///
/// Tourne dans l'hôte API (un seul). En multi-instances, prévoir un verrou (SKIP LOCKED) ;
/// l'opération reste idempotente (RemoveDiscount ne fait rien s'il n'y a plus de remise).
/// </summary>
public sealed class DiscountExpiryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiscountExpiryService> _logger;

    public DiscountExpiryService(IServiceScopeFactory scopeFactory, ILogger<DiscountExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var result = await sender.Send(new ExpireOfferDiscountsCommand(BatchSize), stoppingToken);
                if (result.IsSuccess && result.Value > 0)
                {
                    _logger.LogInformation("Remises échues : {Count} retirée(s).", result.Value);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expiration des remises : échec du cycle.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
