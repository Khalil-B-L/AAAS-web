# ADR 0001 — Architecture en couches et structure de la solution

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 10/11/2026 — mis à jour le 10/11/2026 après l'implémentation de l'étape 1 (comptes), puis après la revue de code de l'étape 1 (référence de `Aaas.Application.Tests` à Infrastructure) ; mis à jour le 13/11/2026 : déclencheur du point 8 atteint, création de `tests/Aaas.Infrastructure.Tests`
- **Références** : cahier des charges § 7, CLAUDE.md
- **Mise en œuvre** : étape 1 (structure, erreurs, comptes). `IAppDbContext` et `IEventLock` arrivent à l'étape 2.

## Contexte

Le cahier des charges impose une API ASP.NET Core découpée en couches Domain / Application / Infrastructure / Api, EF Core, SQL Server, des tests xUnit et des tests d'intégration avec WebApplicationFactory et Testcontainers. Le projet est un mini-projet d'apprentissage sur 4 semaines : il faut une structure propre et présentable en entretien, sans sur-ingénierie.

## Décision

1. **Une solution, quatre projets de production, quatre projets de tests** :

   | Projet | Rôle | Dépend de |
   |---|---|---|
   | `src/Aaas.Domain` | Entités (`Event`, `Reservation`, `News`, `AssociationSettings`), enums de statuts, objets valeur (`PhoneNumber`, `Money`), **règles métier pures** (RG-01 à RG-08), `BusinessRuleException`, `ErrorCodes`, `Roles` | rien |
   | `src/Aaas.Application` | Services de cas d'usage (`AccountService`, puis `EventService`, `ReservationService`…), DTO et leurs attributs de validation, exceptions applicatives (400, 401, 404), interfaces des ports (`IIdentityService`, `ITokenService`, puis `IAppDbContext`, `IEventLock`), options (`BootstrapOptions`, `PrivacyPolicyOptions`, puis `ReservationOptions`) | Domain ; **EF Core à partir de l'étape 2** (voir point 3) |
   | `src/Aaas.Infrastructure` | `AppDbContext`, configurations, conventions et migrations EF, `AppUser` (Identity), implémentation des ports, initialisation de la base, plus tard tâche d'expiration et Azure OpenAI | Application, Domain |
   | `src/Aaas.Api` | Controllers, authentification JWT, `ProblemDetails`, CORS, sondes de santé, composition de l'injection de dépendances, `Program.cs` | Application, Infrastructure |
   | `tests/Aaas.Domain.Tests` | Règles métier, sans base | Domain |
   | `tests/Aaas.Application.Tests` | Orchestration des services avec des doublures des ports (`IIdentityService`, `ITokenService`), validation des DTO | Application **uniquement** |
   | `tests/Aaas.Infrastructure.Tests` | Configuration et composants d'Infrastructure **sans base** : politique de mot de passe (`Identity/PasswordPolicyTests`), initialisation et premier admin (`Initialization/DatabaseInitializerTests`, avec doublures de `UserManager`, `RoleManager` et `IIdentityService`) | Infrastructure |
   | `tests/Aaas.Api.IntegrationTests` | `WebApplicationFactory<Program>` + Testcontainers SQL Server | Api |

   Le front Angular est dans `front/`, hors de la solution .NET.

2. **Controllers** (`[ApiController]`) plutôt que minimal APIs : validation automatique des DTO avec réponse `400 ValidationProblemDetails`, `[Authorize(Roles = ...)]` déclaratif, modèle connu en entretien.

3. **Pas de repository générique ni de MediatR** : les services d'Application utiliseront une interface `IAppDbContext` (les `DbSet` + `SaveChangesAsync`), implémentée par `AppDbContext`. Le `DbContext` est déjà une unité de travail et un repository. Les seuls besoins qui exigent du SQL spécifique (verrou sur un événement, ADR 0004) passent par un port dédié.
   - **État à l'étape 1** : les comptes passent entièrement par le port `IIdentityService` (Identity gère ses propres tables). Application ne référence donc **pas encore** EF Core : ses seules dépendances NuGet sont `Microsoft.Extensions.DependencyInjection.Abstractions` et `Microsoft.Extensions.Options`. La référence à `Microsoft.EntityFrameworkCore` et `IAppDbContext` seront ajoutés **à l'étape 2** (événements), quand un service d'Application aura besoin d'interroger des entités du Domain. On n'ajoute pas une dépendance avant d'en avoir l'usage.

4. **Erreurs** : chaque cas d'erreur est une exception typée, traduite en `ProblemDetails` (RFC 9457) avec l'extension `code` par un `IExceptionHandler` unique de l'Api (`ApiExceptionHandler`). Le `title` est en français et affichable tel quel.

   | Exception | Couche | HTTP | Exemple |
   |---|---|---|---|
   | validation automatique des DTO (`[ApiController]`) | Api | 400 | champ manquant, e-mail invalide, téléphone hors format |
   | `RequestValidationException` (erreurs par champ) | Application | 400 | mot de passe refusé par Identity (voir ADR 0002) |
   | `AuthenticationFailedException` | Application | 401 | identifiants incorrects, compte bloqué |
   | `NotFoundException` | Application | 404 | ressource absente **ou appartenant à un autre membre** |
   | `BusinessRuleException(code, message)` | Domain | 409 | e-mail déjà utilisé, puis règles RG, conflits de concurrence |
   | 401/403 du framework (jeton absent, rôle insuffisant) | Api | 401/403 | corps `ProblemDetails` ajouté par `UseStatusCodePages` |
   | toute autre exception | — | 500 | message générique, détail uniquement dans les journaux (sauf violation d'unicité SQL, journalisée sans l'exception : ADR 0002) |

   Les codes (`validation_failed`, `email_taken`, `invalid_credentials`, `account_locked`…) sont des constantes de `ErrorCodes` (Domain) : le front s'appuie dessus, on ne les renomme jamais.

