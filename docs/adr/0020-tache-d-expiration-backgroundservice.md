# ADR 0020 — Tâche d'expiration : `BackgroundService`, horloge, plusieurs instances, sommeil de la base

- **Statut** : Accepté (porteur, 24/11/2026)
- **Date** : 24/11/2026
- **Mise en œuvre** : tranche « Réservations », tâche C (`plan-reservations.md`)
- **Références** : cahier RG-04, § 6.5 (coûts), O4 ; mvp R6, US-D5 ; **précise** l'ADR 0005 § 5 ; ADR 0002 (journalisation sans donnée personnelle), ADR 0003 (`TimeProvider`) ; `architecture.md` § 5 (sondes qui ne réveillent pas la base)

## Contexte

L'ADR 0005 garantit l'exactitude **sans** tâche de fond : une demande échue n'est jamais comptée (filtre à la lecture) et elle est écrite « Expirée » sous verrou avant chaque nouvelle demande sur le même événement. La tâche périodique ne sert qu'à **ranger la base** : statut écrit pour l'historique, liste admin, « Mes réservations », statistiques.

Trois contraintes concrètes :
1. **Coût** : Azure SQL serverless (offre gratuite) se met en pause après une période sans connexion ; c'est ce qui le rend gratuit. Une requête toutes les 5 minutes l'empêcherait de dormir et consommerait le quota mensuel, exactement le piège évité pour la sonde `/health`.
2. **Plusieurs instances** : rien n'empêche deux instances de l'API (mise à l'échelle, déploiement par slot) ; chacune lance sa tâche.
3. **Robustesse** : depuis .NET 6, une exception non attrapée dans `ExecuteAsync` d'un `BackgroundService` **arrête l'hôte** (`BackgroundServiceExceptionBehavior.StopHost` par défaut) : une base indisponible une minute arrêterait l'API.

## Décision

1. **`ExpirePendingReservationsJob : BackgroundService`** (Infrastructure, `Jobs/`), enregistré par `AddHostedService`. Boucle sur un `PeriodicTimer(interval, timeProvider)` : le `TimeProvider` injecté (le même que celui des services) permet aux tests d'avancer le temps avec `FakeTimeProvider.Advance` au lieu d'attendre.
2. **Un scope DI par passage** (`IServiceScopeFactory.CreateAsyncScope()`) : `AppDbContext` est *scoped* ; un `BackgroundService` est un singleton et ne doit jamais garder un `DbContext` entre deux passages.
3. **Le travail est dans Application** : `ReservationExpiryService.ExpireDueAsync(now, ct)` exécute `ExecuteUpdateAsync` : `UPDATE Reservations SET Status = 4 WHERE Status = 1 AND ExpiresAt <= @now` (disponible dans le paquet `Microsoft.EntityFrameworkCore` en version 10, sans référence à `Relational`). Requête **idempotente** : la rejouer, ou la lancer depuis deux instances en même temps, ne change rien de plus (`WHERE Status = 1`). Pas d'élection de « leader » ni de verrou applicatif (`sp_getapplock`).
4. **Ne pas réveiller la base inutilement** : la tâche ne touche la base que si l'API a répondu **avec succès (code < 400) à au moins une requête `/api/...` depuis le passage précédent**. Un petit middleware (`Aaas.Api`) lève un drapeau dans un singleton `ApiActivity` après la réponse ; le job le consomme (voir « Mise en œuvre » : drapeau et non heure de dernière requête). Sans visiteur, la base dort ; dès qu'un visiteur arrive, la base est de toute façon réveillée par sa requête et le rangement se fait au passage suivant. Ne comptent pas comme activité : les sondes `/health` et les réponses 4xx/5xx (un robot qui balaie `/api/...` en anonyme reçoit des 401 ou 404 et ne doit pas réveiller Azure SQL serverless). Désactivable (`Reservations:ExpirationJob:OnlyWhenActive=false`) pour le local si on veut observer la tâche sans naviguer.
5. **Journalisation** (ADR 0002, 0005) :
   - succès avec au moins une ligne : `Information` « {Count} demande(s) passée(s) en Expirée » — un nombre, **aucun identifiant de membre, aucun e-mail, aucun téléphone** ; rien si 0 ligne ;
   - échec : chaque passage est dans un `try/catch` ; `OperationCanceledException` due à l'arrêt → sortie normale ; toute autre exception → `Error` journalisée **sans le message SQL** si `SqlServerErrors.TryGetSqlErrorNumber` est vrai, c'est-à-dire pour toute `SqlException` de la chaîne (numéro et type seulement ; élargi en mise en œuvre, T-C6), avec l'exception sinon ; puis la boucle **continue** au passage suivant ;
   - jamais de « `catch` vide » ; jamais d'exception qui sort de `ExecuteAsync`.
6. **Configuration** (`appsettings.json`, section `Reservations`, validée au démarrage) : `PendingLifetime` = `2.00:00:00` (48 h), `ConfirmedCancellationDeadline` = `1.00:00:00` (24 h), `ExpirationJob:Interval` = `00:05:00` (bornes : 1 min à 1 h), `ExpirationJob:Enabled` = `true`, `ExpirationJob:OnlyWhenActive` = `true`. Les tests d'intégration désactivent la tâche par défaut (déterminisme) et l'activent dans les tests qui la visent.
7. **Premier passage** au démarrage, puis toutes les `Interval`. Par défaut (`OnlyWhenActive=true`), ce premier passage ne fait rien : aucune requête n'a encore été reçue, le drapeau est à 0. C'est voulu : un redémarrage (déploiement, recyclage) ne réveille pas la base. Sur App Service F1 (sans « Always On »), l'application s'endort après une vingtaine de minutes sans requête : la tâche s'arrête avec elle, ce qui est sans conséquence (point de contexte : exactitude assurée par la lecture).

## Mise en œuvre (24/11/2026)

Écarts et précisions par rapport à la décision, relevés en tâche C (`etat-avancement.md` § 1.9) et en revue :

- **T-C1 — drapeau au lieu de l'heure de dernière requête.** `ApiActivity` contient un `int` : le middleware écrit 1 (`Volatile.Write`), le job lit et remet à 0 en une opération (`Interlocked.Exchange`, `TryConsume`). La question est « une requête depuis le passage précédent ? », pas « depuis quand » : un drapeau ne dépend d'aucune horloge (réelle ou simulée) et une requête arrivée pendant un passage n'est pas perdue.
- **T-C2 — service toujours enregistré.** `AddHostedService` est appelé sans condition ; `Enabled` est lu dans `StartAsync` (via `IOptions`, valeur validée). Désactivé : journal 5002, aucun timer, aucun accès à la base. Lire la configuration pendant l'enregistrement ne verrait pas les valeurs ajoutées par `WebApplicationFactory`.
- **T-C6 — aucun message SQL dans les journaux.** Dès qu'une `SqlException` est dans la chaîne des exceptions (pas seulement 2601/2627/2628), seuls le type et le numéro SQL sont journalisés (5004), sans l'exception ; les autres exceptions sont journalisées complètes (5005).
- **T-C7 — activité non remise après un échec.** Le drapeau est consommé avant le travail ; si le passage échoue, il n'est pas relevé : le passage suivant ne réessaie que s'il y a eu une nouvelle requête. Une panne persistante ne réveille donc pas la base toutes les 5 minutes ; l'exactitude ne dépend pas de la tâche.
- **Activité = réponse < 400 sur `/api/`** (revue) : le middleware appelle `next` puis teste `Response.StatusCode < 400`. Placé avant `UseExceptionHandler`, il voit le 500 écrit par celui-ci.
- **Annulation SqlClient** (revue) : à l'arrêt de l'hôte, SqlClient lève souvent une `SqlException` (« Operation cancelled by user », n° 0) plutôt qu'une `OperationCanceledException`. Un `catch (Exception) when (stoppingToken.IsCancellationRequested)` la convertit en `OperationCanceledException` : arrêt normal, pas de journal `Error`.
- **Deadlock 1205 (théorique)** : l'`UPDATE` global de la tâche et une transaction sous verrou d'événement (demande, modification, confirmation) touchent les mêmes lignes de `Reservations` dans un ordre qui n'est pas garanti ; SQL Server peut choisir l'une comme victime. C'est une erreur transitoire, rejouée par `EnableRetryOnFailure` (stratégie d'exécution d'EF) ; si les reprises s'épuisent côté tâche, le passage est journalisé (5004, erreur 1205) et le suivant réessaie. Côté écritures par entité suivie, un conflit sur une demande écrite Expirée par la tâche donne `DbUpdateConcurrencyException` (`rowversion`) : rejeu unique dans `AdminEventService.UpdateAsync`, `reservation_state_changed` à la confirmation.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Passage toutes les 5 min sans condition | Empêche la pause d'Azure SQL serverless (coût, objectif O4) ; même raisonnement que la sonde `/health`. |
| Intervalle long (1 h) sans condition | Avec un délai de pause d'une heure, la base ne dormirait quasiment jamais ; et la liste admin resterait moins longtemps « propre ». |
| `Task.Delay` dans une boucle | Fonctionne, mais ne se teste pas avec `FakeTimeProvider` sans astuce ; `PeriodicTimer` + `TimeProvider` est l'outil prévu depuis .NET 8. |
| Hangfire, Quartz.NET, Azure Functions (timer), WebJobs | Écartés par l'ADR 0005 (dépendance, tables, ressource Azure en plus pour une requête). |
| Verrou applicatif SQL (`sp_getapplock`) pour une seule instance active | Inutile : la requête est idempotente. |
| Garder un `DbContext` dans le service | Bug classique : `DbContext` n'est pas thread-safe et grossit indéfiniment ; un scope par passage est la règle. |
| `BackgroundServiceExceptionBehavior.Ignore` global | Masquerait les erreurs de **tous** les services hébergés ; on attrape au plus près, dans la boucle. |

## Conséquences

- Tests (`Aaas.Infrastructure.Tests`, sans base, doublure de `ReservationExpiryService`) : un passage par `Advance(interval)` ; une exception lors d'un passage est journalisée **sans** son message SQL et le passage suivant s'exécute ; aucune activité → aucun appel au service ; activité → appel. Test d'intégration : demande échue écrite « Expirée » en base après `Advance`.
- Un middleware de quelques lignes dans `Aaas.Api` ; un singleton `ApiActivity` dans Infrastructure (`Jobs/`), que l'Api écrit et que le job lit.
- La fiche de révision `docs/revision/dotnet-background-service.md` (nouvelle) explique `IHostedService`, `BackgroundService`, `PeriodicTimer`, scope DI et `StopHost`.
