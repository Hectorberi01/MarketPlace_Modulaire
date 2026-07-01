using System.Text.Json;
using MediatR;
using Marketplace.Modules.Marketing.Application.Content;
using Marketplace.Modules.Marketing.Contracts;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>
/// BFF Mobile — contenu éditorial (CMS). Sert les blocs publiés d'un slot, par ex.
/// la FAQ (slot « help_faq »). Public : la FAQ doit être consultable même hors session.
/// </summary>
public static class MobileContentEndpoints
{
    private const string FaqSlot = "help_faq";

    public static IEndpointRouteBuilder MapMobileContentEndpoints(this IEndpointRouteBuilder app)
    {
        var content = app.MapGroup("/mobile/content").WithTags("Mobile · Contenu").AllowAnonymous();

        content.MapGet("/{slot}", GetBySlotAsync);

        return app;
    }

    /// <summary>
    /// Renvoie les entrées d'un slot sous forme normalisée { question, answer }.
    /// Si le CMS ne contient encore aucun bloc publié pour la FAQ, on renvoie une
    /// FAQ par défaut (l'admin peut la surcharger via le back-office CMS).
    /// </summary>
    private static async Task<IResult> GetBySlotAsync(string slot, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListContentBySlotQuery(slot), ct);

        var items = result.IsSuccess
            ? result.Value.Select(ToFaqItem).Where(i => i is not null).Select(i => i!).ToList()
            : new List<FaqItem>();

        if (items.Count == 0 && string.Equals(slot, FaqSlot, StringComparison.OrdinalIgnoreCase))
        {
            items = DefaultFaq();
        }

        return Results.Ok(items);
    }

    /// <summary>Projette un bloc CMS en { question, answer }. Le contenu est censé
    /// être un JSON { "question": "...", "answer": "..." } ; sinon on retombe sur du texte brut.</summary>
    private static FaqItem? ToFaqItem(ContentBlockSummary block)
    {
        var raw = block.Content?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var question = root.TryGetProperty("question", out var q) ? q.GetString() : null;
                var answer = root.TryGetProperty("answer", out var a) ? a.GetString() : null;
                return new FaqItem(question ?? "Question", answer ?? string.Empty, block.Position);
            }
            catch (JsonException)
            {
                // Contenu non-JSON : on l'utilise tel quel comme réponse.
            }
        }

        return new FaqItem("Information", raw, block.Position);
    }

    private static List<FaqItem> DefaultFaq() => new()
    {
        new FaqItem(
            "Comment passer une commande ?",
            "Ajoutez des articles à votre panier, puis appuyez sur « Passer la commande ». Choisissez une adresse de livraison et un moyen de paiement (Mobile Money ou carte), puis confirmez.",
            1),
        new FaqItem(
            "Quels moyens de paiement sont acceptés ?",
            "Nous acceptons Mobile Money (MTN MoMo, Moov) et les cartes bancaires. Vous pouvez enregistrer vos moyens de paiement depuis Compte › Paiements.",
            2),
        new FaqItem(
            "Comment suivre ma commande ?",
            "Rendez-vous dans l'onglet « Commandes ». Chaque commande affiche son statut en temps réel, de la préparation à la livraison.",
            3),
        new FaqItem(
            "Quand serai-je débité ?",
            "Le paiement est initié à la validation de la commande. Pour Mobile Money, vous recevez une demande de confirmation sur votre téléphone.",
            4),
        new FaqItem(
            "Comment contacter un vendeur ?",
            "Sur la page d'un produit ou d'une boutique, appuyez sur « Contacter ». Vous pouvez échanger en direct via la messagerie.",
            5),
        new FaqItem(
            "Comment retourner un article ?",
            "Depuis le détail d'une commande livrée, ouvrez une demande de retour. Le vendeur vous répondra avec la marche à suivre.",
            6),
        new FaqItem(
            "Mes données de paiement sont-elles sécurisées ?",
            "Oui. Nous ne stockons jamais le numéro complet de votre carte ni votre code de sécurité : seuls la marque, les quatre derniers chiffres et l'expiration sont conservés.",
            7),
    };

    private sealed record FaqItem(string Question, string Answer, int Position);
}
