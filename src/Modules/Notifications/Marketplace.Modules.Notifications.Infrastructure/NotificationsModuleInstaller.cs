using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.IntegrationEvents;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using FirebaseAdmin.Messaging;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Application.Notifications;
using Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;
using Marketplace.Modules.Notifications.Application.Emails;
using Marketplace.Modules.Notifications.Application.Notifications.Queries;
using Marketplace.Modules.Notifications.Domain.Devices;
using Marketplace.Modules.Notifications.Domain.Notifications;
using Marketplace.Modules.Notifications.Domain.Preferences;
using Marketplace.Modules.Notifications.Infrastructure.Persistence;
using Marketplace.Modules.Notifications.Infrastructure.Email;
using Marketplace.Modules.Notifications.Infrastructure.Push;
using Marketplace.Modules.Identity.Contracts.IntegrationEvents;
using Marketplace.Modules.Sellers.Contracts.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;
using Marketplace.Modules.Messaging.Contracts.IntegrationEvents;
using Marketplace.Modules.Disputes.Contracts.IntegrationEvents;
using Marketplace.Modules.Reviews.Contracts.IntegrationEvents;
using Marketplace.Modules.Inventory.Contracts.IntegrationEvents;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;
// Lève l'ambiguïté avec FirebaseAdmin.Messaging.FcmOptions.
using FcmOptions = Marketplace.Modules.Notifications.Infrastructure.Push.FcmOptions;

namespace Marketplace.Modules.Notifications.Infrastructure;

