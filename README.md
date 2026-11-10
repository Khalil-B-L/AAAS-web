# AAAS — Application pour association

Application web pour une association : consultation des **actualités**, création de **compte**, **réservation d'événements** (utilisateurs) et **publication** des actualités et événements (administrateurs), plus un **assistant RAG** qui répond aux questions à partir des documents de l'association.

Projet personnel de révision de bout en bout d'une stack .NET / Angular / Docker / Azure.

## Stack

| Couche | Technos |
|---|---|
| API | ASP.NET Core, EF Core, SQL Server, ASP.NET Core Identity + JWT |
| Tests | xUnit, WebApplicationFactory, Testcontainers |
| Front | Angular (composants standalone, signals) |
| Conteneurs | Docker, docker compose (API + SQL Server + front) |
| CI/CD | GitHub Actions |
| Cloud | Azure App Service, Static Web Apps, Azure SQL, Key Vault |
| IA | RAG avec Azure OpenAI |

## Feuille de route

- [ ] Semaine 1 — API, EF Core, auth, réservations, tests, docker compose (API + SQL)
- [ ] Semaine 2 — Front Angular, espace admin, front dans docker compose
- [ ] Semaine 3 — Pipeline GitHub Actions, déploiement Azure, Key Vault
- [ ] Semaine 4 — Assistant RAG

## Agents Claude Code

Le dossier `.claude/agents/` contient les agents utilisés pour le projet : `architecte`, `expert-fonctionnel`, `expert-technique`, `code-reviewer`, `testeur`.
