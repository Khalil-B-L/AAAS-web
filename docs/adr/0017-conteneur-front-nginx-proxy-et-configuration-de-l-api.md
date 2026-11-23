# ADR 0017 — Conteneur du front (nginx), proxy `/api` et adresse de l'API

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 20/11/2026
- **Mise en œuvre** : semaine 2, tâches T0 (proxy de développement) et T1 (Docker) du plan [`plan-front-angular.md`](../technique/plan-front-angular.md) ; partie Azure en S3
- **Références** : cahier § 6.2 (CORS limité au front), § 7 (Dockerfile du front, `docker compose`), § 10 point 7 ; architecture § 7, § 11 ; ADR 0002 (CSP), ADR 0014, ADR 0015 ; `docs/revision/docker.md`

## Contexte

Le front compilé est un ensemble de fichiers statiques (`index.html`, JS, CSS). Il doit appeler l'API dans trois situations :

| Situation | Front servi par | API |
|---|---|---|
| Développement | `ng serve` (`http://localhost:4200`) | `dotnet run` (`http://localhost:5080`) ou conteneur (`http://localhost:8080`) |
| `docker compose` | conteneur `front` | conteneur `api` (`api:8080` sur le réseau compose) |
| Azure (S3) | Static Web Apps (offre gratuite, sans backend lié) | App Service, autre domaine |

L'API autorise déjà CORS pour `Cors:AllowedOrigins` (en-têtes `Authorization` et `Content-Type`). Le `.dockerignore` racine exclut `front/` (contexte de l'API). Une application Angular à routes « propres » (`/evenements/42`) exige que le serveur renvoie `index.html` pour toute URL inconnue, sinon un rechargement de page donne 404.

## Décision

