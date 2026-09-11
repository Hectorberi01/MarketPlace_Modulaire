using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.Roles.Events;

namespace Marketplace.Modules.Identity.Domain.Roles;

/// <summary>
/// Définit ce qu'un acteur a le droit de faire : base de l'autorisation. Porte
/// un ensemble de permissions granulaires (cf. dossier, Role / Permission).
/// Les permissions sont stockées sous forme de codes validés par le VO Permission.
/// </summary>
public sealed class Role : AggregateRoot<RoleId>
{
    private List<string> _permissions = new();

    private Role()
    {
    }

    private Role(RoleId id, string name, string? description, bool isSystem, IEnumerable<string> permissions)
        : base(id)
    {
        Name = name;
        Description = description;
        IsSystem = isSystem;
        _permissions.AddRange(permissions);

        Raise(new RoleCreatedDomainEvent(id.Value, name));
    }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>Rôle système (Buyer, Seller, Admin…) : non supprimable.</summary>
    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<string> Permissions => _permissions.AsReadOnly();

    public static Result<Role> Create(
        string name,
        string? description = null,
        bool isSystem = false,
        IEnumerable<string>? permissions = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("identity.role.name_required", "Le nom du rôle est obligatoire.");
        }

        var codes = new List<string>();
        foreach (var permission in permissions ?? Enumerable.Empty<string>())
        {
            var result = Permission.Create(permission);
            if (result.IsFailure)
            {
                return Result.Failure<Role>(result.Error);
            }

            if (!codes.Contains(result.Value.Value))
            {
                codes.Add(result.Value.Value);
            }
        }

        return new Role(RoleId.New(), name.Trim(), description?.Trim(), isSystem, codes);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // `AddPermission` ET `RemovePermission` SONT RETIRÉES, ET C'EST LE GARDE QUI
    // L'EXIGE.
    //
    // Elles étaient publiques, sans aucun contrôle de `IsSystem`, et personne ne les
    // appelait — vérifié sur tout le dépôt, monolithe, dispatch et tests compris. Elles
    // n'ouvraient donc qu'une chose : le contournement du garde de `SetPermissions`, une
    // permission à la fois. On peut écrire que le garde vit dans l'agrégat et qu'aucun
    // appelant futur ne peut le contourner ; on ne peut pas l'écrire en laissant à côté
    // deux portes sans serrure.
    //
    // Le besoin qu'elles couvraient — modifier les permissions d'un rôle — est servi par
    // `SetPermissions` pour un rôle ordinaire et par `SyncSystemPermissions` pour
    // l'amorçage. Les deux passent par `ApplyPermissions`, qui valide chaque code.
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Remplace l'intégralité des permissions du rôle.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// UN RÔLE SYSTÈME NE SE REDÉFINIT PAS. LE GARDE MANQUAIT ICI, ET NULLE PART
    /// AILLEURS IL N'EXISTAIT.
    ///
    /// `DeleteRoleCommandHandler` vérifiait bien `IsSystem` avant de supprimer. Ni
    /// cette méthode ni `Update` ne le vérifiaient — et la console, de son côté,
    /// masquait « Supprimer » sur un rôle système mais offrait « Éditer » à tous.
    ///
    /// Conséquence : ouvrir « Admin », décocher les permissions, enregistrer. En un
    /// geste, sans confirmation ni refus, plus personne n'a de droits
    /// d'administration — et l'on ne peut plus se redonner ces droits, puisque les
    /// rendre demande précisément d'être administrateur. « Seller » est dans le même
    /// cas, et le BFF vendeur en dépend PAR SON NOM.
    ///
    /// LE GARDE EST DANS L'AGRÉGAT, pas dans le gestionnaire : c'est la seule place
    /// qu'aucun appelant futur ne peut contourner. L'amorçage, lui, passe par
    /// `Create` et n'est donc pas concerné.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result SetPermissions(IEnumerable<string> permissions)
    {
        if (IsSystem)
        {
            return Result.Failure(Error.Conflict(
                "identity.role.system_protected",
                $"Les permissions du rôle système « {Name} » ne peuvent pas être modifiées. "
                + "Créez un rôle dédié si vous avez besoin d'un autre jeu de droits."));
        }

        return ApplyPermissions(permissions);
    }

    /// <summary>
    /// Valide chaque code puis remplace la liste. Partagé par le chemin console et le
    /// chemin amorçage : une seule règle de format, donc aucune chance qu'ils divergent.
    /// </summary>
    private Result ApplyPermissions(IEnumerable<string> permissions)
    {
        var codes = new List<string>();
        foreach (var permission in permissions)
        {
            var result = Permission.Create(permission);
            if (result.IsFailure)
            {
                return Result.Failure(result.Error);
            }

            if (!codes.Contains(result.Value.Value))
            {
                codes.Add(result.Value.Value);
            }
        }

        _permissions.Clear();
        _permissions.AddRange(codes);
        return Result.Success();
    }

    /// <summary>
    /// Réaligne les permissions d'un rôle SYSTÈME sur ce que le code déclare.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA SEULE PORTE LAISSÉE OUVERTE, ET ELLE N'EST PAS POUR LA CONSOLE.
    ///
    /// Le garde de <see cref="SetPermissions"/> protège d'un geste catastrophique —
    /// décocher les permissions d'« Admin » et ne plus pouvoir se les rendre. Mais
    /// l'amorçage, lui, SAUTE les rôles déjà présents : sans cette méthode, ajouter
    /// une permission à « Admin » dans le code ne l'atteindrait jamais sur une base
    /// existante, et plus aucun chemin ne permettrait de le faire. Le garde aurait
    /// muré la dernière issue.
    ///
    /// Elle ne s'applique QU'AUX RÔLES SYSTÈME, précisément parce qu'ils sont définis
    /// dans le code : leur source de vérité est le dépôt, pas la base. Un rôle créé à
    /// la main, lui, appartient à qui l'a créé — et cette méthode le refuse.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result SyncSystemPermissions(IEnumerable<string> permissions)
    {
        if (!IsSystem)
        {
            return Result.Failure(Error.Conflict(
                "identity.role.not_system",
                $"« {Name} » n'est pas un rôle système : ses permissions se règlent depuis la console."));
        }

        return ApplyPermissions(permissions);
    }

    /// <summary>
    /// Renomme le rôle. REFUSÉ SUR UN RÔLE SYSTÈME — même raison que
    /// <see cref="SetPermissions"/>, et une de plus : le code résout certains rôles par
    /// leur NOM, « Seller » notamment. Renommer briserait ces résolutions en silence,
    /// et l'erreur n'apparaîtrait qu'à la prochaine création de boutique.
    /// </summary>
    public Result Update(string name, string? description)
    {
        if (IsSystem)
        {
            return Result.Failure(Error.Conflict(
                "identity.role.system_protected",
                $"Le rôle système « {Name} » ne peut pas être renommé : le code le résout par son nom."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("identity.role.name_required", "Le nom du rôle est obligatoire."));
        }

        Name = name.Trim();
        Description = description?.Trim();
        return Result.Success();
    }
}
