using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Identity.Application.Models;

namespace Marketplace.Modules.Identity.Application.Users.Commands.Mfa;

/// <summary>Initie l'activation MFA : génère un secret TOTP et l'URI otpauth (QR code).</summary>
public sealed record BeginMfaSetupCommand(Guid UserId) : ICommand<MfaSetupResponse>;
