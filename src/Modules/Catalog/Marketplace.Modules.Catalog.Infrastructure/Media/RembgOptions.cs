namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Configuration du détourage LOCAL, via un service rembg auto-hébergé.
/// Section de config : « Media:Rembg ».
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// POURQUOI UNE ALTERNATIVE À CLOUDINARY
///
/// Cloudinary ne sert qu'au TRAITEMENT : l'image finale part sur Cloudflare R2, et
/// l'asset distant est détruit dans la foulée. Rien n'oblige donc ce traitement à
/// sortir de l'infrastructure. rembg fait le même travail dans un conteneur voisin,
/// sans quota, sans facture à l'usage, et sans que les photos des vendeurs quittent
/// le serveur.
///
/// LE MODÈLE ENGAGE JURIDIQUEMENT, PAS LA BIBLIOTHÈQUE.
///
/// rembg est sous licence MIT, mais il embarque des modèles aux licences
/// hétérogènes. Ceux de la famille BRIA (« bria-rmbg ») sont en CC BY-NC 4.0 :
/// USAGE NON COMMERCIAL. Les servir depuis une place de marché serait une infraction,
/// et rien dans l'API ne le signale — le modèle se choisit par une simple chaîne.
///
/// D'où <see cref="Model"/> par défaut sur « u2net » (dépôt d'origine sous Apache 2.0,
/// dérivés sous MIT) et le garde-fou de <see cref="EffectiveModel"/>, qui n'envoie au
/// service QUE des modèles d'une liste blanche vérifiée.
/// ─────────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class RembgOptions
{
    /// <summary>
    /// URL de base du service, réseau interne uniquement (ex. « http://rembg:7000 »).
    ///
    /// Le serveur rembg n'a NI authentification NI limitation de débit. Il ne doit
    /// jamais être publié : pas de port exposé, pas de route Traefik.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Modèle de segmentation. Voir l'avertissement de licence ci-dessus.</summary>
    public string Model { get; set; } = "u2net";

    /// <summary>
    /// Délai maximal d'un détourage. Une inférence u2net sur deux cœurs se compte en
    /// secondes ; la marge couvre la mise en session du modèle au tout premier appel
    /// et les pointes de charge, sans laisser une requête pendre indéfiniment.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Qualité de réencodage JPEG (1-100). 88 : le réglage de l'app mobile à la prise
    /// de vue, cohérent d'un bout à l'autre de la chaîne.
    /// </summary>
    public int JpegQuality { get; set; } = 88;

    /// <summary>
    /// Modèles AUTORISÉS — liste blanche, pas liste noire.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// La première version listait les modèles interdits. Deux défauts, tous deux
    /// vérifiés depuis :
    ///  • elle contenait « birefnet-rmbg », qui N'EXISTE PAS dans rembg — une entrée
    ///    fantôme qui ne protégeait de rien tout en donnant l'impression du contraire ;
    ///  • elle ratait les cas réels. rembg expose une quinzaine de sessions
    ///    (isnet-anime, birefnet-cod, birefnet-dis, sam…) dont plusieurs reposent sur
    ///    des jeux de données aux conditions d'usage distinctes de la licence du code.
    ///    Une liste noire protège de ce qu'on a pensé à écrire ; sur une question de
    ///    licence, c'est exactement l'inverse de ce qu'il faut.
    ///
    /// Ne figurent donc ici que les modèles de la famille U²-Net, dont le dépôt
    /// d'origine est sous Apache 2.0 et les dérivés sous MIT. Ajouter une entrée
    /// suppose d'avoir lu la licence des POIDS — pas seulement celle de rembg (MIT).
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static readonly string[] AllowedModels =
    [
        "u2net",
        "u2netp",
        "u2net_human_seg",
        "silueta",
    ];

    /// <summary>
    /// Modèle réellement envoyé au service.
    ///
    /// Une valeur hors liste blanche retombe sur « u2net » AU LIEU de désactiver la
    /// fonction : une faute de frappe dans la configuration ne doit pas priver
    /// silencieusement tous les vendeurs du détourage. Le repli est sûr par
    /// construction — u2net est le modèle par défaut et sa licence est vérifiée.
    /// </summary>
    public string EffectiveModel
    {
        get
        {
            var candidate = (Model ?? string.Empty).Trim().ToLowerInvariant();
            return AllowedModels.Contains(candidate) ? candidate : "u2net";
        }
    }

    /// <summary>Vrai si un service de détourage local est adressable.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