/// <summary>Enregistre le module Notifications : DbContext, repository, dispatcher et consumers fan-out.</summary>
public sealed class NotificationsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Notifications";

    public Assembly ApplicationAssembly => typeof(ListMyNotificationsQuery).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", NotificationsDbContext.SchemaName)));

        services.AddScoped<INotificationsUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<NotificationDispatcher>();

        // Push (FCM) : si un compte de service Firebase est configuré, on branche
        // l'envoi réel ; sinon un adaptateur no-op (aucun push, mais tout compile/tourne).
        var fcm = BindFcmOptions(configuration);
        services.AddSingleton(fcm);
        var fcmReady = false;
        if (fcm.IsConfigured)
        {
            // ⚠️ Le push est OPTIONNEL : un compte de service FCM MALFORMÉ (clé privée
            // invalide, JSON tronqué, `\n` mal échappés…) ne doit PAS abattre toute la
            // plateforme. On tente de le brancher ; en cas d'échec on DÉGRADE proprement
            // vers NullPushSender en traçant la cause, au lieu de crasher au démarrage.
            try
            {
                if (FirebaseApp.DefaultInstance is null)
                {
                    FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(fcm.ResolveJson()) });
                }
                services.AddSingleton(FirebaseMessaging.DefaultInstance);
                services.AddScoped<IPushSender, FcmPushSender>();
                fcmReady = true;
                Console.WriteLine("[Push] FCM configuré : FcmPushSender actif (les push seront envoyés).");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Push] FCM configuré mais INVALIDE ({ex.GetType().Name} : {ex.Message}). " +
                    "Repli sur NullPushSender (aucun push). Vérifier le JSON du compte de service " +
                    "— clé privée base64 valide et échappement des « \\n » (simple, pas double).");
            }
        }
        if (!fcmReady)
        {
            services.AddScoped<IPushSender, NullPushSender>();
            // DIAGNOSTIC (uniquement si RIEN n'était configuré ; le cas « configuré mais
            // invalide » a déjà loggé sa propre cause dans le catch ci-dessus).
            if (!fcm.IsConfigured)
            {
                Console.WriteLine(
                    $"[Push] FCM NON configuré : NullPushSender actif (aucun push). " +
                    $"Path='{fcm.ServiceAccountPath}', JsonInline={( string.IsNullOrWhiteSpace(fcm.ServiceAccountJson) ? "non" : "oui")}, " +
                    $"FichierExiste={(!string.IsNullOrWhiteSpace(fcm.ServiceAccountPath) && File.Exists(fcm.ServiceAccountPath) ? "oui" : "non")}.");
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // E-MAIL. IL N'Y EN AVAIT AUCUN — ET CE VIDE AVAIT PRODUIT UNE FAILLE CRITIQUE.
        //
        // Aucun IEmailSender n'existait dans le dépôt. Conséquences en chaîne :
        //   • l'e-mail de vérification n'est jamais parti (l'événement n'avait aucun
        //     consommateur) ;
        //   • le jeton de réinitialisation n'avait nulle part où aller, alors il a été
        //     renvoyé DANS LA RÉPONSE HTTP d'un endpoint anonyme. N'importe qui prenait
        //     n'importe quel compte, administrateurs compris.
        //
        // Une capacité manquante finit toujours par être contournée. Le contournement est
        // rarement sûr.
        // ═════════════════════════════════════════════════════════════════════════
        var email = BindEmailOptions(configuration);
        services.AddSingleton(email);
        services.AddSingleton<IAccountLinkBuilder, AccountLinkBuilder>();

        if (email.IsConfigured)
        {
            services.AddHttpClient(ResendEmailSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
            services.AddScoped<IEmailSender, ResendEmailSender>();
        }
        else if (IsProduction(configuration))
        {
            // ⚠️ REFUS DE DÉMARRER. C'est volontairement brutal.
            //
            // Sans e-mail en production : aucun compte ne peut être vérifié, et surtout
            // AUCUN mot de passe oublié ne peut être récupéré. Les utilisateurs se
            // retrouvent enfermés dehors, définitivement, et l'exploitant ne s'en aperçoit
            // que par les plaintes.
            //
            // Un adaptateur silencieux ferait « tourner » la plateforme dans cet état. Un
            // démarrage refusé se remarque tout de suite — et se corrige en une variable.
            throw new InvalidOperationException(
                "Notifications:Email n'est pas configuré (ApiKey, From, AppBaseUrl) alors que "
                + "l'environnement est Production. Sans canal e-mail, la vérification d'adresse et "
                + "la réinitialisation de mot de passe sont IMPOSSIBLES : les utilisateurs qui "
                + "oublient leur mot de passe seront définitivement bloqués. "
                + "Renseigner les secrets dans le vault, puis redéployer.");
        }
        else
        {
            // Développement : l'e-mail est écrit dans la console (jeton compris), pour que
            // le flux complet reste praticable en local. Impossible en production — la
            // garde ci-dessus s'est déclenchée avant.
            services.AddScoped<IEmailSender, DevelopmentEmailSender>();
            Console.WriteLine(
                "[E-mail] Notifications:Email NON configuré : DevelopmentEmailSender actif "
                + "(les e-mails sont écrits dans la console, PAS envoyés).");
        }

        // Les deux consommateurs qui n'existaient pas. Sans eux, les événements étaient
        // publiés, marqués traités, et disparaissaient — sans la moindre erreur.
        services.AddScoped<IIntegrationEventHandler<EmailVerificationRequestedIntegrationEvent>, SendEmailVerificationHandler>();
        services.AddScoped<IIntegrationEventHandler<PasswordResetRequestedIntegrationEvent>, SendPasswordResetEmailHandler>();

        // Consumers fan-out : un fait métier d'un autre module -> une notification.
        services.AddScoped<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, OrderPlacedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, OrderConfirmedNotificationHandler>();

        // Le VENDEUR est prévenu de la même confirmation, par un handler distinct.
        // Le dispatcher résout tous les handlers d'un event (GetServices) : celui de
        // l'acheteur et celui du vendeur coexistent sans se gêner. Séparés, parce
        // qu'ils ne disent pas la même chose à des gens qui n'attendent pas la même
        // chose — et que l'un peut échouer sans faire tomber l'autre.
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, SellerOrderConfirmedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, OrderCancelledNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<ShipmentShippedIntegrationEvent>, ShipmentShippedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>, ShipmentDeliveredNotificationHandler>();

        // REMBOURSEMENTS. Deux moments, deux messages :
        //  - accepté : rassure l'acheteur pendant que l'admin exécute le versement
        //              chez FedaPay (qui n'a pas d'API de remboursement) ;
        //  - versé   : l'argent est parti — on prévient l'acheteur ET le vendeur,
        //              qui vient d'être débité.
        services.AddScoped<IIntegrationEventHandler<ReturnRefundApprovedIntegrationEvent>, ReturnRefundApprovedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<ReturnRefundedIntegrationEvent>, ReturnRefundedNotificationHandler>();

        // BOUTIQUE VALIDÉE. L'admin active le vendeur → e-mail de bienvenue + push/in-app :
        // le vendeur passe de « en attente » à « peut publier », il doit le savoir tout de suite.
        services.AddScoped<IIntegrationEventHandler<SellerActivatedIntegrationEvent>, SellerActivatedNotificationHandler>();

        // REVERSEMENT VERSÉ. L'événement existait sans consommateur : le vendeur était
        // payé sans en être informé — le message qu'il attend le plus.
        services.AddScoped<IIntegrationEventHandler<PayoutPaidIntegrationEvent>, PayoutPaidNotificationHandler>();

        // MESSAGERIE. Sans ça, un fil de discussion n'alerte personne : chaque partie
        // devait rouvrir l'application pour découvrir une réponse.
        services.AddScoped<IIntegrationEventHandler<MessageSentIntegrationEvent>, MessageSentNotificationHandler>();

        // LITIGES. À l'ouverture, l'ADMIN est alerté (sinon rien ne remonte hors console) ;
        // à la résolution, l'ACHETEUR apprend la décision.
        services.AddScoped<AdminNotificationTarget>();
        services.AddScoped<IIntegrationEventHandler<DisputeOpenedIntegrationEvent>, DisputeOpenedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<DisputeResolvedIntegrationEvent>, DisputeResolvedNotificationHandler>();

        // CYCLE DE VIE BOUTIQUE : dossier KYB à valider (admin), suspension et
        // réactivation (vendeur, doublées par e-mail : il n'ouvrira pas forcément l'app).
        services.AddScoped<IIntegrationEventHandler<SellerRegisteredIntegrationEvent>, SellerRegisteredAdminNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<SellerClosedIntegrationEvent>, SellerClosedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<SellerReactivatedIntegrationEvent>, SellerReactivatedNotificationHandler>();

        // ACTIVITÉ VENDEUR : nouvel avis, rupture de stock (une vente perdue par heure).
        services.AddScoped<IIntegrationEventHandler<ReviewPublishedIntegrationEvent>, ReviewPublishedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<StockDepletedIntegrationEvent>, StockDepletedNotificationHandler>();

        // PAIEMENT ÉCHOUÉ : sans ce message, l'acheteur attend un colis qui ne partira pas.
        services.AddScoped<IIntegrationEventHandler<PaymentFailedIntegrationEvent>, PaymentFailedNotificationHandler>();

        services.AddOutboxProcessor<NotificationsDbContext>();
    }

    private static EmailOptions BindEmailOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Notifications:Email");
        return new EmailOptions
        {
            ApiKey = section["ApiKey"] ?? string.Empty,
            From = section["From"] ?? string.Empty,
            AppBaseUrl = section["AppBaseUrl"] ?? string.Empty,
        };
    }

    /// <summary>
    /// Même détection que PaymentsModuleInstaller : on lit l'environnement depuis la
    /// configuration, car un IModuleInstaller ne reçoit pas d'IWebHostEnvironment.
    /// </summary>
    private static bool IsProduction(IConfiguration configuration)
    {
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"];

        return string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);
    }

    private static FcmOptions BindFcmOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Notifications:Fcm");
        return new FcmOptions
        {
            ServiceAccountJson = section["ServiceAccountJson"] ?? string.Empty,
            ServiceAccountPath = section["ServiceAccountPath"] ?? string.Empty,
        };
    }
}
