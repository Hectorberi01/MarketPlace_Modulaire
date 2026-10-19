using System.Net;
using System.Text;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Microsoft.Extensions.Configuration;

namespace Marketplace.Host.Client.Endpoints;

/// <summary>
/// BFF Client — LIENS UNIVERSELS (deep linking).
///
/// Trois routes, toutes anonymes, servies à la racine du domaine mobile
/// (prod https://m.hba-express.org, staging m.marketplace-staging.hba-marketplace.fr) :
///
///   • /.well-known/apple-app-site-association  → dit à iOS quelles URLs ouvrir
///     directement dans l'app (Universal Links).
///   • /.well-known/assetlinks.json             → équivalent Android (App Links).
///   • /p/{id}                                  → PAGE DE REPLI. Si l'app est
///     installée, le système l'intercepte AVANT d'afficher cette page et ouvre
///     l'app sur la fiche produit. Sinon, le navigateur affiche cette page :
///     aperçu du produit + boutons de téléchargement (App Store / Google Play).
///
/// Tout est piloté par la configuration (section « DeepLink ») : identifiants
/// d'app, empreintes de signature et liens de store se changent en appsettings,
/// sans toucher au code.
/// </summary>
public static class ClientDeepLinkEndpoints
{
    public static IEndpointRouteBuilder MapClientDeepLinkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/apple-app-site-association", AppleAppSiteAssociation)
            .WithTags("Client · DeepLink").AllowAnonymous().ExcludeFromDescription();

        app.MapGet("/.well-known/assetlinks.json", AssetLinks)
            .WithTags("Client · DeepLink").AllowAnonymous().ExcludeFromDescription();

        app.MapGet("/p/{id:guid}", ProductLandingAsync)
            .WithTags("Client · DeepLink").AllowAnonymous();

        app.MapGet("/s/{id:guid}", ShopLandingAsync)
            .WithTags("Client · DeepLink").AllowAnonymous();

