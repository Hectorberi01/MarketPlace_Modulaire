using System.Security.Cryptography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PREUVE DE REMISE — TROIS VALEURS QUI NE PROUVAIENT RIEN.
///
/// <c>ProofOfDeliveryKind</c> proposait Pin, Photo et Signature. Derrière, un seul
/// <c>string? ProofValue</c> que le domaine se contentait de nettoyer. Concrètement :
///
///   • PIN : aucun code n'était jamais ÉMIS, et aucun n'était COMPARÉ. Le livreur
///     tapait « 1 » et la course exigeant un code se fermait ;
///   • PHOTO et SIGNATURE : rien n'était téléversé ni conservé. La « preuve » était
///     le texte que le livreur voulait bien écrire.
///
/// Les trois donnaient donc exactement la même garantie — aucune — tout en
/// affichant au client qu'une remise était vérifiée. C'est pire qu'une absence de
/// preuve : c'est une absence de preuve qui se présente comme une preuve.
///
/// CE QUE CE TYPE CHANGE
///
/// La valeur attendue dépend maintenant du genre, et chaque genre est vérifiable :
/// un PIN est comparé à un code émis à la création ; une photo ou une signature est
/// une RÉFÉRENCE DE STOCKAGE, produite par un téléversement, pas une chaîne libre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProofOfDelivery : ValueObject
{
    /// <summary>Longueur du code remis au destinataire. Quatre chiffres : ce qu'on retient sans le noter.</summary>
    public const int PinLength = 4;

    private ProofOfDelivery(ProofOfDeliveryKind kind, string value, DateTime capturedAtUtc)
    {
        Kind = kind;
        Value = value;
        CapturedAtUtc = capturedAtUtc;
    }

    // Requis par EF Core.
    private ProofOfDelivery()
    {
        Value = string.Empty;
    }

    public ProofOfDeliveryKind Kind { get; private init; }

    /// <summary>
    /// Le code saisi, ou la référence du fichier téléversé. Jamais du texte libre.
    /// </summary>
    public string Value { get; private init; }

    public DateTime CapturedAtUtc { get; private init; }

    /// <summary>
    /// Émet un code à quatre chiffres.
    ///
    /// ⚠️ <see cref="RandomNumberGenerator"/> et non <see cref="Random"/>. Un PIN
    /// prévisible ne vaut pas mieux que pas de PIN : avec <c>Random</c>, deux courses
    /// créées dans la même milliseconde partageraient leur code, et la suite entière
    /// se rejoue à partir d'une seule valeur observée.
    ///
    /// Quatre chiffres, c'est dix mille possibilités — faible en absolu, mais le
    /// code est à usage unique, il expire avec la course, et le livreur n'a qu'une
    /// poignée de tentatives avant que le client n'appelle le support. La menace
    /// n'est pas la force brute, c'est la prévisibilité.
    /// </summary>
    public static string IssuePin()
        => RandomNumberGenerator.GetInt32(0, 10_000).ToString("D" + PinLength);

    /// <summary>
    /// Enregistre une preuve. <paramref name="expectedPin"/> n'est utilisé que pour
    /// le genre Pin ; il est comparé à temps constant.
    /// </summary>
    public static Result<ProofOfDelivery> Capture(
        ProofOfDeliveryKind kind, string? value, string? expectedPin, DateTime atUtc)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        if (kind is ProofOfDeliveryKind.None)
        {
            return Result.Failure<ProofOfDelivery>(
                Error.Validation("delivery.proof.not_required", "Cette course n'exige aucune preuve de remise."));
        }

        if (trimmed is null)
        {
            return Result.Failure<ProofOfDelivery>(
                Error.Validation("delivery.proof.missing", "Une preuve de remise est requise pour cette course."));
        }

        return kind switch
        {
            ProofOfDeliveryKind.Pin => CapturePin(trimmed, expectedPin, atUtc),
            ProofOfDeliveryKind.Photo or ProofOfDeliveryKind.Signature => CaptureFile(kind, trimmed, atUtc),

            // Pas de « _ => une preuve quelconque ». Un genre ajouté à
            // l'énumération sans être traité ici doit CASSER — c'est exactement
            // l'oubli qui a rendu Photo et Signature décoratifs pendant des mois.
            _ => Result.Failure<ProofOfDelivery>(
                Error.Validation("delivery.proof.kind_unsupported",
                    $"Le genre de preuve « {kind} » n'a pas de mode de capture défini."))
        };
    }

    private static Result<ProofOfDelivery> CapturePin(string submitted, string? expectedPin, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(expectedPin))
        {
            // La course exige un PIN mais aucun n'a été émis : c'est un défaut de
            // création, pas une erreur du livreur. On refuse plutôt que d'accepter
            // n'importe quoi — accepter reviendrait à revenir au comportement
            // qu'on est en train de corriger.
            return Result.Failure<ProofOfDelivery>(
                Error.Conflict("delivery.proof.pin_not_issued",
                    "Aucun code n'a été émis pour cette course : contactez le support."));
        }

        // Comparaison à TEMPS CONSTANT, comme pour une clé d'API. Sur quatre
        // chiffres l'écart de temps est infime, mais la règle ne souffre pas
        // d'exception : c'est en la relâchant « quand ça n'a pas d'importance »
        // qu'on la relâche ensuite là où elle en a.
        var expected = System.Text.Encoding.UTF8.GetBytes(expectedPin);
        var actual = System.Text.Encoding.UTF8.GetBytes(submitted);

        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return Result.Failure<ProofOfDelivery>(
                Error.Validation("delivery.proof.pin_mismatch",
                    "Le code saisi ne correspond pas à celui remis au destinataire."));
        }

        return new ProofOfDelivery(ProofOfDeliveryKind.Pin, submitted, atUtc);
    }

    /// <summary>
    /// Photo et signature : la valeur doit être une RÉFÉRENCE DE STOCKAGE, pas du
    /// texte. On ne peut pas vérifier ici que le fichier existe — le domaine ne
    /// connaît pas le stockage — mais on peut refuser tout ce qui n'en a pas la
    /// forme, ce qui écarte le cas qui nous occupe : le livreur qui tape « ok ».
    /// </summary>
    private static Result<ProofOfDelivery> CaptureFile(ProofOfDeliveryKind kind, string reference, DateTime atUtc)
    {
        var looksLikeReference =
            reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || reference.Contains('/', StringComparison.Ordinal);

        if (!looksLikeReference || reference.Length < 8)
        {
            return Result.Failure<ProofOfDelivery>(
                Error.Validation("delivery.proof.file_required",
                    kind is ProofOfDeliveryKind.Photo
                        ? "Une photo du colis remis est requise : téléversez-la d'abord, puis transmettez sa référence."
                        : "Une signature du destinataire est requise : téléversez-la d'abord, puis transmettez sa référence."));
        }

        return new ProofOfDelivery(kind, reference, atUtc);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Kind;
        yield return Value;
        yield return CapturedAtUtc;
    }
}
