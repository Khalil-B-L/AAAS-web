# Fiche de révision — Le trajet complet : écran Angular → nginx → API .NET → SQL Server

> Pour savoir raconter en entretien « ce qui se passe quand on clique ». Chaque étape cite le fichier du projet AAAS qui la réalise. Compléments : `angular.md` (front), `docker.md` (conteneurs), `aspnet-identity-jwt.md` (jeton), `ef-core-concurrence.md` (version, verrou).

## 1. Les acteurs et les adresses

| Acteur | Local `docker compose` | Développement (`ng serve`) | Azure (S3) |
|---|---|---|---|
| Navigateur | `http://localhost:4200` | `http://localhost:4200` | `https://<site>.azurestaticapps.net` |
| Serveur du front | conteneur `front` (nginx, 8080) | serveur de dev Angular (Vite) | Static Web Apps |
| Relais `/api` | nginx → `http://api:8080` | `proxy.conf.mjs` → `http://localhost:5080` (ou `API_URL`) | aucun : appel direct, **CORS** |
| API | conteneur `api` (Kestrel, 8080) | `dotnet run` (5080) | App Service |
| Base | conteneur `sqlserver` (volume `sqlserver-data`) | idem | Azure SQL |

Le front appelle toujours un **chemin relatif** (`/api/...`, `environment.apiBaseUrl = ''`) : en local, le navigateur ne voit qu'une origine, donc pas de CORS (ADR 0017).

## 2. Un visiteur ouvre `/evenements?page=2`

```
1. Navigateur        GET http://localhost:4200/evenements?page=2
2. nginx             try_files : aucun fichier « evenements » → index.html (repli SPA)        front/nginx/default.conf
3. Navigateur        télécharge main-XXXX.js (cache 1 an) puis le chunk de la page (lazy)      app.routes.ts (loadComponent)
4. Angular démarre   AuthService relit localStorage « aaas.session », SANS appel réseau       core/auth/auth.service.ts
5. Routeur           « evenements » → EventList ; ?page=2 → input page() = 2                    withComponentInputBinding()
6. rxResource        params { page: 2 } → EventsApi.list(2)                                     features/public/event-list/event-list.ts
7. HttpClient        GET /api/events?page=2&pageSize=20 (HttpParams)                             core/api/events.api.ts
8. authInterceptor   URL commence par /api/ : ajoute Bearer si une session existe              core/auth/auth.interceptor.ts
9. nginx             location /api/ → résout « api » (127.0.0.11) → http://api:8080            front/nginx/default.conf
10. API              ExceptionHandler → CORS → Authentication (jeton facultatif ici) → EventsController.List
11. Application      PublicEventService.ListAsync : publiés ou annulés, EndsAt > maintenant (TimeProvider)
12. EF Core          SELECT … WHERE Status IN (2,3) AND EndsAt > @now ORDER BY StartsAt, Id
                     OFFSET 20 ROWS FETCH NEXT 20 ROWS ONLY  ───────▶ SQL Server
13. Domain           EventOccupancyReader (décompte groupé), RemainingSeats, IsBookable
14. Réponse          200 { items, page: 2, pageSize: 20, totalCount } (dates en UTC « +00:00 »)
15. Écran            events.value() change → @for des cartes ; date | date (fuseau +0100, locale fr) ;
                     price | tndPrice ; eventMention(e) ; <app-pager>                           shared/…
```

Points à savoir dire :
- **Où est l'heure de Tunis ?** Nulle part dans l'API ni la base (UTC partout, ADR 0003) : seulement à l'affichage (`DatePipe` à `+0100`) et à la saisie (`toTunisIso`).
- **Pourquoi la page est-elle dans l'URL ?** Lien partageable, bouton « Précédent » du navigateur, et `rxResource` relance l'appel tout seul quand `page()` change.
- **Que se passe-t-il si l'API démarre encore ?** nginx répond 502 → `toApiError` → « Le service est momentanément indisponible » + « Réessayer » (`events.reload()`).

## 3. Un membre se connecte

