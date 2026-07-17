# syntax=docker/dockerfile:1
# Dockerfile unique paramétré : sert à construire le monolithe (Marketplace.Api)
# comme chacun des 4 BFF. Le projet à publier et son DLL d'entrée sont passés en
# arguments de build (PROJECT / APP_DLL) depuis docker-compose.
#
# Contexte de build attendu : la RACINE du dépôt (pour disposer de
# Directory.Build.props / Directory.Packages.props et de tous les modules).

ARG DOTNET_VERSION=8.0

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG PROJECT
WORKDIR /src

# Copie de toute la solution (gestion centralisée des packages + graphe de projets).
COPY . .

# Cache NuGet partagé (BuildKit) : les 5 images et les rebuilds réutilisent les
# packages déjà téléchargés → restores quasi instantanés après la 1re fois.
#
# ⚠️ sharing=locked : `docker compose build` construit les 5 images EN PARALLÈLE
# (BuildKit bake), or le cache par défaut est en accès CONCURRENT (sharing=shared).
# Cinq `dotnet restore` écrivant ensemble dans le même cache se marchent dessus et
# NuGet échoue sur « Could not find file .../xxxxx.tmp ». `locked` sérialise l'accès
# au cache (un restore à la fois), sans le désactiver — la compilation, elle, reste
# parallèle.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "${PROJECT}"
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "${PROJECT}" -c Release -o /app --no-restore /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
ARG APP_DLL
ENV APP_DLL=${APP_DLL} \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

WORKDIR /app

# curl pour le HEALTHCHECK des conteneurs (image aspnet sans outil HTTP par défaut).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

EXPOSE 8080

# APP_DLL est figé au build de chaque service (un DLL d'entrée par image).
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
