namespace Marketplace.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Frontière transactionnelle d'un module. SaveChanges persiste l'agrégat ET
/// dispatche ses domain events. En monolithe modulaire, plusieurs étapes
/// peuvent partager une transaction ; après extraction, chacune devient un pas
/// de Saga — le code métier ne change pas.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
