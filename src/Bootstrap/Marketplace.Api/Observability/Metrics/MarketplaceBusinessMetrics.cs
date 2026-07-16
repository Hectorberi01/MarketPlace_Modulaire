using System.Diagnostics.Metrics;
using Marketplace.BuildingBlocks.Application.Observability;

namespace Marketplace.Api.Observability.Metrics;

/// <summary>
/// Métriques métier de la marketplace (meter <see cref="MarketplaceMeters.Business"/>).
///
/// Deux familles :
/// <list type="bullet">
///   <item><b>Compteurs</b> (événementiels, cumulatifs) : inscriptions, commandes,
///         paniers, CA, commissions. Émis depuis les handlers d'événements de domaine.</item>
///   <item><b>Jauges observables</b> (état courant) : utilisateurs/vendeurs actifs,
///         produits disponibles. Alimentées périodiquement via <see cref="SetGauge"/>
///         (un publisher lit la base et pousse les valeurs).</item>
/// </list>
///
/// Montants financiers : compteurs en plus petite unité monétaire, ventilés par
/// <c>currency</c> (jamais d'addition inter-devises — EUR, XOF, USD restent séparés).
/// Les taux (conversion, annulation, panier moyen) se calculent en PromQL à partir
/// de ces compteurs (voir la doc), pas côté application.
/// </summary>
public sealed class MarketplaceBusinessMetrics : IMarketplaceBusinessMetrics, IDisposable
{
    private readonly Meter _meter;

    private readonly Counter<long> _usersRegistered;
    private readonly Counter<long> _productsCreated;
    private readonly Counter<long> _cartsCreated;
    private readonly Counter<long> _cartsAbandoned;
    private readonly Counter<long> _ordersCreated;
    private readonly Counter<long> _ordersCompleted;
    private readonly Counter<long> _ordersCancelled;
    private readonly Counter<long> _revenueMinor;
    private readonly Counter<long> _commissionMinor;

    private readonly Dictionary<string, long> _gauges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["users_active"] = 0,
        ["vendors_active"] = 0,
        ["products_available"] = 0,
    };
    private readonly object _sync = new();

    public MarketplaceBusinessMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MarketplaceMeters.Business);

        _usersRegistered = _meter.CreateCounter<long>("users_registered", description: "Nouvelles inscriptions utilisateurs.");
        _productsCreated = _meter.CreateCounter<long>("products_created", description: "Produits créés.");
        _cartsCreated = _meter.CreateCounter<long>("carts_created", description: "Paniers créés.");
        _cartsAbandoned = _meter.CreateCounter<long>("carts_abandoned", description: "Paniers abandonnés.");
        _ordersCreated = _meter.CreateCounter<long>("orders_created", description: "Commandes créées.");
        _ordersCompleted = _meter.CreateCounter<long>("orders_completed", description: "Commandes payées / finalisées.");
        _ordersCancelled = _meter.CreateCounter<long>("orders_cancelled", description: "Commandes annulées.");

        _revenueMinor = _meter.CreateCounter<long>(
            "marketplace_revenue",
            unit: "{minor_unit}",
            description: "Chiffre d'affaires (commandes payées), plus petite unité monétaire, par devise.");
        _commissionMinor = _meter.CreateCounter<long>(
            "platform_commission",
            unit: "{minor_unit}",
            description: "Commissions de la plateforme, plus petite unité monétaire, par devise.");

        // Jauges d'état courant (label « metric » implicite via le nom de série).
        _meter.CreateObservableGauge("users_active", () => Observe("users_active"), description: "Utilisateurs actifs (fenêtre glissante).");
        _meter.CreateObservableGauge("vendors_active", () => Observe("vendors_active"), description: "Vendeurs actifs.");
        _meter.CreateObservableGauge("products_available", () => Observe("products_available"), description: "Produits disponibles à la vente.");
    }

    public void UserRegistered() => _usersRegistered.Add(1);
    public void ProductCreated() => _productsCreated.Add(1);
    public void CartCreated() => _cartsCreated.Add(1);
    public void CartAbandoned() => _cartsAbandoned.Add(1);
    public void OrderCreated() => _ordersCreated.Add(1);
    public void OrderCancelled() => _ordersCancelled.Add(1);

    /// <summary>
    /// Une commande est payée/finalisée : incrémente orders_completed, et — si fournis —
    /// le CA et la commission (en plus petite unité monétaire), ventilés par devise.
    /// </summary>
    public void OrderCompleted(string currency, long revenueMinorUnits = 0, long commissionMinorUnits = 0)
    {
        _ordersCompleted.Add(1);
        var currencyTag = new KeyValuePair<string, object?>("currency", currency);
        if (revenueMinorUnits > 0)
        {
            _revenueMinor.Add(revenueMinorUnits, currencyTag);
        }

        if (commissionMinorUnits > 0)
        {
            _commissionMinor.Add(commissionMinorUnits, currencyTag);
        }
    }

    /// <summary>Met à jour une jauge d'état (users_active, vendors_active, products_available).</summary>
    public void SetGauge(string name, long value)
    {
        lock (_sync)
        {
            _gauges[name] = value;
        }
    }

    private long Observe(string name)
    {
        lock (_sync)
        {
            return _gauges.TryGetValue(name, out var value) ? value : 0;
        }
    }

    public void Dispose() => _meter.Dispose();
}
