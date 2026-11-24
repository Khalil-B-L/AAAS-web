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

## 6 bis. Étape 2 : cache de couches, migration sur un volume existant

**Cache de couches après l'ajout d'un paquet NuGet.** L'étape 2 ajoute `Microsoft.EntityFrameworkCore` à `Aaas.Application.csproj` et à `Directory.Packages.props`. Ces deux fichiers sont copiés **avant** le `dotnet restore` : leur changement invalide la couche `COPY ... *.csproj`, donc le `restore`, puis tout ce qui suit.

```
# sortie abrégée, numéros d'étapes et durées indicatifs
docker compose build api        # 1er build après l'ajout du paquet
 => [build 3/9] COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./   0.1s   ← refait
 => [build 8/9] RUN dotnet restore src/Aaas.Api/Aaas.Api.csproj                                   25s    ← refait (téléchargement)
docker compose build api        # 2e build, après une simple modification d'un .cs
 => CACHED [build 3/9] COPY global.json ...
 => CACHED [build 8/9] RUN dotnet restore ...                                                            ← réutilisé
 => [build 9/9] COPY src/ src/                                                                          ← seul le code est refait
```

Règle : **ce qui change rarement en haut, ce qui change souvent en bas**. Une couche invalidée invalide toutes les suivantes. C'est pour cela que `.editorconfig` (ajouté à l'étape 2) est copié **après** le `restore` : le modifier ne force pas un nouveau téléchargement des paquets.

À observer soi-même : `docker compose build api` deux fois de suite (tout `CACHED`), puis modifier un `.cs` et relancer (seules les dernières étapes sont refaites). `docker history aaas-api:local` liste les couches et leur taille.

**Migration au démarrage sur un volume existant.** La base vit dans le volume `sqlserver-data` (§ 1). `docker compose up -d --build api` remplace le **conteneur** de l'API (nouvelle image) sans toucher au volume de SQL Server : au démarrage, l'API voit dans `__EFMigrationsHistory` que `InitialIdentity` est déjà appliquée et n'applique que `AddEventsAndAssociationSettings`. Les comptes de l'étape 1 restent.

| Commande | Conteneurs | Volume `sqlserver-data` (base, comptes, admin) |
|---|---|---|
| `docker compose up -d --build api` | API recréée | conservé, migration ajoutée |
| `docker compose down` | supprimés | **conservé** |
| `docker compose down -v` | supprimés | **supprimé** : au prochain `up`, base vide, toutes les migrations et le premier admin sont rejoués. Local uniquement. |

Vérifier la migration depuis le conteneur SQL : voir `docs/technique/exploitation.md` § 7 (`sqlcmd -I`, `MSYS_NO_PATHCONV=1` sous Git Bash).

## 6 ter. Image du front : Node pour compiler, nginx pour servir (S2)

Fichiers : `front/Dockerfile`, `front/nginx/default.conf`, `front/nginx/security-headers.conf`, `front/.dockerignore`, service `front` de `docker-compose.yml`. Décision : ADR 0017.

```dockerfile
FROM node:24.22.3-alpine3.24 AS build                 # ≈ 170 Mo : Node + npm, juste pour compiler
WORKDIR /app
COPY package.json package-lock.json ./                # dépendances d'abord (cache)
RUN npm ci --no-audit --no-fund                       # exactement le lockfile, node_modules vide au départ
COPY . .                                              # le code (front/.dockerignore exclut node_modules, dist…)
RUN npx ng build --configuration production           # → /app/dist/front/browser : des fichiers statiques

FROM nginxinc/nginx-unprivileged:1.30.6-alpine3.24 AS final   # ≈ 60 Mo, non-root (UID 101), port 8080
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist/front/browser /usr/share/nginx/html
EXPOSE 8080
```

| Notion | Ce qu'il faut savoir |
|---|---|
| **Pourquoi deux étapes** | Un front compilé n'est que du HTML/JS/CSS : il faut un serveur web, pas Node. Image finale : **61 Mo** (mesuré), contre 170 Mo pour la seule image Node. |
| **`npm ci` vs `npm install`** | `npm ci` installe **exactement** `package-lock.json`, part d'un `node_modules` vide et échoue si le lockfile ne correspond pas à `package.json` : reproductible (≈ `dotnet restore` avec versions centralisées). `npm install` peut mettre à jour le lockfile. |
| **Cache** | `COPY package*.json` + `npm ci` **avant** `COPY . .` : modifier un composant ne relance que les deux dernières couches. |
| **Contexte** | `docker build front` : le contexte est `front/`, donc c'est `front/.dockerignore` qui compte (le `.dockerignore` racine exclut `front/` du contexte de l'API). Sans lui, `node_modules` Windows (des centaines de Mo) partirait au démon. |
| **Non-root** | `nginx-unprivileged` tourne en utilisateur `nginx` (UID 101) et écoute sur **8080** : un port < 1024 exigerait root. Vérifier : `docker exec <conteneur> id`. |
| **Repli SPA** | `try_files $uri $uri/ /index.html;` : `/evenements/42` n'existe pas sur disque, nginx renvoie `index.html` et le routeur Angular affiche la page. Sans cela, F5 sur une route profonde = 404. |
| **Reverse proxy** | `location /api/ { proxy_pass $api_upstream; }` : le navigateur ne parle qu'à `localhost:4200`, nginx relaie vers `api:8080` sur le réseau compose → **même origine, pas de CORS** (≈ YARP devant une API). |
| **DNS de Docker** | `resolver 127.0.0.11` + `set $api_upstream http://api:8080;` : avec une **variable**, nginx résout `api` à chaque requête (cache 10 s). Sans elle, il résout une seule fois au démarrage et renvoie 502 après `docker compose up -d --build api` (nouvelle IP). |
| **Cache HTTP** | `index.html` : `no-cache` (toujours revalidé). `main-ABCD1234.js` : `max-age=31536000, immutable` (le nom change à chaque version). |
| **Healthcheck HTTP** | `wget -q --spider http://127.0.0.1:8080/healthz` (BusyBox présent dans Alpine) ; `/healthz` répond sans appeler l'API. `docker compose ps` affiche `healthy`. |
| **Configuration figée** | `environment.ts` est compilé dans le JS : changer l'URL de l'API demande de **recompiler** l'image (contrairement à l'API, qui lit ses variables d'environnement au démarrage). D'où le chemin relatif `/api`. |

