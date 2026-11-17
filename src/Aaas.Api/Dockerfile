# Image de l'API. Contexte de construction = racine du dépôt (besoin de global.json, Directory.*.props, nuget.config) :
#   docker build -f src/Aaas.Api/Dockerfile -t aaas-api .

# ---- Stage 1 : compilation, dans l'image SDK (lourde, jamais déployée) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copier d'abord ce qui décrit les dépendances : cette couche et le « restore » restent en cache
# tant qu'aucun .csproj ni fichier de versions ne change, même si le code C# change.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Aaas.Domain/Aaas.Domain.csproj src/Aaas.Domain/
COPY src/Aaas.Application/Aaas.Application.csproj src/Aaas.Application/
COPY src/Aaas.Infrastructure/Aaas.Infrastructure.csproj src/Aaas.Infrastructure/
COPY src/Aaas.Api/Aaas.Api.csproj src/Aaas.Api/
RUN dotnet restore src/Aaas.Api/Aaas.Api.csproj

# Puis le code : seule cette partie est refaite quand on modifie un .cs.
COPY src/ src/
RUN dotnet publish src/Aaas.Api/Aaas.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- Stage 2 : exécution, dans l'image runtime ASP.NET (sans SDK ni code source) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Les images .NET écoutent sur 8080 par défaut (ASPNETCORE_HTTP_PORTS) : un port > 1024 ne demande pas root.
EXPOSE 8080

# Utilisateur non-root fourni par l'image (UID 1654) : une faille dans l'app ne donne pas root dans le conteneur.
USER $APP_UID

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Aaas.Api.dll"]
