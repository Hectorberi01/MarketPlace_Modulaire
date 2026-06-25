using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Identity.Contracts;

namespace Marketplace.Modules.Identity.Application.Users.Queries.GetUser;

/// <summary>Récupère le profil public d'un compte.</summary>
public sealed record GetUserQuery(Guid UserId) : IQuery<UserSummary>;
