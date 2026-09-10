using HbaExpress.Dispatch.Application.Couriers;
using HbaExpress.Dispatch.Application.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HbaExpress.Dispatch.Application;

/// <summary>
/// Enregistre les services applicatifs du dispatch.
///
/// PAS DE SCAN D'ASSEMBLAGE, ET C'EST VOLONTAIRE. Ce service compte cinq services
/// applicatifs ; les nommer un par un tient en dix lignes, se lit d'un coup d'oeil, et
/// ne cache rien. Un scan par convention économiserait ces dix lignes au prix de
/// l'évidence — on ne saurait plus, en lisant, ce que le conteneur contient.
/// </summary>
public static class DispatchApplicationServices
{
    public static IServiceCollection AddDispatchApplication(this IServiceCollection services)
    {
        // Horloge injectée plutôt que DateTime.UtcNow : c'est ce qui rend l'expiration
        // des offres et le delai global testables sans attendre vingt-cinq secondes.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<DeliveryJobService>();
        services.AddScoped<RedispatchService>();
        services.AddScoped<JobProgressService>();
        services.AddScoped<CourierService>();

        // Lecture seule, enregistrée à part de CourierService pour la même raison qu'elle
        // vit dans un autre fichier : rien de ce qui répond à « où sont les coursiers » ne
        // doit pouvoir en déplacer un.
        services.AddScoped<FleetQuery>();

        return services;
    }
}
