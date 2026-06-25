using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.BuildingBlocks.Infrastructure.Modularity;

/// <summary>
/// Point d'entrée d'enregistrement d'un module dans le conteneur. Chaque module
/// fournit son installer (DbContext, handlers, repositories, outbox…). Le
/// Bootstrap découvre et exécute tous les installers — c'est le seul endroit qui
/// « connaît » l'ensemble des modules, et il ne dépend que de leurs Contracts +
/// Infrastructure d'enregistrement, jamais de leurs internes.
/// </summary>
public interface IModuleInstaller
{
    string ModuleName { get; }

    /// <summary>Assembly Application du module, scannée par MediatR (handlers).</summary>
    Assembly ApplicationAssembly { get; }

    void Install(IServiceCollection services, IConfiguration configuration);
}