1. **Même origine en local** : le front appelle l'API en **chemin relatif** (`/api/...`), `environment.apiBaseUrl = ''`. Un relais redirige `/api` vers l'API :
   - en développement : `proxy.conf.mjs` du serveur de développement Angular, cible `process.env.API_URL ?? 'http://localhost:5080'` (`API_URL=http://localhost:8080` quand l'API tourne dans compose) ;
   - dans compose : **nginx** du conteneur `front`, `location /api/` → `http://api:8080`.
   Le navigateur ne voit qu'une origine : **pas de CORS en local**. La règle CORS de l'API ne servira qu'en Azure.
2. **Azure (S3)** : un fichier `environment.azure.ts` (remplacement de fichier à la compilation) fixera `apiBaseUrl` à l'URL d'App Service, qui n'est pas un secret. Static Web Apps sert les fichiers sans nginx : le repli sur `index.html` et les en-têtes de sécurité seront recopiés dans `staticwebapp.config.json`. **L'image Docker du front sert au local et à la CI, pas à Azure.**
3. **Dockerfile multi-stage** `front/Dockerfile`, contexte `front/` (avec son propre `front/.dockerignore` : `node_modules/`, `dist/`, `.angular/`, `coverage/`) :
   - étape `build` : image `node:24-alpine` (tag précis épinglé à la mise en œuvre), `COPY package.json package-lock.json`, `RUN npm ci`, puis `COPY . .` et `RUN npx ng build` (configuration `production`) ;
   - étape finale : `nginxinc/nginx-unprivileged` (version stable, Alpine, tag précis) : nginx **non-root**, à l'écoute sur **8080** ; on copie `dist/front/browser/` et la configuration nginx. Ni Node, ni sources, ni `node_modules` dans l'image finale.
4. **Configuration nginx** (`front/nginx/default.conf`) :
   - `try_files $uri $uri/ /index.html;` : repli de l'application monopage ;
   - `/api/` relayé vers l'API avec `resolver 127.0.0.11 valid=10s` (DNS interne de Docker) et une variable d'amont, pour que nginx retrouve l'API quand son conteneur est recréé avec une autre adresse IP (sinon 502 jusqu'au redémarrage du front) ; `client_max_body_size` raisonnable, en-têtes `X-Forwarded-*` transmis ;
   - cache : `index.html` jamais mis en cache (`no-cache`), fichiers à empreinte (`main-ABC123.js`) en cache long `immutable` ;
   - compression `gzip` des JS/CSS/JSON ;
   - en-têtes de sécurité : `Content-Security-Policy` (`default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`. `style-src 'unsafe-inline'` est nécessaire aux styles de composants Angular ; l'**intégration du CSS critique** est désactivée dans `angular.json` (`optimization.styles.inlineCritical: false`), car elle ajoute un gestionnaire `onload` en ligne que la CSP bloquerait. Polices et icônes **hébergées localement** (pas de Google Fonts : CSP stricte et aucune requête vers un tiers) ;
   - `location = /healthz` qui répond 200 sans journal, pour le healthcheck.
5. **Service compose `front`** :
   - `build: { context: ./front }`, `image: aaas-front:local` ;
   - `ports: "127.0.0.1:4200:8080"` (poste local uniquement, comme les autres services) ;
   - `depends_on: api` simple (l'API n'a pas de healthcheck, architecture § 7) : pendant les quelques secondes de démarrage de l'API, `/api` renvoie 502, ce que le front affiche comme « service momentanément indisponible » (ADR 0016) ;
   - `healthcheck` : `wget -q --spider http://127.0.0.1:8080/healthz` (BusyBox présent dans l'image Alpine).
   Conséquence : `ng serve` et le conteneur `front` utilisent le même port 4200 et ne tournent pas en même temps (choix simple ; le conteneur sert à vérifier la version compilée, `ng serve` au développement).

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Appel direct de l'API avec CORS en local (`http://localhost:8080`) | Fonctionne (l'API l'autorise déjà), mais demande l'URL de l'API dans le front local et masque la différence local / Azure ; le relais reproduit le mode « même origine » et évite les erreurs CORS en développement. |
| Configuration lue à l'exécution (`/config.json` chargé au démarrage) | Une même image pour tous les environnements, mais un appel et un état de chargement de plus ; inutile tant que l'image Docker du front ne va pas en Azure. |
| Image `nginx:alpine` officielle | Processus maître en root et port 80 ; l'image non-root applique le même principe que l'API (`$APP_UID`). |
| Servir le front par l'API (`UseStaticFiles`) | Un seul conteneur, mais contraire au cahier (Static Web Apps pour le front) et mélange les cycles de build .NET et Node. |
| `proxy_pass http://api:8080;` sans `resolver` | Plus court, mais nginx résout le nom une seule fois au démarrage : après `docker compose up -d --build api`, 502 jusqu'au redémarrage du front. |
| Pas de healthcheck sur `front` | Aucun service n'en dépend ; mais il coûte une ligne, rend `docker compose ps` lisible et sert d'exemple de healthcheck HTTP (impossible sur l'API faute de `wget`). |

## Conséquences

- Le code Angular ne contient aucune URL absolue en S2 ; seul `environment.azure.ts` en aura une en S3.
- La variable `Cors__AllowedOrigins__0: http://localhost:4200` de `docker-compose.yml` n'est plus nécessaire en local (même origine) mais reste inoffensive ; on la garde pour pouvoir tester un appel direct.
- Le contexte de build du front est `front/` : le `.dockerignore` racine (qui exclut `front/` pour l'API) n'intervient pas ; c'est `front/.dockerignore` qui compte.
- La CSP impose de ne charger ni police ni script externe ; tout ajout futur (assistant RAG, S4) passe par l'API.
- En S3 : `staticwebapp.config.json` (`navigationFallback`, `globalHeaders` avec la même CSP, `connect-src` élargi à l'URL d'App Service) et `Cors:AllowedOrigins` = URL de Static Web Apps.

## Reste à valider par le porteur

- Port 4200 partagé entre `ng serve` et le conteneur (sinon publier le conteneur sur `127.0.0.1:8081`).
