# ADR 0008 — Création du premier administrateur et initialisation de la base

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 10/11/2026 — mis à jour le 10/11/2026 après l'implémentation de l'étape 1, puis après la revue de code de l'étape 1 (abandon de la promotion automatique d'un compte existant) ; mis à jour le 13/11/2026 (journalisation de la promotion en Warning, option présente dans `.env.example`, course entre instances, adresse d'admin non publiée, échecs de démarrage en Critical), puis le même jour après relecture du code final (e-mail et mot de passe de l'admin obligatoires pour `docker compose`, journalisation d'un échec de démarrage sans l'exception d'origine)
- **Références** : cahier RG-09, RG-11, § 6.2, § 6.4 ; mvp R20, R22, R24, Q11
- **Mise en œuvre** : étape 1 (`src/Aaas.Infrastructure/Initialization/DatabaseInitializer.cs`, `src/Aaas.Api/Program.cs`, `.env.example`, `docker-compose.yml`, procédure SQL dans `docs/technique/exploitation.md`, tests dans `tests/Aaas.Infrastructure.Tests/Initialization/DatabaseInitializerTests.cs` et `tests/Aaas.Api.IntegrationTests/Infrastructure/StartupTests.cs`)

## Contexte

Personne ne peut s'inscrire en tant qu'admin. Le premier admin est créé à l'installation, les suivants sont désignés manuellement au MVP, et il doit toujours rester au moins un admin. Aucun secret (dont le mot de passe initial) ne doit se trouver dans le dépôt, qui est **public**.

La revue de code de l'étape 1 a relevé une faille dans la première version de cette décision : si un compte membre existait déjà avec l'e-mail `Bootstrap:AdminEmail`, il était **promu admin automatiquement**. Or l'inscription est publique et l'adresse visée était prévisible (la première version de `.env.example`, versionnée dans un dépôt public, proposait une adresse d'exemple que beaucoup d'installations auraient gardée). Un tiers qui s'inscrit avec cette adresse **avant** le premier démarrage configuré devient administrateur, avec son propre mot de passe. L'inscription ne vérifiant pas la possession de l'e-mail (pas de confirmation au MVP), rien ne prouve que ce compte appartient à l'association.

## Décision

**Initialisation idempotente au démarrage de l'API**, pilotée par la configuration (`DatabaseInitializer`, hébergé dans Infrastructure et appelé depuis `Program.cs` avant `app.RunAsync()`, juste après la validation explicite des options, ADR 0001).

1. **Migrations** : si `Database:MigrateOnStartup = true` (en local et dans `docker compose`), `Database.MigrateAsync()`. La valeur par défaut d'`appsettings.json` est `false` : en Azure, la CI applique les migrations avec un *migration bundle* EF (`dotnet ef migrations bundle`) avant le déploiement.
2. **Rôles** : création des rôles `Admin` et `User` s'ils n'existent pas (`EnsureRolesAsync`). Si une autre instance crée le même rôle au même moment, le conflit est toléré (le rôle existe, c'est le résultat voulu) et le démarrage continue.
3. **Premier admin** : si **aucun** utilisateur n'a le rôle `Admin` **et** que `Bootstrap:AdminEmail`, `Bootstrap:AdminPassword`, `Bootstrap:AdminFirstName`, `Bootstrap:AdminLastName` et `Bootstrap:AdminPhone` sont renseignés, création du compte avec **les rôles `User` et `Admin`** (un admin garde les droits d'un membre, ADR 0002), par le même port `IIdentityService` que l'inscription, donc avec les mêmes règles :
   - **`Bootstrap:AdminPhone` au même format que les membres** (RG-11) : « +216 » + 8 chiffres, espaces tolérés, stocké en E.164 ;
   - mot de passe soumis aux règles d'Identity (R21) ;
   - `CreatedAt` = `PrivacyConsentAt` = date du démarrage, `PrivacyPolicyVersion` = `PrivacyPolicy:CurrentVersion` (« 1.0 ») : l'admin est un compte comme les autres au regard des données personnelles (§ 6.4). `LastLoginAt` est initialisée à la même date (même code que l'inscription ; sans conséquence pour R25, qui retombe de toute façon sur `CreatedAt`).
   - Cas particuliers :
     - s'il existe déjà un admin : rien n'est fait, même si la configuration est présente (aucune recherche de l'e-mail n'a lieu) ;
     - s'il n'existe aucun admin et que la configuration est incomplète : avertissement dans les journaux (liste des clés manquantes), l'API démarre quand même. Ce cas ne concerne que `dotnet run` et Azure : avec `docker compose`, l'e-mail et le mot de passe sont **obligatoires** (point 4) et compose refuse de lancer les conteneurs s'ils manquent ;
     - **s'il n'existe aucun admin et qu'un compte existe déjà avec `Bootstrap:AdminEmail`** : par défaut, l'API **refuse de démarrer** (`InvalidOperationException`) avec un message explicite, **sans reproduire l'e-mail** (point 4), du type : « Un compte existe déjà avec Bootstrap:AdminEmail et aucun administrateur n'existe. Il n'est pas promu automatiquement : promouvez-le manuellement (docs/technique/exploitation.md § 6), ou, après avoir vérifié que ce compte vous appartient, définissez Bootstrap:PromoteExistingAccount=true. » ;
     - **option explicite `Bootstrap:PromoteExistingAccount = true`** (défaut `false` ; présente dans `.env.example` sous la forme `BOOTSTRAP_PROMOTE_EXISTING_ACCOUNT=false`, avec un commentaire qui renvoie à la procédure, et transmise par `docker-compose.yml` avec `false` par défaut) : rétablit l'ancien comportement pour ce seul cas. Le compte existant est promu (ajout du rôle `Admin`, il garde `User`), sans toucher à son mot de passe ni à ses données, puis son `SecurityStamp` est renouvelé pour révoquer ses jetons émis sans le rôle. La promotion est journalisée en **`Warning`** (trace d'audit : une élévation de privilèges a eu lieu au démarrage), avec l'identifiant du compte et sans l'e-mail. À n'utiliser qu'après avoir vérifié que le compte appartient bien à l'association, avec **une seule instance** de l'API démarrée (voir la course ci-dessous), puis à remettre à `false` ;
     - **configuration invalide** (téléphone hors format, mot de passe trop faible ou reprenant une valeur d'exemple publique, e-mail invalide) : l'API **refuse de démarrer** avec un message explicite (`InvalidOperationException`), plutôt que de tourner sans admin.
   - **Deux instances qui démarrent en même temps** (plusieurs instances App Service, redémarrage en chevauchement) :
     - **création des rôles** : gérée (point 2) ;
     - **création du premier admin** : gérée. La seconde instance reçoit « e-mail déjà utilisé » (index unique) à la création ; elle vide le suivi de modifications d'EF, **revérifie qu'un admin existe** (créé par la première instance) et ne fait rien ; si aucun admin n'existe (compte créé entre-temps par une inscription), elle applique la règle du compte existant (arrêt, ou promotion si l'option est activée) ;
     - **promotion d'un compte existant** (option activée) : **non gérée**. Deux instances peuvent tenter d'ajouter le rôle `Admin` au même compte ; la seconde échoue (rôle déjà attribué) et s'arrête, ou les deux renouvellent le `SecurityStamp`. Aucun risque d'élévation supplémentaire (c'est le même compte), mais un démarrage en échec possible. D'où la consigne : **avec `PromoteExistingAccount = true`, une seule instance**. Gérer ce cas coûterait du code pour une opération manuelle et ponctuelle.
   - **Échec au démarrage** : `Program.cs` intercepte toute exception de l'initialisation (sauf l'annulation), la journalise en **`Critical`** puis arrête le processus. Deux cas (détail et justification dans l'ADR 0002, « Journaux ») :
     - **`InvalidOperationException` « nue »** (type exact, sans `InnerException`) : ce sont les erreurs de configuration levées par `DatabaseInitializer` (ou par EF), dont les messages sont rédigés sans donnée personnelle. Journalisée **avec** l'exception, puis relancée telle quelle (`throw;`) : message et pile d'origine conservés ;
     - **toute autre exception** (SQL, EF, `InvalidOperationException` qui enveloppe une erreur SQL) : son message peut contenir un e-mail (violation d'unicité). Seuls un message fixe, le **type** de l'exception et le **numéro d'erreur SQL** sont journalisés ; puis une `InvalidOperationException` **neutre, sans `InnerException`**, est relancée. Sans cela, le runtime .NET écrirait l'exception non gérée (message SQL compris) sur la sortie d'erreur, donc dans `docker compose logs api`. **Écart assumé** : la pile d'appels d'origine est perdue dans ce cas (voir Alternatives).
4. **Valeurs** : user-secrets en local, fichier `.env` (non versionné, modèle dans `.env.example`) pour `docker compose`, Key Vault en Azure.
   - Dans `.env.example`, **`BOOTSTRAP_ADMIN_EMAIL` et `BOOTSTRAP_ADMIN_PASSWORD` sont vides** (comme `JWT_SIGNING_KEY` et `MSSQL_SA_PASSWORD`, ADR 0002) ; `docker-compose.yml` les rend **obligatoires** avec la syntaxe `${VAR:?message}` : `docker compose up` s'arrête avec un message explicite tant qu'elles ne sont pas renseignées. Sans cela, l'API démarrerait sans admin, avec un simple avertissement facile à manquer.
   - **Pourquoi le mot de passe est vide** : un mot de passe d'exemple publié dans un dépôt public serait utilisable par n'importe qui sur une installation qui l'aurait gardé (le code refuse en plus les valeurs d'exemple connues).
   - **Pourquoi l'e-mail est vide** : ce n'est pas un secret, mais l'adresse réellement utilisée **ne doit pas être publiée**, et aucune valeur par défaut n'est proposée. Une adresse connue d'avance permet à un tiers de s'inscrire avec elle avant le premier démarrage et de **bloquer l'installation** (déni de service, voir Conséquences).
   - `BOOTSTRAP_ADMIN_FIRSTNAME`, `BOOTSTRAP_ADMIN_LASTNAME`, `BOOTSTRAP_ADMIN_PHONE` ont des valeurs d'exemple sans enjeu (le téléphone, au format RG-11, entre guillemets à cause des espaces) ; `BOOTSTRAP_PROMOTE_EXISTING_ACCOUNT=false`.
   - Une fois le premier admin créé, les valeurs `Bootstrap:*` ne sont plus lues ; en Azure, elles peuvent être retirées. Avec `docker compose`, l'e-mail et le mot de passe doivent rester renseignés (contrainte `:?`), mais sont ignorés.
   - Les journaux ne contiennent jamais l'e-mail ni le mot de passe de l'admin, y compris dans les messages d'erreur ci-dessus.
5. **Admins suivants et promotion manuelle** (MVP) : procédure manuelle documentée dans `docs/technique/exploitation.md` (§ 6). C'est aussi la procédure recommandée quand le démarrage s'est arrêté parce que le compte `Bootstrap:AdminEmail` existait déjà. La personne crée un compte (ou il existe déjà et l'exploitant a vérifié qu'il lui appartient), puis un script SQL l'ajoute au rôle `Admin` **et** renouvelle son `SecurityStamp` (pour que son jeton actuel soit refusé et qu'elle se reconnecte avec le nouveau rôle). Le script s'exécute avec **`sqlcmd -I`** :
   - **pourquoi `-I`** : sans cette option, `sqlcmd` ouvre sa session avec `QUOTED_IDENTIFIER OFF` ; or SQL Server exige `ON` pour modifier une table qui a des **index filtrés**, ce qui est le cas d'`AspNetUsers` (`UX_AspNetUsers_NormalizedEmail`, `UserNameIndex`) : erreur 1934. SSMS et EF Core activent ce réglage par défaut, `sqlcmd` non ;
   - retirer un admin suit le même principe, **après avoir vérifié qu'il en reste un autre** (RG-09).
   La gestion des admins depuis l'interface est prévue « plus tard ».

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| **Promouvoir automatiquement un compte existant portant l'e-mail `Bootstrap:AdminEmail`** (première version de cet ADR) | Prise de contrôle possible : l'inscription est publique, l'adresse est prévisible (dépôt public) et sa possession n'est pas vérifiée. Un tiers inscrit avant le premier démarrage configuré devenait admin avec son propre mot de passe. Conservée uniquement derrière l'option explicite `Bootstrap:PromoteExistingAccount`, désactivée par défaut. |
| Ignorer le compte existant et démarrer sans admin (simple avertissement) | L'API tourne sans admin (RG-09 non respectée) et l'avertissement passe facilement inaperçu dans les journaux d'App Service. Un arrêt explicite est plus visible et force une décision. |
| Supprimer ou écraser le compte existant (nouveau mot de passe) | Destruction silencieuse de données d'un tiers ou d'un vrai membre ; et l'attaquant garderait l'accès si seul le rôle était en cause. |
| Promouvoir seulement si l'e-mail du compte est confirmé | Pas de confirmation d'e-mail au MVP : la condition ne serait jamais remplie. À reconsidérer si la confirmation d'e-mail est ajoutée. |
| Gérer aussi la course sur la promotion (verrou applicatif `sp_getapplock`, ou tolérance de « rôle déjà attribué ») | Code supplémentaire pour une opération manuelle et exceptionnelle ; la consigne « une seule instance » suffit. |
| E-mail d'admin d'exemple dans `.env.example`, ou e-mail et mot de passe facultatifs dans `docker-compose.yml` (`:-`) | Démarrage plus rapide, mais une adresse publiée est prévisible (blocage de l'installation), et une valeur absente fait démarrer l'API sans admin. L'obligation `:?` coûte une ligne à remplir. |
| Au démarrage, relancer l'exception d'origine (`throw;`) dans tous les cas | Pile d'appels conservée, mais le runtime écrit l'exception non gérée sur la sortie d'erreur : le message SQL (et l'e-mail qu'il peut contenir) finirait dans `docker compose logs`. Retenue seulement pour les `InvalidOperationException` de configuration (ADR 0002). |
| `HasData` dans une migration EF | Le hachage du mot de passe (ou pire, le mot de passe) finirait dans le dépôt public ; de plus le hachage est différent à chaque calcul. |
| Le premier inscrit devient admin | Course dangereuse : sur un site public, n'importe qui peut s'inscrire avant l'association. |
| Commande en ligne `dotnet Aaas.Api.dll create-admin` | Propre, mais peu pratique sur App Service (console SSH) et dans un conteneur ; à envisager pour les admins suivants si la procédure SQL est trop pénible. |
| Endpoint « installer » protégé par un code | Surface d'attaque supplémentaire en production. |
| Premier admin avec le seul rôle `Admin` | Il ne pourrait pas réserver comme un membre, et un membre promu aurait, lui, les deux rôles : incohérent. |
| Accepter un format de téléphone plus souple pour l'admin | Deux règles pour la même donnée ; le numéro de l'admin s'affiche et se compose comme celui d'un membre. |

## Conséquences

- Démarrage de l'API idempotent : relancer `docker compose up` ne crée jamais un second admin.
- Avec `docker compose`, impossible de démarrer sans avoir choisi l'e-mail et le mot de passe de l'admin : le premier `docker compose up` échoue tant que `.env` n'est pas complété (message explicite de compose, avant toute création de conteneur).
- Un tiers qui s'inscrit avec l'adresse de l'admin avant l'installation ne peut plus obtenir le rôle : au pire, il **bloque le démarrage** jusqu'à ce que l'exploitant tranche (déni de service à l'installation, sans élévation de privilèges). Pour l'éviter : choisir une **adresse d'admin non publiée** (jamais celle d'un exemple ou d'une documentation publique), et démarrer l'API configurée **avant** d'ouvrir le site au public.
- Une erreur de saisie dans `.env` (par exemple `BOOTSTRAP_ADMIN_PHONE=20123456` sans `+216`) arrête l'API au démarrage, avec une entrée `Critical` : `docker compose logs api` affiche le format attendu.
- Une erreur SQL ou EF au démarrage (base injoignable, migration en échec, violation d'unicité) n'affiche dans les journaux que son type et son numéro SQL : le diagnostic passe par ces deux indices (et, si besoin, par une reproduction locale), pas par la pile d'appels, perdue dans ce cas.
- La promotion via l'option laisse une trace `Warning` dans les journaux, qui permet de la retrouver lors d'un audit.
- RG-09 est respectée à la création ; elle est ensuite protégée par le refus de supprimer le dernier admin (ADR 0007) et par la vérification manuelle dans la procédure SQL.
- Les tests d'intégration configurent `Bootstrap:*` via `WebApplicationFactory` pour disposer d'un admin de test ; les cas limites (compte existant, option, course entre instances) sont couverts sans base par `DatabaseInitializerTests` (doublures de `UserManager`, `RoleManager`, `IIdentityService`, ADR 0001 point 8).
- Points à tester (agent `testeur`) :
  - compte existant + aucun admin + option absente → le démarrage échoue, message sans l'e-mail, entrée `Critical`, compte inchangé (pas de rôle `Admin`) ;
  - même situation avec `PromoteExistingAccount = true` → rôles `[User, Admin]`, mot de passe inchangé, `SecurityStamp` changé, ancien jeton refusé, entrée `Warning` avec l'identifiant et sans l'e-mail ;
  - admin déjà présent + compte existant → rien, démarrage normal ;
  - rôle créé par une autre instance entre la vérification et la création → démarrage normal ;
  - e-mail pris pendant la création par une autre instance qui a créé l'admin → démarrage normal, aucun doublon ;
  - `.env` sans `BOOTSTRAP_ADMIN_EMAIL` ou sans `BOOTSTRAP_ADMIN_PASSWORD` → `docker compose config` / `up` échoue avec le message de compose ;
  - exception SQL au démarrage (par exemple base injoignable) → entrée `Critical` avec le type et le numéro SQL, sans message SQL ; l'exception finale (sortie d'erreur du processus) est une `InvalidOperationException` sans `InnerException` ;
  - `InvalidOperationException` de configuration (téléphone invalide) → entrée `Critical` avec l'exception, message d'origine conservé.

## Reste à valider par le porteur

- **Admins suivants par script SQL** au MVP (R22, Q11) : suffisant, ou faut-il avancer la commande `create-admin` ?
- **Arrêt du démarrage** quand l'adresse de l'admin est déjà prise (plutôt que promotion automatique) : à confirmer.
