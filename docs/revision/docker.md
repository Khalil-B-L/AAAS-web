# Fiche de révision — Docker et docker compose

> À relire la veille d'un entretien. Exemples tirés du projet AAAS (`src/Aaas.Api/Dockerfile`, `docker-compose.yml`).

## 1. Les notions en une ligne

| Notion | Définition | Analogie .NET |
|---|---|---|
| **Image** | Modèle figé, en lecture seule : un système de fichiers (OS minimal + runtime + appli) + une commande de démarrage. Construite par `docker build` depuis un `Dockerfile`. | La **classe** ; ou le dossier produit par `dotnet publish` avec son runtime |
| **Conteneur** | Une instance **en cours d'exécution** d'une image, avec sa couche d'écriture propre (perdue à la suppression). | L'**objet** (`new`) ; un processus isolé |
| **Couche (layer)** | Chaque instruction du Dockerfile (`COPY`, `RUN`...) produit une couche ; une image est un empilement de couches, partagées entre images. | Un incrément de build |
| **Cache de build** | Une couche est réutilisée si l'instruction et ses entrées n'ont pas changé ; dès qu'une couche change, toutes les suivantes sont refaites. | Build incrémental de MSBuild |
| **Registry** | Entrepôt d'images (`mcr.microsoft.com`, Docker Hub, GitHub Container Registry). `docker pull` / `docker push`. | Un flux NuGet, pour les images |
| **Volume** | Stockage géré par Docker, **hors** du conteneur : survit à sa suppression. | Un disque de données séparé du serveur |
| **Port publié** | `"8080:8080"` = `hôte:conteneur`. Sans publication, le port n'est joignable que depuis le réseau Docker. | Une règle de pare-feu / NAT |
| **Réseau** | Compose crée un réseau privé par projet ; chaque service y est joignable **par son nom** (DNS interne). | — |
| **Variables d'environnement** | Configuration injectée au démarrage du conteneur. En .NET, `Jwt__SigningKey` = `Jwt:SigningKey` et prime sur `appsettings.json`. | Les variables d'environnement d'IIS / App Service |
| **Healthcheck** | Commande lancée périodiquement dans le conteneur ; état `starting` → `healthy` / `unhealthy`. | Une sonde de santé (`/health`) |

## 2. Dockerfile multi-stage (exemple du projet)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build          # stage 1 : image lourde avec le SDK
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Aaas.Api/Aaas.Api.csproj src/Aaas.Api/            # (+ les autres .csproj)
RUN dotnet restore src/Aaas.Api/Aaas.Api.csproj            # couche en cache tant que les .csproj ne changent pas
COPY src/ src/                                             # le code : invalide le cache à partir d'ici seulement
RUN dotnet publish src/Aaas.Api/Aaas.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final         # stage 2 : runtime seul
WORKDIR /app
EXPOSE 8080
USER $APP_UID                                              # utilisateur non-root « app » (1654) fourni par l'image
COPY --from=build /app/publish .                           # on ne récupère QUE le résultat du stage 1
ENTRYPOINT ["dotnet", "Aaas.Api.dll"]
```

Pourquoi :
- **Multi-stage** : l'image finale ne contient ni SDK, ni code source, ni `obj/` → plus petite (≈ 315 Mo contre ≈ 900 Mo) et moins de surface d'attaque.
- **Ordre des COPY** : dépendances d'abord, code ensuite → modifier un `.cs` ne relance pas le `restore` (on le voit : `CACHED` dans la sortie du build).
- **Non-root** : une faille dans l'appli ne donne pas root dans le conteneur. Le port 8080 (> 1024) ne demande pas de privilège.
- **Contexte de build** = le dossier passé à `docker build` (ici la racine du dépôt), envoyé au démon. Le Dockerfile ne peut copier que ce qui est dans le contexte.
- `ENTRYPOINT ["...", "..."]` (forme exec) : le processus .NET est le PID 1 et reçoit directement le signal d'arrêt (arrêt propre).

## 3. `.dockerignore`

Exclut des fichiers du **contexte** : `bin/`, `obj/`, `.git/`, `.env`, `node_modules/`... Deux bénéfices : build plus rapide (moins de fichiers envoyés) et **aucun secret** ni artefact local copié par mégarde par un `COPY src/ src/`. Piège : un `obj/` Windows copié dans l'image peut casser le `restore` Linux.

## 4. docker compose (exemple du projet)

```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-CU27-GDR2-ubuntu-22.04   # tag précis, pas « latest »
    environment:
      MSSQL_SA_PASSWORD: ${MSSQL_SA_PASSWORD:?Renseignez MSSQL_SA_PASSWORD dans .env}   # lu depuis .env ; « :? » = obligatoire
    ports: ["127.0.0.1:1433:1433"]               # IP_hôte:port_hôte:port_conteneur ; 127.0.0.1 = ce poste seulement
    volumes: [sqlserver-data:/var/opt/mssql]     # volume nommé : les données survivent à « down »
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$${MSSQL_SA_PASSWORD}\" -C -b -Q \"SELECT 1\" -o /dev/null"]
      interval: 10s
      retries: 10
      start_period: 30s                          # échecs ignorés pendant le démarrage
  api:
    build: { context: ., dockerfile: src/Aaas.Api/Dockerfile }
    depends_on:
      sqlserver: { condition: service_healthy }  # attendre le healthcheck, pas seulement le démarrage
    environment:
      ConnectionStrings__Default: "Server=sqlserver,1433;..."   # « sqlserver » = nom du service sur le réseau compose
    ports: ["127.0.0.1:8080:8080"]
