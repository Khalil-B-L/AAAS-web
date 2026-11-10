# AAAS — consignes du projet

Application web pour une **association en Tunisie** : actualités, comptes, demandes de réservation d'événements validées par un administrateur, publication par les administrateurs, puis un assistant RAG. **Le document de référence est le cahier des charges `docs/cahier-des-charges.md` : toute tâche doit le respecter** (périmètre, règles RG-xx, contraintes techniques, définition de « terminé » § 10). Tout ajout ou changement de périmètre se fait d'abord dans ce document (avec une ligne dans son historique), jamais directement dans le code. Le détail des user stories est dans `docs/fonctionnel/mvp.md`.

**Le projet sert d'abord à réviser** ASP.NET Core, EF Core, SQL Server, xUnit, Angular, Docker, GitHub Actions et Azure, en vue d'entretiens. Docker et Angular sont presque oubliés : il faut les expliquer. .NET et SQL Server sont maîtrisés.

## Contexte métier à ne pas oublier
- Fuseau `Africa/Tunis` (UTC+1, sans changement d'heure). Les dates sont stockées en UTC.
- Interface en français uniquement.
- Aucun paiement en ligne : prix optionnel en TND, payé en main propre, marqué « payé » par l'admin.
- Réservation en deux temps : demande « En attente » (bloque une place, expire après un délai), l'utilisateur contacte le numéro de l'admin affiché, l'admin confirme ou refuse.
- Protection des données : loi organique tunisienne n° 2004-63 (INPDP), pas le RGPD.

## Façon de travailler
1. `expert-fonctionnel` : user stories et critères d'acceptation.
2. `architecte` : valide et conçoit le plan, rédige les ADR.
3. `expert-technique` : code en expliquant, tient les fiches de révision.
4. `testeur` : tests unitaires et d'intégration.
5. `code-reviewer` : relecture avant commit.
6. `documentaliste` : guide utilisateur et documentation de la logique.

Ne jamais committer avec l'identité Ouidou : ce dépôt utilise l'identité git locale `Khalil-B-L`. L'utilisateur fait lui-même ses commits et ses push.

## Documentation : à tenir à jour au fil de l'eau
Toute modification qui change un comportement visible ou une règle doit mettre à jour la doc concernée **dans la même tâche**.

| Dossier / fichier | Contenu | Rédigé par |
|---|---|---|
| `docs/fonctionnel/` | Besoin, user stories, critères d'acceptation, questions ouvertes | expert-fonctionnel |
| `docs/adr/` | Décisions d'architecture (une par fichier `NNNN-titre.md`) | architecte |
| `docs/technique/architecture.md` | Vue d'ensemble : couches, schéma de données, flux, déploiement | architecte |
| `docs/technique/regles-metier.md` | Toutes les règles métier et le cycle de vie des statuts, avec l'endroit du code qui les applique | expert-technique |
| `docs/technique/api.md` | Endpoints : rôle requis, entrées, sorties, codes d'erreur | expert-technique |
| `docs/technique/exploitation.md` | Lancer en local (Docker), configuration, secrets, CI/CD, déploiement Azure | expert-technique |
| `docs/revision/` | Fiches de révision par techno | expert-technique |
| `docs/guide-utilisateur/` | Guides pour les visiteurs/membres et pour les administrateurs | documentaliste |

Le documentaliste relit l'ensemble et assemble la documentation finale ; il ne doit pas avoir à deviner la logique à partir du code.