```
Login.submit()                markAllAsTouched ; formulaire invalide → arrêt, messages sous les champs   features/account/login/login.ts
  submitting.set(true)        bouton désactivé (anti double clic), écran mis à jour (signal, zoneless)
AuthApi.login()               POST /api/auth/login { email, password } — pas de jeton (pas de session)
  → nginx → AuthController.Login → AccountService.LoginAsync → Identity CheckPasswordSignInAsync
    (PBKDF2, compteur d'échecs, blocage 15 min) → UPDATE LastLoginAt → TokenService (JWT HS256, 60 min)
200 { accessToken, expiresAt, user }
AuthService.startSession()    décode le JWT (sub, given_name, role chaîne|tableau), échéance locale
                              = maintenant + (exp − iat) − 60 s, localStorage, minuterie           core/auth/auth.service.ts
Router.navigateByUrl(safeReturnUrl(returnUrl))   « /mon-compte » accepté, « //site.com » refusé   core/auth/return-url.ts
En-tête                       lit isLoggedIn(), firstName(), isAdmin() (signals) → « Bonjour Salma »  layout/header.html
```

Erreur 401 `invalid_credentials` : l'intercepteur **ne** redirige **pas** (la requête n'avait pas de jeton) ; l'écran affiche le `title` de l'API dans un bandeau.

## 4. Un admin enregistre une modification (concurrence)

```
AdminEventForm   GET /api/admin/events/42 → form.reset(valeurs à l'heure de Tunis), version.set("AAAAAAAAB9E=")
                 (toTunisLocalInput : '2026-12-05T09:00:00+00:00' → '2026-12-05T10:00')
« Enregistrer »  PUT /api/admin/events/42 { …, startsAt: "2026-12-05T10:00+01:00", version, confirmRegistrantsWarning: false }
  intercepteur   + Authorization: Bearer … (rôle Admin dans le jeton ; l'API revérifie le security stamp en base)
  API            AdminEventsController.Update → AdminEventService.UpdateAsync
                 IEventLock : SELECT … FROM Events WITH (UPDLOCK, ROWLOCK) WHERE Id = @id   (transaction)
                 compare la version reçue au rowversion → périmée ? 409 concurrent_update
                 Domain : Event.Update (règles R11, R12) → UPDATE Events … (rowversion change)
  réponse        200 AdminEventDto { …, version: "AAAAAAAAB+A=" }
Écran            applyEvent(réponse) : version.set(nouvelle), form.reset (pristine), « Enregistré. »
```

Les deux 409 qui demandent une réaction (ADR 0016 § 4) :
- `concurrent_update` : dialogue « Événement modifié par un autre administrateur » → « Recharger » relit la fiche (saisie perdue) ou « Fermer » (la saisie reste pour la recopier).
- `registrants_warning` (à partir de l'étape 4) : dialogue avec le texte de l'API → « Confirmer » renvoie **la même requête** (même version) avec `confirmRegistrantsWarning: true`.

**Piège classique** : oublier de remplacer la `version` après une publication ou une dépublication → l'enregistrement suivant reçoit un faux `concurrent_update`. Test : `admin-forms.spec.ts`, « publication : version remplacée ».

## 5. Une session expire

| Déclencheur | Ce qui se passe | Fichier |
|---|---|---|
| Minuterie locale (60 min − 60 s) | sur une page réservée : `/connexion?returnUrl=…&raison=expiree` ; sur une page publique : message « Votre session a expiré. Veuillez vous reconnecter. » | `auth.service.ts` |
| 401 d'une requête qui portait un jeton (compte supprimé, rôle retiré, jeton périmé) | `expire(true)` : déconnexion locale + page de connexion avec retour | `auth.interceptor.ts` |
| Déconnexion dans un autre onglet | événement `storage` → session effacée ici aussi, sortie des pages réservées | `auth.service.ts` |

Constaté en test de bout en bout : un navigateur qui gardait le jeton d'une **autre** installation (autre clé de signature) restaure la session, l'API refuse le jeton (401), l'intercepteur ferme la session et renvoie à la connexion. C'est le comportement voulu par l'ADR 0015 (pas d'appel `/me` au démarrage).

## 6. Questions d'entretien

- **Décrivez le chemin d'une requête de votre front jusqu'à la base.** Reprendre le § 2 en 6 étapes : routeur → service HTTP → intercepteur → reverse proxy → contrôleur/service/EF Core → SQL ; puis le retour et la mise à jour par signal.
- **Où vérifiez-vous les droits ?** Dans l'API (politique « authentifié par défaut », rôle `Admin`, security stamp à chaque requête). Les guards Angular ne font que du confort.
- **Comment gérez-vous deux admins qui modifient la même fiche ?** Concurrence optimiste (`rowversion` exposé en `version`), 409 côté API, dialogue « Recharger » côté front ; verrou `UPDLOCK` pour les règles qui comptent les places.
- **Pourquoi pas de CORS en local mais en Azure ?** Même origine grâce au relais nginx en local ; deux domaines en Azure (Static Web Apps et App Service).
