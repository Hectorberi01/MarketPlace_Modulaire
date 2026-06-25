# BFF — Backend For Frontend (placeholder)

Couche d'agrégation par surface cliente (cf. dossier §10). Chaque front parle à
un BFF adapté, pas directement aux modules :

- `Marketplace.Bff.Mobile` — payloads compacts pour l'app Flutter acheteur.
- `Marketplace.Bff.Web` — agrégation pour le SSR Next.js (site acheteur).

Le BFF compose plusieurs appels in-process (via les `Contracts` des modules) en
parallèle (`Task.WhenAll`) avec dégradation gracieuse, et s'appuie sur le read
model dénormalisé (Search) pour les listings. Non implémenté dans ce socle
backend Jour 1.
