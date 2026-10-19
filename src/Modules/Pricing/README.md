# Module Pricing (placeholder)

Module prévu par le dossier d'architecture, pas encore implémenté dans ce socle.

Il suivra exactement le patron du module **Catalog** : quatre projets en Clean
Architecture — `Pricing.Domain`, `Pricing.Application`, `Pricing.Infrastructure`,
`Pricing.Contracts` — un schéma PostgreSQL dédié (`pricing`),
un `IModuleInstaller` enregistré dans `Marketplace.Host/Modules/ModuleRegistry.cs`,
et une communication vers l'extérieur uniquement via `Contracts` (appel
in-process) ou via les `IntegrationEvents` (bus).

Pour le créer : copier le dossier `Catalog`, renommer les projets/namespaces,
ajouter les projets à `Marketplace.sln`, brancher l'installer.
