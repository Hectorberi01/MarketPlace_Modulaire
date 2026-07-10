using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.ProcessImage;

/// <summary>
/// Traite une image produit (détourage IA + fond blanc) AVANT création du produit,
/// pour permettre au vendeur de valider le rendu. Ne persiste rien : renvoie les
/// octets traités, qui seront ensuite téléversés vers R2 lors de la création.
/// </summary>
public sealed record ProcessImageCommand(
    string FileName, string ContentType, byte[] Content) : ICommand<ProcessedImage>;

internal sealed class ProcessImageCommandHandler : ICommandHandler<ProcessImageCommand, ProcessedImage>
{
    private readonly IImageProcessor _processor;

    public ProcessImageCommandHandler(IImageProcessor processor) => _processor = processor;

    public Task<Result<ProcessedImage>> Handle(ProcessImageCommand command, CancellationToken cancellationToken)
        => _processor.RemoveBackgroundWhiteAsync(
            command.FileName, command.ContentType, command.Content, cancellationToken);
}
