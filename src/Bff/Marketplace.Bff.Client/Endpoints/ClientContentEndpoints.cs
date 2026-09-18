using System.Text.Json;
using MediatR;
using Marketplace.Modules.Marketing.Application.Content;
using Marketplace.Modules.Marketing.Contracts;

namespace Marketplace.Bff.Client.Endpoints;

/// <summary>
/// BFF Client — contenu éditorial (CMS). Sert les blocs publiés d'un slot, par ex.
/// la FAQ (slot « help_faq »). Public : la FAQ doit être consultable même hors session.
/// </summary>
public static class ClientContentEndpoints
{
    private const string FaqSlot = "help_faq";

    public static IEndpointRouteBuilder MapClientContentEndpoints(this IEndpointRouteBuilder app)
    {
        var content = app.MapGroup("/client/content").WithTags("Client · Contenu").AllowAnonymous();

        content.MapGet("/{slot}", GetBySlotAsync);

        // ═════════════════════════════════════════════════════════════════════════
        // LES TEXTES LÉGAUX ONT LEUR PROPRE ROUTE, ET CE N'EST PAS UN DOUBLON.
        //
        // `/content/{slot}` normalise TOUT en paires { question, réponse } et
        // fabrique une FAQ par défaut quand le slot est vide. Servir des CGV par ce
        // chemin les découperait en questions-réponses et — le point décisif — ne
        // transporterait AUCUNE version.
        //
        // Or la version est ce que le serveur enregistre à l'acceptation. Un texte
        // servi sans elle donnerait un consentement qui ne désigne rien.
        // ═════════════════════════════════════════════════════════════════════════
        var legal = app.MapGroup("/client/legal").WithTags("Client · Légal").AllowAnonymous();

        legal.MapGet("/{document}", GetLegalDocumentAsync);

        return app;
    }

    /// <summary>
    /// Les documents servis, et la correspondance avec leur emplacement CMS.
    ///
    /// LISTE FERMÉE. Sans elle, `/client/legal/help_faq` rendrait la FAQ sous une
    /// forme légale, et n'importe quel slot du CMS deviendrait lisible sous un nom
    /// qui promet un document contractuel.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LegalSlots =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["terms"] = "legal_terms_customer",
            ["privacy"] = "legal_privacy_customer",
        };

    /// <summary>
    /// Un document légal AVEC SA VERSION, tel que l'application doit l'afficher et
    /// le faire accepter.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA VERSION ET LE TEXTE VOYAGENT DANS LE MÊME BLOC, ET C'EST TOUT L'INTÉRÊT.
    ///
    /// Avant cette route, chaque application embarquait le texte ET décidait de la
    /// version qu'elle faisait enregistrer. Le serveur notait « a accepté
    /// 2026-07-12 » sans savoir quelle rédaction avait été affichée : deux binaires
    /// portant des textes différents pouvaient produire le même enregistrement, et
    /// une application ancienne ne redemandait JAMAIS l'accord, puisqu'elle ignorait
    /// que le texte avait changé.
    ///
    /// Ici, la version est un champ du document. Elle ne peut pas désigner un autre
    /// texte que celui qui l'accompagne, parce qu'ils sont le même enregistrement.
    /// ═════════════════════════════════════════════════════════════════════════════
    ///
    /// 404 QUAND LE CMS N'A RIEN. Pas un document vide, pas un texte par défaut :
    /// l'application retombe alors sur la copie qu'elle embarque, et fait accepter
    /// LA VERSION DE CETTE COPIE — l'utilisateur accepte le texte qu'il a lu.
    /// Rendre un document vide lui ferait accepter une page blanche.
    private static async Task<IResult> GetLegalDocumentAsync(
        string document, ISender sender, CancellationToken ct)
    {
        if (!LegalSlots.TryGetValue(document ?? string.Empty, out var slot))
        {
            return Results.NotFound(new { error = "unknown_document" });
        }

        var result = await sender.Send(new ListContentBySlotQuery(slot), ct);
        if (result.IsFailure)
        {
            return Results.NotFound(new { error = "document_unavailable" });
        }

        // UN SEUL BLOC PAR DOCUMENT, et le premier par position fait foi. Un document
        // légal n'est pas une liste de fragments qu'on assemble : le découper en
        // blocs rendrait possible d'en publier la moitié.
        var bloc = result.Value.OrderBy(b => b.Position).FirstOrDefault();
        if (bloc is null || string.IsNullOrWhiteSpace(bloc.Content))
        {
            return Results.NotFound(new { error = "document_unavailable" });
        }

        LegalDocumentPayload? charge;
        try
        {
            charge = JsonSerializer.Deserialize<LegalDocumentPayload>(
                bloc.Content, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            // Bloc mal formé : on ne sert PAS un document partiel. L'application
            // retombe sur sa copie embarquée, qui est au moins cohérente.
            return Results.NotFound(new { error = "document_malformed" });
        }

        if (charge is null
            || string.IsNullOrWhiteSpace(charge.Version)
            || charge.Sections is not { Count: > 0 })
        {
            return Results.NotFound(new { error = "document_malformed" });
        }

        return Results.Ok(new
        {
            document,
            version = charge.Version,
            lastUpdated = charge.LastUpdated,
            company = charge.Company,
            sections = charge.Sections.Select(s => new
            {
                title = s.Title,
                body = s.Body ?? new List<string>(),
            }),
        });
    }

    /// <summary>
    /// La forme attendue dans <c>ContentBlock.Content</c> pour un document légal.
    ///
    /// C'est du JSON saisi dans le CMS, donc NON VÉRIFIÉ par un schéma : le handler
    /// refuse ce qui n'a ni version ni sections, et l'application retombe sur sa
    /// copie plutôt que d'afficher un document tronqué.
    /// </summary>
    private sealed record LegalDocumentPayload(
        string? Version,
        string? LastUpdated,
        string? Company,
        List<LegalSectionPayload>? Sections);

    private sealed record LegalSectionPayload(string? Title, List<string>? Body);

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