5. **Identifiants** : `Guid` pour toutes les entités (générés en séquentiel par EF Core côté client, ce qui évite la fragmentation de l'index cluster), pour ne pas exposer d'identifiants devinables dans les URL.

6. **Outillage commun** : `global.json` (version du SDK .NET LTS), `Directory.Build.props` (nullable, implicit usings, warnings de nullabilité en erreur), `Directory.Packages.props` (gestion centralisée des versions NuGet), `dotnet-tools.json` (outil `dotnet-ef` local), solution au format `Aaas.slnx`.

7. **Démarrage** : les options marquées `ValidateOnStart` sont vérifiées **explicitement** (`IStartupValidator.Validate()`) avant l'initialisation de la base, pour qu'une clé JWT absente soit signalée comme telle et non masquée par une erreur SQL (ADR 0008). Un échec de l'initialisation est journalisé en `Critical` avec un message sûr (sans donnée personnelle) avant l'arrêt du processus.

8. **Tests unitaires d'Infrastructure** : projet `tests/Aaas.Infrastructure.Tests` (référence Infrastructure).
   - **Historique** : à l'étape 1, le seul test d'Infrastructure (`PasswordPolicyTests`, qui construit un `UserManager` à partir de `AddInfrastructure(...)` et vérifie R21 sans base) vivait dans `Aaas.Application.Tests`, avec une référence à Infrastructure **actée comme exception**. Le déclencheur de révision était l'apparition d'un **deuxième** test unitaire d'Infrastructure.
   - **Déclencheur atteint** (revue de l'étape 1) : `DatabaseInitializerTests` (premier admin, cas du compte existant, option `Bootstrap:PromoteExistingAccount`, course entre instances) est un deuxième ensemble de tests d'Infrastructure. Le projet `tests/Aaas.Infrastructure.Tests` a été créé (33 tests à ce jour), `PasswordPolicyTests` y a été déplacé (`Identity/`), et `Aaas.Application.Tests` ne référence plus que `Aaas.Application`.
   - **Règle** : un test qui a besoin de types d'Infrastructure (Identity, `AppDbContext`, `DatabaseInitializer`, conventions EF) va dans `Aaas.Infrastructure.Tests` s'il n'a pas besoin d'une vraie base, dans `Aaas.Api.IntegrationTests` sinon. `Aaas.Application.Tests` n'utilise que des doublures des ports.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Minimal APIs | Tout à fait viables (validation intégrée depuis .NET 10). Écartées pour garder un seul style, le plus répandu en entreprise ; peut faire l'objet d'un exercice de révision. |
| CQRS + MediatR | Une indirection de plus par cas d'usage sans bénéfice ici (pas de pipeline complexe, pas de lecture/écriture séparées). MediatR est en outre passé sous licence commerciale. |
| Repositories par entité | Duplication de ce que fait déjà `DbContext`, et tests moins réalistes. Les tests d'intégration sur un vrai SQL Server couvrent la persistance. |
| Domain totalement ignorant d'EF + Application sans EF | Plus « pur », mais oblige à écrire des repositories et des mappings. Compromis assumé : seule Application verra les abstractions EF (à partir de l'étape 2) ; le Domain reste sans dépendance. |
| Ajouter EF Core à Application dès l'étape 1 | Aucune utilisation à l'étape 1 : dépendance morte, et `IAppDbContext` aurait été dessiné sans cas d'usage réel. |
| Pattern `Result<T>` au lieu d'exceptions | Plus explicite, mais plus verbeux. Les violations de règles sont rares et remontent toutes jusqu'à l'API : une exception typée suffit. (Le port `IIdentityService` renvoie toutefois des résultats typés, `CreateUserResult`, parce qu'Identity signale ses erreurs par valeur de retour et non par exception.) |
| Garder les tests d'Infrastructure dans `Aaas.Application.Tests` | Acceptable pour un seul test (choix de l'étape 1), plus au-delà : Application.Tests verrait les types d'Infrastructure et pourrait s'en servir par commodité. |
| Déplacer les tests d'Infrastructure dans `Aaas.Api.IntegrationTests` | Les tests deviennent dépendants de Docker (Testcontainers) et lents, alors qu'ils n'ont besoin d'aucune base. Les tests d'intégration couvrent seulement le câblage de bout en bout (par exemple « mot de passe faible → 400 », démarrage avec compte existant). |

## Conséquences

- Les règles métier sont testables sans base (`Aaas.Domain.Tests`), ce qui est la priorité de test n°1.
- À partir de l'étape 2, Application dépendra de `Microsoft.EntityFrameworkCore` : à expliquer en entretien comme un compromis pragmatique.
- `AppUser` (Identity) vit dans Infrastructure ; le Domain ne connaît que des `UserId` (`Guid`). Les règles qui portent sur les comptes (RG-09, RG-11, R20, R21) sont appliquées dans Application, via `IIdentityService`, avec l'objet valeur `PhoneNumber` du Domain comme source unique de la règle du téléphone.
- Quatre projets de tests à exécuter en CI (S3) ; seul `Aaas.Api.IntegrationTests` a besoin de Docker.
- Les dépendances des projets de tests suivent celles des couches : à vérifier en revue de code (aucune `ProjectReference` à Infrastructure dans `Aaas.Application.Tests`).

## Reste à valider par le porteur

- La structure et les choix ci-dessus (aucune question fonctionnelle en suspens sur cet ADR).
