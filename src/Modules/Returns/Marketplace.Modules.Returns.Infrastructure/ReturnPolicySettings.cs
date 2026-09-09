using System.Globalization;
using Microsoft.Extensions.Configuration;
using Marketplace.Modules.Returns.Application.Abstractions;

namespace Marketplace.Modules.Returns.Infrastructure;

/// <summary>
/// Lit la fenêtre de retour dans la configuration : « Returns:WindowDays ».
///
/// SEPT JOURS PAR DÉFAUT. Une valeur absente ne doit pas produire une fenêtre nulle —
/// ce qui interdirait tout retour — ni une fenêtre infinie — ce qui rendrait une
/// commande de l'an dernier remboursable. Elle produit la règle courante.
///
/// UNE VALEUR ABERRANTE EST TRAITÉE COMME ABSENTE : zéro, négative, ou impossible à
/// lire. Un délai de retour qui vaut zéro parce que quelqu'un a mal tapé une variable
/// d'environnement serait un incident commercial parfaitement silencieux — tous les
/// retours refusés, aucune erreur nulle part, et le service client découvrant le
/// problème par les réclamations.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// LECTURE PAR L'INDEXEUR, PAS PAR `GetValue&lt;T&gt;`. Ce n'est pas une préférence de
/// style : `GetValue` est une extension du paquet Microsoft.Extensions.Configuration
/// .Binder, que ce module ne référence pas — il n'a que `.Abstractions`. L'indexeur,
/// lui, est sur l'interface. Ajouter le paquet aurait marché aussi ; analyser la
/// chaîne nous-mêmes évite une dépendance pour une seule ligne, et rend explicite le
/// traitement de la valeur illisible.
/// ─────────────────────────────────────────────────────────────────────────────────
/// </summary>
internal sealed class ReturnPolicySettings : IReturnPolicySettings
{
    private const int DefaultWindowDays = 7;

    private readonly int _windowDays;

    public ReturnPolicySettings(IConfiguration configuration)
    {
        var raw = configuration["Returns:WindowDays"];

        _windowDays = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days > 0
            ? days
            : DefaultWindowDays;
    }

    public TimeSpan WindowAfterDelivery => TimeSpan.FromDays(_windowDays);
}