**Pièges rencontrés en construisant l'image**
- **Accolades dans une regex nginx** : `location ~* ^/chunk-[A-Za-z0-9]{8}\.js$ {` fait échouer nginx (« missing closing parenthesis ») : les `{}` sont lus comme un bloc. Mettre la regex **entre guillemets**. Tester la configuration sans démarrer : `docker run --rm --entrypoint nginx aaas-front:local -t`.
- **`add_header` et l'héritage** : dès qu'une `location` contient un `add_header`, elle n'hérite plus d'**aucun** `add_header` du niveau `server`. Les en-têtes de sécurité sont donc dans un fichier inclus dans chaque `location` (`security-headers.conf`).
- **CSP et CSS critique** : l'optimisation `inlineCritical` d'Angular ajoute un `onload` en ligne que `script-src 'self'` bloquerait : désactivée dans `angular.json`. Vérifier dans `index.html` servi : aucun `<script>` en ligne.
- **« can not modify /etc/nginx/conf.d/default.conf (read-only file system?) »** au démarrage : simple information du script d'entrée de l'image (il voulait ajouter l'écoute IPv6) ; le fichier appartient à root et nginx tourne en non-root. Sans effet.

**À observer soi-même**

```bash
docker build -t aaas-front:local front          # 1er build : npm ci télécharge (~1 min)
docker build -t aaas-front:local front          # 2e build : tout CACHED
# modifier un .html, rebuild : seules COPY . . et ng build sont refaites
docker image ls aaas-front                       # ≈ 61 Mo
docker run --rm -p 127.0.0.1:4300:8080 aaas-front:local   # seul, sans API : /api répond 502
curl -I http://127.0.0.1:4300/evenements/42      # 200 text/html (repli SPA) + Content-Security-Policy
curl -I http://127.0.0.1:4300/healthz            # 200
```

## 7. Flux complet d'une requête (vue d'ensemble du projet)

Exemple : un membre clique sur « Se connecter ».

```
[Navigateur]  composant Login (formulaire) ── AuthApi.login() ── HttpClient ── authInterceptor
      │        (l'intercepteur ajoute « Authorization: Bearer ... » aux requêtes suivantes, une fois connecté)
      │  POST http://localhost:4200/api/auth/login   {"email":"...","password":"..."}
      ▼
[Hôte : port 4200] ──(port publié 127.0.0.1:4200:8080)──▶ [Conteneur front : nginx, utilisateur nginx]
      │        location /api/ → resolver 127.0.0.11 → api:8080 (réseau compose ; en Azure : HTTPS + CORS)
      ▼
[Conteneur api, utilisateur app] (aussi joignable directement sur le port publié 8080, pour curl)
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
- **J'ajoute un paquet NuGet : pourquoi le build Docker est-il long une fois, puis rapide ?** Le `.csproj` modifié invalide la couche du `restore` (téléchargement) ; ensuite elle est en cache tant que les fichiers de dépendances ne changent pas.
- **`docker compose down` efface-t-il ma base ?** Non, seulement les conteneurs et le réseau ; `down -v` supprime aussi les volumes.
- **`"1433:1433"` ou `"127.0.0.1:1433:1433"` ?** Le premier expose le port à tout le réseau ; le second le limite à la machine. En développement, on n'expose que ce dont l'hôte a besoin.
- **Comment servir une application Angular dans un conteneur ?** Build multi-stage : Node compile (`npm ci`, `ng build`), une image nginx non-root sert `dist/…/browser` avec un repli vers `index.html` pour les routes du client.
- **Pourquoi pas de CORS en local avec le conteneur front ?** nginx relaie `/api` vers l'API : le navigateur ne voit qu'une origine. CORS ne s'applique qu'entre origines différentes (Azure : Static Web Apps → App Service).
- **nginx renvoie 502 après avoir recréé le conteneur de l'API : pourquoi ?** nginx avait résolu `api` au démarrage ; la nouvelle IP n'est pas connue. Parade : `resolver 127.0.0.11` + variable dans `proxy_pass`.
- **Comment vérifier qu'un conteneur ne tourne pas en root ?** `docker exec <conteneur> id` (ici `uid=101(nginx)`).
