# syntax=docker/dockerfile:1
# Dockerfile paramétré : construit l'hôte (Marketplace.Host). Le projet à publier
# et son DLL d'entrée sont passés en arguments de build (PROJECT / APP_DLL) par le
# compose, par deploy/build-and-push.sh et par le workflow de CI.
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

# Cache NuGet partagé (BuildKit) : les rebuilds réutilisent les packages déjà
# téléchargés → restores quasi instantanés après la 1re fois.
#
# sharing=locked, et ce n'est pas devenu inutile avec l'hôte unique : `docker
# compose build` peut encore construire plusieurs images en parallèle (BuildKit
# bake), et deux `dotnet restore` écrivant ensemble dans le même cache font
# échouer NuGet sur « Could not find file .../xxxxx.tmp ». `locked` sérialise
# l'accès au cache sans le désactiver ; la compilation reste parallèle.
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
