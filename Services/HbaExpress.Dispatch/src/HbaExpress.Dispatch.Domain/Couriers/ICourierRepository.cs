namespace HbaExpress.Dispatch.Domain.Couriers;

public interface ICourierRepository
{
    Task AddAsync(Courier courier, CancellationToken cancellationToken = default);

    Task<Courier?> GetByIdAsync(CourierId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Le coursier derrière un COMPTE UTILISATEUR — c'est ce que porte le jeton JWT.
    ///
    /// Toutes les routes de l'application coursier passent par ici : le jeton dit quel
    /// compte appelle, cette lecture dit quel coursier c'est. Sans elle, l'identifiant du
    /// jeton ne correspondrait à aucune ligne et chaque appel finirait en 404.
    /// </summary>
    Task<Courier?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Coursiers candidats à une offre : actifs, en ligne, desservant cette commune.
    ///
    /// Le filtre grossier est fait EN BASE — inutile de charger toute la flotte pour
    /// n'en garder que cinq. L'affinage par distance reste au domaine
    /// (<c>CourierMatcher</c>), là où il se teste sans base de données.
    /// </summary>
    Task<IReadOnlyList<Courier>> ListCandidatesAsync(
        string communeCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Coursiers PRÉSENTS SUR LE TERRAIN : actifs, en ligne ou en course.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA PREMIÈRE LECTURE DE POSITION DE CE SERVICE.
    ///
    /// Les positions étaient écrites toutes les quinze secondes et n'étaient lues que
    /// par le moteur d'appariement. Aucune surface — ni coursier, ni acheteur, ni
    /// administrateur — ne pouvait savoir où se trouvait la flotte. Une console qui
    /// pilote la livraison sans jamais la voir répare à l'aveugle.
    ///
    /// Les HORS LIGNE sont exclus en base, et ce n'est pas de l'économie : leur
    /// dernière position date de leur dernière connexion, et l'afficher reviendrait à
    /// dessiner sur une carte des coursiers qui ne travaillent pas.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<IReadOnlyList<Courier>> ListOnFieldAsync(CancellationToken cancellationToken = default);
}