volumes:
  sqlserver-data:
```

Points clés :
- **`depends_on` seul** n'attend que le *lancement* du conteneur ; avec `condition: service_healthy`, compose attend que le healthcheck soit vert.
- **`$$`** dans `docker-compose.yml` : échappe le `$` pour que la variable soit lue par le shell **du conteneur**, au lieu d'être interpolée par compose.
- **`localhost` dans un conteneur = le conteneur lui-même.** L'API joint la base par `sqlserver`, ton PC la joint par `localhost,1433` (port publié).
- **`.env`** à côté de `docker-compose.yml` est lu automatiquement pour l'interpolation `${...}`. Il n'est jamais versionné (`.env.example` sert de modèle, avec les secrets **vides**).
- **Interpolation** : `${VAR:?msg}` = erreur avec `msg` si absente **ou vide** (secrets obligatoires) ; `${VAR?msg}` = erreur seulement si absente ; `${VAR:-défaut}` = valeur par défaut si absente ou vide. Vérifier sans rien lancer : `docker compose config`.
- **Ports publiés** : `"1433:1433"` écoute sur `0.0.0.0` (toutes les interfaces, donc le Wi-Fi du café). `"127.0.0.1:1433:1433"` limite l'accès au poste. Les conteneurs entre eux n'en ont pas besoin : ils passent par le réseau compose (`sqlserver:1433`).
- **Tag ou digest** : un tag (`2022-latest`) est une étiquette mobile vers un digest (`sha256:...`, immuable). Épingler un tag précis (`2022-CU27-GDR2-ubuntu-22.04`) rend les builds reproductibles ; épingler le digest (`image@sha256:...`) est encore plus strict. Retrouver le digest local : `docker image inspect <image> --format '{{json .RepoDigests}}'`.

## 5. Commandes utiles

| Commande | Effet |
|---|---|
| `docker compose up --build -d` | (Re)construit les images et démarre tout en arrière-plan |
| `docker compose up -d sqlserver` | Démarre un seul service |
| `docker compose ps` | État des services (dont `healthy`) et ports |
| `docker compose logs -f api` | Suit les journaux d'un service |
| `docker compose exec api id` | Lance une commande dans un conteneur qui tourne (ici : vérifier l'utilisateur non-root) |
| `docker compose exec sqlserver bash` | Shell dans le conteneur |
| `docker compose restart api` | Redémarre un service (test d'idempotence de l'initialisation) |
| `docker compose down` / `down -v` | Supprime conteneurs + réseau / **et** les volumes (données perdues) |
| `docker compose config` | Affiche le fichier résolu (variables remplacées) : détecte les erreurs sans rien lancer |
| `docker inspect --format '{{json .State.Health}}' aaas-sqlserver-1` | Détail des derniers healthchecks |
| `docker image ls` / `docker system df` | Images et place occupée |
| `docker build -f src/Aaas.Api/Dockerfile -t aaas-api:local .` | Construire l'image seule (le `.` final = contexte) |

## 6. Pièges rencontrés dans le projet

- **Healthcheck de l'API** : l'image `aspnet` ne contient ni `curl` ni `wget`. Options : installer `curl` (image plus grosse), embarquer un mini-programme de sonde, ou ne pas mettre de healthcheck et s'en remettre à la sonde de la plateforme (App Service). Choix actuel : pas de healthcheck sur `api`.
- **`sqlcmd` sans `-I`** : `QUOTED_IDENTIFIER OFF` → toute écriture sur une table qui a un index filtré échoue (erreur 1934).
- **Git Bash** convertit `/opt/...` en `C:/Program Files/Git/opt/...` dans les arguments de `docker` : préfixer par `MSYS_NO_PATHCONV=1`.
- **Mot de passe SA** : SQL Server refuse de démarrer si le mot de passe est trop faible (le conteneur s'arrête, voir `docker compose logs sqlserver`). Éviter `;` (casse la chaîne de connexion) et `$` (interpolation compose).
- **Mot de passe SA figé dans le volume** : `MSSQL_SA_PASSWORD` n'est lu qu'à la **création** de la base `master`, au premier démarrage sur un volume vide. Le changer dans `.env` ensuite donne « Login failed for user 'sa' » (healthcheck rouge, `api` ne démarre pas). En local : `docker compose down -v` puis `up` (données perdues) ; sinon `ALTER LOGIN sa WITH PASSWORD = ...` avec l'ancien mot de passe.
- **DataProtection** : l'avertissement « keys ... may not be persisted outside of the container » signifie que les clés sont perdues à la suppression du conteneur. Sans importance tant qu'on n'utilise ni cookies ni jetons de réinitialisation de mot de passe.

## 7. Flux complet d'une requête (vue d'ensemble du projet)

Exemple : un membre clique sur « Se connecter ».

```
[Navigateur]  composant Angular (formulaire) ── AuthService.login() ── HttpClient
      │        (S2) l'interceptor ajoutera « Authorization: Bearer ... » aux requêtes suivantes
      │  POST http://localhost:8080/api/auth/login   {"email":"...","password":"..."}
      ▼        (en S2 : via nginx du conteneur front qui relaie /api vers api:8080 ; en Azure : HTTPS + CORS)