        return app;
    }

    // ---------------------------------------------------------------------
    // iOS — Universal Links
    // ---------------------------------------------------------------------
    private static IResult AppleAppSiteAssociation(IConfiguration config)
    {
        var appIds = config.GetSection("DeepLink:AppleAppIds").Get<string[]>()
                     ?? new[] { "QSVS3QW2NQ.fr.hbamarket.app", "QSVS3QW2NQ.fr.hbamarket.app.staging" };

        // Le format « components » est celui recommandé depuis iOS 13. On ouvre
        // dans l'app les fiches produit (/p/*) ET les vitrines boutique (/s/*).
        var payload = new
        {
            applinks = new
            {
                apps = Array.Empty<string>(),
                details = new[]
                {
                    new
                    {
                        appIDs = appIds,
                        components = new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["/"] = "/p/*",
                                ["comment"] = "Fiches produit",
                            },
                            new Dictionary<string, object>
                            {
                                ["/"] = "/s/*",
                                ["comment"] = "Vitrines boutique",
                            },
                        },
                    },
                },
            },
        };

        // Content-Type application/json OBLIGATOIRE, et aucune redirection : Apple
        // récupère ce fichier tel quel.
        return Results.Json(payload, contentType: "application/json");
    }

    // ---------------------------------------------------------------------
    // Android — App Links
    // ---------------------------------------------------------------------
    private static IResult AssetLinks(IConfiguration config)
    {
        var packages = config.GetSection("DeepLink:AndroidPackages").Get<string[]>()
                       ?? new[] { "com.hbaexpress.client", "com.hbaexpress.client.staging" };

        // Empreintes SHA-256 du certificat de signature (Play App Signing).
        // TANT QU'ELLES SONT VIDES, la vérification automatique Android échoue —
        // le lien ouvrira la page web au lieu de l'app. À renseigner en config.
        var fingerprints = config.GetSection("DeepLink:AndroidSha256").Get<string[]>()
                           ?? Array.Empty<string>();

        var statements = packages.Select(pkg => new
        {
            relation = new[] { "delegate_permission/common.handle_all_urls" },
            target = new
            {
                @namespace = "android_app",
                package_name = pkg,
                sha256_cert_fingerprints = fingerprints,
            },
        });

        return Results.Json(statements, contentType: "application/json");
    }

    // ---------------------------------------------------------------------
    // Page de repli (app absente / desktop / aperçu de lien)
    // ---------------------------------------------------------------------
    private static async Task<IResult> ProductLandingAsync(
        Guid id, ICatalogModuleApi catalog, IConfiguration config, CancellationToken ct)
    {
        // ─────────────────────────────────────────────────────────────────────────
        // LA SURFACE LA PLUS EXPOSÉE DES TROIS.
        //
        // Cette page rend du HTML avec des balises Open Graph : un produit servi ici
        // est partageable ET récupérable par un robot d'indexation. Un brouillon ou
        // un produit rejeté y était intégralement exposé, alors même que le
        // commentaire de repli ci-dessous promettait « produit introuvable ou RETIRÉ :
        // page générique » — promesse qu'aucun test de statut ne tenait.
        // ─────────────────────────────────────────────────────────────────────────
        var product = await catalog.GetPubliclyVisibleProductAsync(id, ct);

        var appStore = config.GetValue<string>("DeepLink:AppStoreUrl")
                       ?? config.GetValue<string>("MobileApp:UpdateUrlIos")
                       ?? "https://apps.apple.com/app/id0000000000";
        var playStore = config.GetValue<string>("DeepLink:PlayStoreUrl")
                        ?? config.GetValue<string>("MobileApp:UpdateUrlAndroid")
                        ?? "https://play.google.com/store/apps/details?id=com.hbaexpress.client";

        // Produit introuvable / retiré : page générique, on renvoie quand même du
        // HTML (200) pour rester un bel aperçu de lien plutôt qu'une erreur brute.
        var name = product?.Name ?? "HBA Express";
        var description = Trim(product?.Description ?? "Découvrez des milliers de produits sur HBA Express.", 160);
        var image = product?.Media?.FirstOrDefault(m => m.IsPrimary)?.Url
                    ?? product?.Media?.FirstOrDefault()?.Url
                    ?? string.Empty;

        var html = BuildHtml(name, description, image, appStore, playStore);
        return Results.Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// Page de repli pour un lien BOUTIQUE partagé (`/s/{id}`). Même logique que la
    /// fiche produit : si l'app est installée, le système ouvre la vitrine
    /// directement ; sinon le navigateur affiche cet aperçu + boutons de store.
    /// </summary>
    private static async Task<IResult> ShopLandingAsync(
        Guid id, ISellerModuleApi sellers, IConfiguration config, CancellationToken ct)
    {
        var seller = (await sellers.GetSellerAsync(id, ct))?.ToPublic();

        var appStore = config.GetValue<string>("DeepLink:AppStoreUrl")
                       ?? config.GetValue<string>("MobileApp:UpdateUrlIos")
                       ?? "https://apps.apple.com/app/id0000000000";
        var playStore = config.GetValue<string>("DeepLink:PlayStoreUrl")
                        ?? config.GetValue<string>("MobileApp:UpdateUrlAndroid")
                        ?? "https://play.google.com/store/apps/details?id=com.hbaexpress.client";

        // Boutique introuvable / suspendue : aperçu générique (200) plutôt qu'une erreur.
        var name = seller?.ShopName ?? "HBA Express";
        var description = Trim(
            !string.IsNullOrWhiteSpace(seller?.Description)
                ? seller!.Description!
                : "Découvrez cette boutique et ses produits sur HBA Express.",
            160);
        var image = seller?.LogoUrl ?? string.Empty;

        var html = BuildHtml(name, description, image, appStore, playStore);
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static string Trim(string s, int max)
        => s.Length <= max ? s : s[..max].TrimEnd() + "…";

    private static string BuildHtml(string name, string description, string image, string appStore, string playStore)
    {
        string E(string s) => WebUtility.HtmlEncode(s);
        string A(string s) => WebUtility.HtmlEncode(s).Replace("\"", "&quot;");

        var imgTag = string.IsNullOrEmpty(image)
            ? ""
            : $"<img class=\"photo\" src=\"{A(image)}\" alt=\"{A(name)}\" />";
        var ogImage = string.IsNullOrEmpty(image) ? "" : $"<meta property=\"og:image\" content=\"{A(image)}\" />";

        var sb = new StringBuilder();
        sb.Append($@"<!doctype html>
<html lang=""fr"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
<title>{E(name)} · HBA Express</title>
<meta name=""description"" content=""{A(description)}"" />
<meta property=""og:type"" content=""product"" />
<meta property=""og:title"" content=""{A(name)}"" />
<meta property=""og:description"" content=""{A(description)}"" />
{ogImage}
<meta name=""twitter:card"" content=""summary_large_image"" />
<style>
  :root {{ --green:#1E7A4B; --ink:#0E1A15; --subtle:#7A8580; --bg:#F4F6F5; --line:#E3E8E5; }}
  * {{ box-sizing:border-box; }}
  body {{ margin:0; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;
         background:var(--bg); color:var(--ink); display:flex; min-height:100vh; align-items:center; justify-content:center; padding:24px; }}
  .card {{ background:#fff; border:1px solid var(--line); border-radius:20px; max-width:420px; width:100%;
          padding:28px 24px; text-align:center; box-shadow:0 10px 40px rgba(0,0,0,.06); }}
  .brand {{ font-weight:800; color:var(--green); letter-spacing:.5px; font-size:13px; text-transform:uppercase; }}
  .photo {{ width:100%; max-height:280px; object-fit:contain; border-radius:14px; background:#fff; margin:16px 0; }}
  h1 {{ font-size:20px; margin:8px 0 6px; }}
  p.desc {{ color:var(--subtle); font-size:14px; line-height:1.5; margin:0 0 20px; }}
  a.btn {{ display:flex; align-items:center; justify-content:center; gap:8px; text-decoration:none;
          padding:14px 18px; border-radius:14px; font-weight:700; font-size:15px; margin:10px 0; }}
  a.primary {{ background:var(--green); color:#fff; }}
  a.secondary {{ background:var(--bg); color:var(--ink); border:1px solid var(--line); }}
  .hint {{ color:var(--subtle); font-size:12px; margin-top:16px; }}
</style>
</head>
<body>
  <main class=""card"">
    <div class=""brand"">HBA Express</div>
    {imgTag}
    <h1>{E(name)}</h1>
    <p class=""desc"">{E(description)}</p>
    <a id=""ios"" class=""btn primary"" href=""{A(appStore)}"" style=""display:none"">Télécharger sur l'App Store</a>
    <a id=""android"" class=""btn primary"" href=""{A(playStore)}"" style=""display:none"">Disponible sur Google Play</a>
    <a id=""both-ios"" class=""btn secondary"" href=""{A(appStore)}"">App Store</a>
    <a id=""both-android"" class=""btn secondary"" href=""{A(playStore)}"">Google Play</a>
    <p class=""hint"">Ouvrez ce lien depuis l'app HBA Express pour voir le produit directement.</p>
  </main>
<script>
  // Priorise le bon store selon la plateforme ; sur desktop, on montre les deux.
  var ua = navigator.userAgent || '';
  var isIOS = /iPhone|iPad|iPod/i.test(ua);
  var isAndroid = /Android/i.test(ua);
  function show(id){{ var e=document.getElementById(id); if(e) e.style.display='flex'; }}
  function hide(id){{ var e=document.getElementById(id); if(e) e.style.display='none'; }}
  if (isIOS) {{ show('ios'); hide('both-ios'); hide('both-android'); }}
  else if (isAndroid) {{ show('android'); hide('both-ios'); hide('both-android'); }}
</script>
</body>
</html>");
        return sb.ToString();
    }
}
