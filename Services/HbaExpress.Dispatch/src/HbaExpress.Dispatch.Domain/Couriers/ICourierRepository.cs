namespace HbaExpress.Dispatch.Domain.Couriers;

public interface ICourierRepository
{
    Task AddAsync(Courier courier, CancellationToken cancellationToken = default);

    Task<Courier?> GetByIdAsync(CourierId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Coursiers candidats à une offre : actifs, en ligne, desservant cette commune.
    ///
    /// Le filtre grossier est fait EN BASE — inutile de charger toute la flotte pour
    /// n'en garder que cinq. L'affinage par distance reste au domaine
    /// (<c>CourierMatcher</c>), là où il se teste sans base de données.
    /// </summary>
    Task<IReadOnlyList<Courier>> ListCandidatesAsync(
        string communeCode, CancellationToken cancellationToken = default);
}