[Hôte : port 8080] ──(port publié 8080:8080)──▶ [Conteneur api, utilisateur app]
      ▼
 Kestrel ─▶ middlewares : ExceptionHandler → StatusCodePages → CORS → Authentication (JWT) → Authorization
      ▼
 AuthController.Login (Api)            : validation du DTO ([ApiController] → 400 si invalide)
      ▼
 AccountService.LoginAsync (Application) : règles du cas d'usage, TimeProvider
      ▼
 IdentityService (Infrastructure)       : SignInManager.CheckPasswordSignInAsync (PBKDF2, blocage)
      ▼
 AppDbContext (EF Core) ── TDS, réseau compose « Server=sqlserver,1433 » ──▶ [Conteneur sqlserver]
                                                                              données dans le volume sqlserver-data
      ▲
 TokenService : JWT signé HS256 ◀── réponse 200 { accessToken, expiresAt, user }  (ou ProblemDetails 401)
```

Plus tard : en **CI** (GitHub Actions), la même image est construite et publiée dans un registry ; en **Azure**, l'API tourne sur App Service, la base est Azure SQL, et les secrets (`Jwt__SigningKey`, chaîne de connexion) viennent de Key Vault au lieu de `.env`.

## 8. Questions d'entretien

- **Différence entre image et conteneur ?** L'image est le modèle immuable ; le conteneur en est une instance en cours d'exécution, avec une couche d'écriture éphémère.
- **Pourquoi un build multi-stage ?** Compiler avec le SDK, livrer avec le runtime seul : image plus petite, sans code source ni outils, donc plus sûre et plus rapide à déployer.
- **Comment optimiser le cache ?** Copier d'abord les fichiers de dépendances (`*.csproj`, `package.json`), restaurer, puis copier le code ; garder les instructions qui changent souvent en bas du Dockerfile.
- **Où vont les données d'une base en conteneur ?** Dans un volume ; sans volume, elles disparaissent avec le conteneur.
- **`depends_on` garantit-il que la base est prête ?** Non, seulement qu'elle est lancée ; il faut `condition: service_healthy` avec un healthcheck, et idéalement un retry côté appli (`EnableRetryOnFailure`).
- **Pourquoi ne pas lancer en root ?** Principe du moindre privilège : limiter l'impact d'une faille.
- **Pourquoi éviter `latest` ?** Le contenu change sans que le fichier change : deux postes ou deux builds n'ont pas la même version, et un bug peut apparaître « tout seul ». On épingle un tag précis (ou un digest) et on monte de version volontairement.
- **`"1433:1433"` ou `"127.0.0.1:1433:1433"` ?** Le premier expose le port à tout le réseau ; le second le limite à la machine. En développement, on n'expose que ce dont l'hôte a besoin.
