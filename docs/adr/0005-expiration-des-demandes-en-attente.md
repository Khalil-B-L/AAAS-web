# ADR 0005 — Expiration des demandes En attente

- **Statut** : Accepté (porteur, 24/11/2026)
- **Date** : 10/11/2026 — complété le 13/11/2026 (journalisation des erreurs de la tâche de fond, suite à l'ADR 0002)
- **Références** : cahier RG-04, § 5.1 étape 4 ; mvp R6, US-D5, US-E6

## Contexte

Une demande En attente expire **48 h après la demande, ou au début de l'événement si c'est plus tôt**. Les places affichées et les contrôles de capacité ne doivent **jamais** compter une demande échue, même pendant quelques minutes. Une demande confirmée à 9 h 59 pour une échéance à 10 h n'expire pas ; une demande expirée ne peut plus être confirmée.

Contrainte d'hébergement : l'offre gratuite d'App Service (F1) n'a pas l'option « Always On » ; l'application s'endort après une période d'inactivité. Une tâche planifiée interne ne tourne donc pas en continu.

## Décision

**Échéance calculée et stockée à la création, expiration appliquée à la lecture, matérialisée à l'écriture et par une tâche périodique.**

1. **À la création** : `ExpiresAt = min(RequestedAt + ReservationOptions.PendingLifetime, Event.StartsAt)`, calculé dans le Domain. `PendingLifetime` = 48 h par défaut, dans `appsettings.json` (paramétrable sans modifier le code, comme l'exige le cahier).
2. **Règle unique dans le Domain** : une demande est échue si `Status == Pending && now >= ExpiresAt` (l'échéance elle-même est déjà échue : « quand on arrive au 12 mars à 10 h, la demande passe en Expirée »). `Reservation.Confirm(now)` refuse une demande échue.
3. **À la lecture** : toutes les requêtes de décompte filtrent `Status == Confirmed || (Status == Pending && ExpiresAt > now)`, via une seule expression réutilisable (`ReservationQueries.IsActive(now)`). Une demande échue mais pas encore écrite est **présentée** avec le statut Expirée dans les DTO.
4. **Matérialisation à l'écriture** : sous le verrou de l'événement (ADR 0004), avant le comptage, `ExecuteUpdate` passe en Expirée les demandes échues de cet événement. Cela libère aussi l'index unique filtré (RG-03) pour qu'un membre puisse refaire une demande.
5. **Tâche de fond** : un `BackgroundService` avec `PeriodicTimer` (toutes les 5 minutes, réglable) exécute un `ExecuteUpdate` global et idempotent : `UPDATE Reservations SET Status = Expired WHERE Status = Pending AND ExpiresAt <= @now`. Elle sert à garder la base propre (historique, liste admin), pas à garantir l'exactitude : si l'application dort, le point 3 suffit.
   - **Journalisation de ses erreurs** : la tâche s'exécute hors requête HTTP, donc `ApiExceptionHandler` ne la voit pas, et l'événement EF `CoreEventId.SaveChangesFailed` est ignoré (ADR 0002). Elle doit donc **attraper et journaliser elle-même** ses exceptions à chaque passage (niveau Error, sans message SQL brut d'une violation d'unicité, sans donnée personnelle ; identifiants de réservation ou d'événement seulement), puis **continuer** au passage suivant : une erreur ponctuelle (base Azure SQL en sortie de pause, par exemple) ne doit pas arrêter le `BackgroundService`.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Uniquement une tâche planifiée | Entre deux passages, et quand App Service F1 est endormi, des demandes échues bloqueraient des places : contraire à US-D5 « les places affichées sont toujours justes ». |
| Uniquement le calcul à la lecture | Exact, mais la base garde indéfiniment des `Pending` échus, l'index unique filtré bloque une nouvelle demande, et les requêtes de reporting doivent toutes appliquer le filtre. |
| Hangfire ou Quartz.NET | Dépendance et tables supplémentaires pour une seule requête périodique. |
| Azure Functions (timer) | Ressource Azure de plus à déployer et à sécuriser ; ne résout pas l'exactitude entre deux exécutions. Envisageable plus tard si l'API passe sur un plan sans tâche de fond. |
| SQL Server Agent | Indisponible sur Azure SQL Database. |

## Conséquences

- La règle d'échéance existe en **un seul endroit** du Domain, plus une expression de requête qui la traduit en SQL ; un test vérifie qu'elles concordent.
- Course confirmation contre expiration : la confirmation charge la réservation, vérifie `now < ExpiresAt` dans le Domain et enregistre avec contrôle de `rowversion` (ADR 0006). Si la tâche de fond a écrit Expirée entre-temps, la sauvegarde échoue en conflit (409). Si la confirmation est validée d'abord, la tâche ne la touche pas (`WHERE Status = Pending`).
- La liste admin des demandes à traiter (US-E6) n'affiche que les demandes non échues, triées par `ExpiresAt` croissante (index `IX_Reservations_Status_ExpiresAt`).
- Tests avec `FakeTimeProvider` : échéance à 48 h, échéance ramenée au début de l'événement, confirmation à échéance − 1 minute, refus à l'échéance exacte, places libérées sans passage de la tâche ; pour la tâche de fond, une exception lors d'un passage est journalisée et le passage suivant s'exécute.
