# ADR 0015 — Session côté front : jeton JWT, intercepteur, guards, expiration

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 20/11/2026
- **Mise en œuvre** : semaine 2, tâche T3 du plan [`plan-front-angular.md`](../technique/plan-front-angular.md)
- **Références** : ADR 0002 (JWT HS256 60 min sans refresh, `localStorage`, claims `sub`, `given_name`, `role`, `sst`) ; mvp US-A2, US-A3 (« Session expirée ») ; architecture § 6 ; cahier § 6.2

## Contexte

L'API délivre un jeton porteur (`Authorization: Bearer`) valable 60 minutes, sans jeton de rafraîchissement (ADR 0002). Le jeton contient `sub`, `given_name`, `jti`, `sst` et un claim `role` par rôle ; ni e-mail ni téléphone. L'ADR 0002 a déjà retenu `localStorage`. Restent à décider : ce qu'on stocke, comment on restaure la session au chargement, comment on gère l'expiration, les 401/403 et les redirections.

L'architecture (§ 6, version 7) prévoyait un appel `GET /api/account/me` au démarrage pour « confirmer la session ». Cet appel bloquerait l'affichage de toutes les pages (y compris publiques) et échouerait pendant les secondes où l'API démarre dans `docker compose` (nginx renvoie 502).

## Décision

1. **Stockage** : une seule entrée `localStorage` `aaas.session` = `{ accessToken, localDeadline }`. Rien d'autre : pas de profil (nom, e-mail, téléphone) dans le navigateur (données minimales, loi 2004-63).
2. **Restauration synchrone** au démarrage, **sans appel réseau** : `AuthService` lit `aaas.session`, ignore et efface une session dont `localDeadline` est dépassée, et **décode la charge utile** du JWT (Base64url, quelques lignes, sans bibliothèque) pour obtenir `sub`, `given_name` et `role`. Le claim `role` peut être une **chaîne** (un rôle) ou un **tableau** (plusieurs) : les deux sont normalisés en tableau. La signature n'est **pas** vérifiée côté front : ces informations ne servent qu'à l'affichage (menu, prénom) et aux guards ; la sécurité réelle reste vérifiée par l'API à chaque requête (révocation par security stamp comprise).
3. **Signals de session** : `session = signal<Session | null>`, `isLoggedIn = computed(...)`, `isAdmin = computed(() => roles.includes('Admin'))`, `firstName = computed(...)`. « Mon compte » appelle `GET /api/account/me` à l'ouverture de la page seulement.
4. **Échéance locale** : à la connexion, `localDeadline = Date.now() + (exp − iat) × 1000 − 60 s` (durée de vie lue dans le jeton, repli sur 60 min si `iat` manque). On ne compare jamais `exp` à l'horloge du poste : un téléphone mal réglé de deux heures rendrait sinon toute connexion « expirée » dès sa création. Une minuterie (`setTimeout`) déconnecte à l'échéance avec le message « Votre session a expiré. Reconnectez-vous. ».
5. **Intercepteur fonctionnel `authInterceptor`** (`withInterceptors([...])`) :
   - ajoute `Authorization: Bearer` **uniquement** aux requêtes vers l'API (URL qui commence par `environment.apiBaseUrl + '/api/'`) et seulement si la session est valide ;
   - sur une réponse **401 d'une requête qui portait un jeton** : déconnexion locale, message « session expirée », redirection vers `/connexion?returnUrl=<page courante>`. Un 401 sans jeton (mauvais mot de passe, `invalid_credentials`, `account_locked`) est laissé au composant ;
   - ne gère **pas** les autres erreurs (le composant les affiche, ADR 0016).
6. **Guards** (fonctions) :
   - `authGuard` (`canActivate`) : pages membres (`/mon-compte`, plus tard `/mes-reservations`) ; non connecté → `UrlTree` vers `/connexion?returnUrl=...` ;
   - `adminGuard` (`canMatch` sur le chemin `admin`) : non connecté → connexion ; connecté sans rôle `Admin` → page « Accès réservé aux administrateurs ». `canMatch` empêche aussi le téléchargement du code admin ;
   - `guestGuard` : `/connexion` et `/inscription` redirigent vers `/evenements` si l'on est déjà connecté.
   Les guards sont du **confort** : un membre qui forcerait l'affichage d'un écran admin recevrait 403 de l'API.
7. **`returnUrl`** : accepté seulement s'il commence par `/` et pas par `//` (sinon `/evenements`) : pas de redirection ouverte vers un autre site.
8. **Déconnexion** (US-A3) : effacement de `aaas.session`, du signal et de la minuterie, retour à `/evenements`. **Aucun appel à l'API** (jeton sans état) ; le jeton reste techniquement valide jusqu'à son échéance, risque accepté par l'ADR 0002.
9. **Plusieurs onglets** : écoute de l'événement `storage` ; une déconnexion ou connexion dans un onglet est reportée dans les autres.
10. **403** reçu d'une route admin (rôle retiré entre-temps) : message « Accès réservé aux administrateurs » ; pas de déconnexion.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| `GET /api/account/me` bloquant au démarrage (`provideAppInitializer`) | Ralentit toutes les pages, y compris publiques ; échoue quand l'API démarre ou s'endort (App Service F1, Azure SQL en pause) ; le 401 de la première requête protégée suffit à détecter une session révoquée. |
| `sessionStorage` | Perdu à la fermeture de l'onglet, non partagé entre onglets : les bénévoles se reconnecteraient sans cesse. Même exposition XSS que `localStorage`. |
| Jeton en mémoire seulement | Perdu à chaque rechargement de page : inutilisable. |
| Cookie `HttpOnly` | Protège du vol par XSS, mais demande une authentification par cookie et une protection CSRF côté API, et des cookies tiers (`SameSite=None`) entre Static Web Apps et App Service, de plus en plus bloqués par les navigateurs. Contraire à l'ADR 0002. |
| Stocker aussi le profil (`AccountDto`) | Données personnelles inutiles dans le navigateur ; le prénom et les rôles sont déjà dans le jeton. |
| Bibliothèque `jwt-decode` | Dix lignes suffisent ; une dépendance de moins. |
| Comparer `exp` à l'horloge du poste | Faux si l'horloge du téléphone ou du PC est décalée. |

## Conséquences

- Le rendu des pages publiques ne dépend jamais de l'API d'authentification.
- Une session révoquée (compte supprimé, rôle retiré) n'est détectée qu'au premier appel protégé : le menu « Admin » peut rester visible jusque-là. Acceptable (confort uniquement).
- Une saisie longue (formulaire d'événement) peut être perdue si la session expire avant l'enregistrement : question UX-5 du plan (avertissement avant échéance ou non), liée à la question ouverte n° 2 de l'architecture (durée de session).
- Atténuation XSS (le risque principal de `localStorage`, ADR 0002) : aucun `innerHTML` contourné (`bypassSecurityTrust*` interdit), Content-Security-Policy servie par nginx (ADR 0017) puis par Static Web Apps (S3).
- **Architecture § 6 à mettre à jour** : supprimer « appelle `GET /api/account/me` pour confirmer la session » au démarrage.

## Reste à valider par le porteur

- Abandon de l'appel `/me` au démarrage ; comportement à l'expiration pendant une saisie (UX-5).
