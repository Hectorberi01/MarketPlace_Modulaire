#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# A9 — plafond de tentatives + lettres mortes de l'outbox.
#
# La table `outbox_messages` existe dans LES 25 SCHÉMAS (une par module) : il faut
# donc 25 migrations. Elles ajoutent 3 colonnes (AttemptCount défaut 0,
# NextAttemptAtUtc, DeadLetteredOnUtc) et 2 index partiels.
#
# AUCUNE REPRISE DE DONNÉES : les lignes existantes démarrent à 0 tentative, sans
# temporisation ni lettre morte — exactement le comportement d'avant.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Analytics/Marketplace.Modules.Analytics.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Billing/Marketplace.Modules.Billing.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Cart/Marketplace.Modules.Cart.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Catalog/Marketplace.Modules.Catalog.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Disputes/Marketplace.Modules.Disputes.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Fraud/Marketplace.Modules.Fraud.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Identity/Marketplace.Modules.Identity.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Inventory/Marketplace.Modules.Inventory.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Loyalty/Marketplace.Modules.Loyalty.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Marketing/Marketplace.Modules.Marketing.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Messaging/Marketplace.Modules.Messaging.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Notifications/Marketplace.Modules.Notifications.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Offers/Marketplace.Modules.Offers.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Ordering/Marketplace.Modules.Ordering.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Payments/Marketplace.Modules.Payments.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Pricing/Marketplace.Modules.Pricing.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Recommendations/Marketplace.Modules.Recommendations.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Returns/Marketplace.Modules.Returns.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Reviews/Marketplace.Modules.Reviews.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Search/Marketplace.Modules.Search.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Sellers/Marketplace.Modules.Sellers.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Settlement/Marketplace.Modules.Settlement.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Shipping/Marketplace.Modules.Shipping.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Tax/Marketplace.Modules.Tax.Infrastructure
dotnet ef migrations add AddOutboxRetryTracking -p src/Modules/Wishlist/Marketplace.Modules.Wishlist.Infrastructure
