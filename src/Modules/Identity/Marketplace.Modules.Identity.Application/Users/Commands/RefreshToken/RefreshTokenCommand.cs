using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Identity.Application.Models;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RefreshToken;

/// <summary>Échange un refresh token valide contre une nouvelle paire de jetons (rotation).</summary>
public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthTokens>;
