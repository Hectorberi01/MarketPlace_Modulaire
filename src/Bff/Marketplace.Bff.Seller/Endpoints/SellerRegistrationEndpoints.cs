using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;
using Marketplace.Modules.Identity.Application.Users.Commands.ApproveUser;
using Marketplace.Modules.Identity.Application.Users.Commands.AssignRole;
using Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;
using Marketplace.Modules.Identity.Application.Users.Commands.RequestEmailVerification;
using Marketplace.Modules.Identity.Application.Users.Commands.VerifyEmailCode;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — AUTO-INSCRIPTION depuis l'app (/seller/auth/register + /verify).
///
/// Le vendeur crée lui-même son compte. Deux cas, un seul parcours côté app :
///
///  • E-mail inconnu → on crée le compte (rôle Buyer, PendingVerification) et on
///    envoie un CODE à 6 chiffres.
///  • E-mail déjà connu (un acheteur qui se lance) → on n'en recrée pas ; on émet
///    un code pour PROUVER qu'il relève bien cette boîte avant de lui rattacher une
///    boutique. Sans cette preuve, n'importe qui rattacherait une boutique au compte
///    d'autrui.
///
/// La boutique n'est créée qu'à l'étape /verify, une fois le code validé — car
/// l'onboarding vendeur EXIGE un e-mail vérifié. À la validation, le compte est
/// activé (il doit pouvoir se connecter et compléter son KYB) ; la PUBLICATION DE
/// PRODUITS, elle, reste gatée par la validation admin (KYB + activation vendeur).
///
/// Non transactionnel : si une étape tardive échoue après la création de la
/// boutique, l'état partiel subsiste (même caveat que l'onboarding admin) — à
/// durcir en saga si nécessaire.
/// </summary>
public static class SellerRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapSellerRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/seller/auth").WithTags("Vendeur · Auth").RequireRateLimiting("auth");

        auth.MapPost("/register", RegisterAsync).AllowAnonymous();
        auth.MapPost("/verify", VerifyAsync).AllowAnonymous();

        return app;
    }

    /// <summary>Étape 1 : crée le compte (ou identifie l'existant) et envoie le code.</summary>
    private static async Task<IResult> RegisterAsync(
        SellerSelfRegisterRequest request,
        ISender sender,
        IIdentityModuleApi identity,
        ISellerModuleApi sellers,
        CancellationToken ct)
    {
        var existing = await identity.GetUserByEmailAsync(request.Email, ct);

        if (existing is null)
        {
            return (await sender.Send(new RegisterUserCommand(
                    request.FirstName, request.LastName, request.Email,
                    request.PhoneNumber ?? string.Empty, request.Password, CreatedByAdmin: false), ct))
                .Match(userId => Results.Ok(new SellerRegisterResponse(userId, IsNewAccount: true)));
        }

        var shop = await sellers.GetSellerByUserIdAsync(existing.Id, ct);
        if (shop is not null)
        {
            return Results.Problem(
                title: "sellers.seller.already_seller",
                detail: "Ce compte est déjà rattaché à une boutique.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return (await sender.Send(new RequestEmailVerificationCommand(existing.Id), ct))
            .Match(() => Results.Ok(new SellerRegisterResponse(existing.Id, IsNewAccount: false)));
    }

    /// <summary>Étape 2 : valide le code, crée la boutique, attribue le rôle, active le compte.</summary>
    private static async Task<IResult> VerifyAsync(
        SellerVerifyRequest request,
        ISender sender,
        CancellationToken ct)
    {
        // 1. Code (strict : preuve de possession de la boîte).
        var verified = await sender.Send(new VerifyEmailCodeCommand(request.UserId, request.Code), ct);
        if (verified.IsFailure)
        {
            return verified.Match(() => Results.Ok());
        }

        // 2. Boutique AVANT rôle/activation : c'est l'étape qui peut légitimement
        //    échouer (nom de boutique déjà pris). L'e-mail est désormais vérifié,
        //    donc RegisterSeller l'accepte.
        var metadata = request.Company?.ToDomain();
        var sellerResult = await sender.Send(
            new RegisterSellerCommand(request.UserId, request.ShopName, 0.10m, metadata), ct);
        if (sellerResult.IsFailure)
        {
            return sellerResult.Match(_ => Results.Ok());
        }

        var sellerId = sellerResult.Value;

        // 3. Rôle Seller — sans lui, impossible de se connecter à /seller/*.
        var rolesResult = await sender.Send(new ListRolesQuery(), ct);
        if (rolesResult.IsFailure)
        {
            return rolesResult.Match(_ => Results.Ok());
        }

        var sellerRole = rolesResult.Value.FirstOrDefault(
            r => r.Name.Equals("Seller", StringComparison.OrdinalIgnoreCase));
        if (sellerRole is null)
        {
            return Results.Problem(
                title: "identity.role.seller_missing",
                detail: "Le rôle « Seller » est introuvable. La base n'a pas été correctement initialisée.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var roleResult = await sender.Send(new AssignRoleCommand(request.UserId, sellerRole.Id), ct);
        if (roleResult.IsFailure)
        {
            return roleResult.Match(() => Results.Ok());
        }

        // 4. Activation du compte : l'e-mail est prouvé, le vendeur doit pouvoir
        //    entrer et compléter son KYB. La publication produit reste gatée ailleurs.
        var approveResult = await sender.Send(new ApproveUserCommand(request.UserId), ct);
        if (approveResult.IsFailure)
        {
            return approveResult.Match(() => Results.Ok());
        }

        return Results.Ok(new SellerVerifyResponse(sellerId, request.UserId));
    }

    // ── Contrats ────────────────────────────────────────────────────────────────

    /// <summary>Corps de l'étape 1. Le mot de passe est ignoré si l'e-mail existe déjà.</summary>
    public sealed record SellerSelfRegisterRequest(
        string Email,
        string Password,
        string FirstName,
        string LastName,
        string? PhoneNumber);

    /// <summary>
    /// Corps de l'étape 2. Le nom de boutique et les infos société sont fournis ici,
    /// une fois le code en main : la boutique n'existe qu'après validation.
    /// </summary>
    public sealed record SellerVerifyRequest(
        Guid UserId,
        string Code,
        string ShopName,
        SellerCompanyInfoDto? Company);

    /// <summary>Infos société déclarées (toutes optionnelles) → metadata jsonb.</summary>
    public sealed record SellerCompanyInfoDto(
        string? LegalName,
        string? Rccm,
        string? Ifu,
        string? Address,
        string? City,
        string? Activity,
        string? ManagerName,
        string? Phone)
    {
        public SellerCompanyInfo ToDomain()
            => new(LegalName, Rccm, Ifu, Address, City, Activity, ManagerName, Phone);
    }

    public sealed record SellerRegisterResponse(Guid UserId, bool IsNewAccount)
    {
        /// <summary>Toujours vrai : l'app doit ensuite demander le code puis /verify.</summary>
        public bool RequiresVerification => true;
    }

    public sealed record SellerVerifyResponse(Guid SellerId, Guid UserId);
}
