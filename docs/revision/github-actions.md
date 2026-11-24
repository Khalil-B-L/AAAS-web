# Fiche de révision — GitHub Actions (CI)

> À relire la veille d'un entretien. Exemples tirés du projet AAAS (`.github/workflows/ci.yml`, `.github/dependabot.yml`). Procédure d'exploitation : `docs/technique/exploitation.md` § 10.

## 1. Les notions en une ligne

| Notion | Définition | Analogie |
|---|---|---|
| **CI** (intégration continue) | À chaque push / PR, une machine neuve compile et teste le code : on sait vite si `main` est cassée. | Le « build du serveur » qui remplace « ça marche sur mon poste » |
| **CD** (déploiement continu) | Après une CI verte, publication automatique (images, Azure). Ici : `cd.yml`, pas encore écrit. | — |
| **Workflow** | Un fichier YAML dans `.github/workflows/`, déclenché par des événements (`on:`). | Un pipeline Azure DevOps |
| **Événement** (`on`) | `push`, `pull_request`, `workflow_dispatch` (bouton manuel), `schedule` (cron)... Filtres `branches`, `paths-ignore`. | Les déclencheurs d'un pipeline |
| **Job** | Ensemble de steps exécutés sur **un même runner**. Les jobs d'un workflow tournent **en parallèle** par défaut ; `needs:` les enchaîne. | Un stage / une machine de build |
| **Step** | Une commande (`run:`) ou une action réutilisable (`uses:`). Les steps d'un job partagent le disque. | Une tâche du pipeline |
| **Runner** | La machine qui exécute un job. `ubuntu-latest` : VM hébergée par GitHub, **neuve à chaque job**, avec Docker, git, Node, .NET préinstallés. | Un agent de build hébergé |
| **Action** | Brique réutilisable publiée sur GitHub (`actions/checkout@v7`). `@v7` = étiquette de version majeure (reçoit les correctifs). | Un paquet NuGet de tâches |
| **Cache** | Sauvegarde d'un dossier (paquets NuGet, cache npm, couches Docker) entre runs, retrouvé par une **clé**. Accélère, ne change pas le résultat. | Le dossier `~/.nuget/packages` partagé |
| **Artefact** | Fichier produit par un run et **téléchargeable** ensuite (résultats de tests, binaires). Durée de conservation limitée. | Les « drops » d'un build |
| **`GITHUB_TOKEN` / `permissions`** | Jeton automatique du run ; `permissions:` restreint ce qu'il peut faire. | Le compte de service du pipeline (moindre privilège) |
| **Secrets** | Valeurs chiffrées (`${{ secrets.X }}`), masquées dans les journaux. Pas utilisées par `ci.yml`. | Key Vault / user-secrets |
| **`concurrency`** | Un seul run actif par « groupe » ; `cancel-in-progress` annule l'ancien. | — |
| **Dependabot** | Robot GitHub qui ouvre des PR de mise à jour de dépendances, vérifiées par la CI. | Renovate ; « Mises à jour » du gestionnaire NuGet, automatisées |

## 2. Anatomie du workflow du projet

```yaml
on:
  push: { branches: [main], paths-ignore: ['docs/**', '**/*.md'] }
  pull_request: { branches: [main], paths-ignore: ['docs/**', '**/*.md'] }
  workflow_dispatch:

permissions:
  contents: read                 # le jeton ne peut que LIRE le dépôt

concurrency:
  group: ci-${{ github.ref }}    # une file par branche / PR
  cancel-in-progress: true       # un nouveau push annule le run obsolète

jobs:
  backend:                       # .NET : restore, build -warnaserror, tests projet par projet, artefact TRX
    runs-on: ubuntu-latest
    timeout-minutes: 30          # défaut : 360 min ! Un test bloqué consommerait 6 h de runner
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with: { global-json-file: global.json }   # même SDK que sur les postes
      - uses: actions/cache@v6
        with:
          path: ~/.nuget/packages
          key: nuget-${{ runner.os }}-${{ hashFiles('Directory.Packages.props', '**/*.csproj') }}
      ...
  front:  { ... }                # Node de .nvmrc, npm ci, lint, test:ci, build
  docker: { ... }                # docker build des deux images, push: false
```

Points à savoir expliquer :
- **Trois jobs parallèles** : durée totale = le plus long des trois, pas la somme. Ils ne partagent rien (machines différentes) : chaque job refait son `checkout`.
- **`${{ ... }}`** : expression évaluée par GitHub **avant** d'envoyer le step au runner (`github.ref`, `runner.os`, `hashFiles(...)`, `steps.build.outcome`).
- **Conditions de step** : par défaut un step ne s'exécute que si tous les précédents ont réussi (`success()`). `always()` : même après un échec ou une annulation (upload des résultats de tests). `!cancelled()` : sauf annulation. Le projet utilise `!cancelled() && steps.build.outcome == 'success'` pour lancer **tous** les projets de tests même si l'un échoue, mais aucun si la compilation a échoué.
- **`defaults.run.working-directory: front`** : tous les `run:` du job front partent de `front/` (les `uses:` ne sont pas concernés, d'où `front/.nvmrc` en chemin complet).

## 3. Cache : clé et restauration

- `key` : si la clé **exacte** existe, le dossier est restauré (*cache hit*) et n'est **pas** réenregistré en fin de job.
- `restore-keys` : préfixes de repli ; on restaure le cache le plus récent qui commence par ce préfixe (*cache partiel*), puis on enregistre un nouveau cache sous la clé exacte.
- `hashFiles('Directory.Packages.props', '**/*.csproj')` : la clé change dès qu'une version de paquet change → nouveau cache. C'est la même logique que l'ordre des `COPY` dans un Dockerfile.
- `setup-dotnet` a un `cache: true` intégré, mais il **exige des `packages.lock.json`** (absents ici) : d'où `actions/cache`. `setup-node` gère `cache: npm` avec `cache-dependency-path: front/package-lock.json`.
- Cache des couches Docker : `cache-from/cache-to: type=gha` (BuildKit via `docker/setup-buildx-action`). `scope` distinct par image, `mode=max` pour garder aussi les couches du stage `build`.
- Le cache est limité (10 Go par dépôt, entrées inutilisées supprimées après 7 jours) : c'est une **accélération**, jamais une dépendance.

## 4. Sécurité

- **Moindre privilège** : `permissions: contents: read` au niveau du workflow. Sans cela, le jeton reçoit les droits par défaut du dépôt (parfois écriture). Un job de déploiement ajoutera seulement ce dont il a besoin (ex. `packages: write` pour GHCR, `id-token: write` pour OIDC vers Azure).
- **PR venant d'un fork** : `pull_request` s'exécute **sans** les secrets et avec un jeton en lecture seule ; c'est voulu. Ne pas utiliser `pull_request_target` pour lancer le code d'une PR (il a les secrets).
- **Épinglage des actions** : `@v7` (étiquette majeure, mise à jour par l'éditeur) est le compromis courant ; `@<sha du commit>` est le plus sûr (immuable) mais demande Dependabot pour suivre. Le projet : versions majeures + Dependabot.
- **OIDC** (pour `cd.yml`) : GitHub prouve son identité à Azure par un jeton signé à courte durée ; aucun mot de passe Azure stocké dans les secrets GitHub.

## 5. Commandes et gestes utiles

| Besoin | Où / commande |
|---|---|
| Voir les runs | onglet **Actions** du dépôt ; encart « Checks » d'une PR |
| Relancer | « Re-run failed jobs » (seulement les jobs rouges) ou « Re-run all jobs » |
| Lancer à la main | « Run workflow » (grâce à `workflow_dispatch`) |
| Résultats de tests | artefact `resultats-tests-dotnet` en bas de la page du run (`.trx`, ouvrable dans Visual Studio) |
| Journal détaillé | relancer avec « Enable debug logging » |
| Vérifier un workflow avant de pousser | `actionlint .github/workflows/ci.yml` (binaire autonome, rhysd/actionlint) |
| Bloquer la fusion d'une PR rouge | Settings → Branches (ou Rulesets) → « Require status checks to pass » sur `main` |

## 6. Pièges

- **Timeout par défaut de 6 h** : toujours fixer `timeout-minutes`.
- **`paths-ignore` + checks obligatoires** : si un check est requis sur `main`, une PR qui ne touche que la doc ne déclenche pas le workflow et reste bloquée « en attente ». Solution : ne pas rendre le check obligatoire, ou retirer `paths-ignore`.
- **`-warnaserror` seulement en CI** : un avertissement ignoré en local casse la CI. Lancer la même commande avant de pousser (`exploitation.md` § 10).
- **Différences Windows / Linux** : casse des noms de fichiers (`Import './Foo'` vs `foo.ts`), fins de ligne, chemins avec `\`.
- **`cancel-in-progress` sur `main`** : deux push rapprochés → seul le dernier commit est vérifié. Acceptable pour une CI ; à éviter pour un déploiement (on mettrait alors `cancel-in-progress: false`).
- **Docker sur le runner** : seulement sur les runners Linux hébergés (`ubuntu-latest`) ; pas sur `windows-latest` pour des conteneurs Linux. Testcontainers en dépend.
- **Artefacts et caches publics** : sur un dépôt public, tout le monde peut télécharger les artefacts. Ne jamais y mettre de secret ni de données personnelles.

## 7. Questions d'entretien

1. **Différence entre job et step ?** — Un job tourne sur son propre runner (machine neuve), en parallèle des autres sauf `needs:` ; ses steps s'exécutent en séquence sur la même machine et partagent le disque.
2. **Différence entre cache et artefact ?** — Le cache accélère les runs suivants (dépendances, retrouvées par clé, pas garanti). L'artefact est un résultat d'un run, à télécharger ou à passer à un autre job (tests, binaires, paquet à déployer).
3. **Comment limiter les droits d'un workflow ?** — `permissions:` au niveau workflow ou job (`contents: read` par défaut), pas de secret dans les PR de forks, OIDC plutôt que des mots de passe de cloud, actions épinglées.
4. **Pourquoi `npm ci` et pas `npm install` en CI ?** — `npm ci` installe exactement le lockfile dans un `node_modules` vide et échoue s'il est incohérent : build reproductible. `npm install` peut modifier le lockfile.
5. **Comment faire tourner des tests d'intégration avec une vraie base en CI ?** — Testcontainers lance le conteneur SQL Server depuis les tests, sur le Docker du runner Ubuntu ; alternative : `services:` au niveau du job (conteneur de service géré par GitHub).
6. **À quoi sert `concurrency` ?** — Ne garder qu'un run par groupe (branche / PR) et annuler les runs devenus obsolètes : économie de minutes, résultat du dernier commit plus vite.

## 8. Exercices

- Ajouter au job `front` l'upload du rapport de couverture (`front/coverage/`) en artefact, seulement si les tests ont tourné.
- Ajouter une étape `docker compose config` dans le job `docker` avec des valeurs **factices** pour les variables obligatoires (`MSSQL_SA_PASSWORD`, `JWT_SIGNING_KEY`, `BOOTSTRAP_ADMIN_EMAIL`, `BOOTSTRAP_ADMIN_PASSWORD`) passées par `env:`.
- Transformer les quatre steps de tests en une **matrice** (`strategy.matrix.project: [...]`) et comparer : avantages (moins de YAML, jobs parallèles) et inconvénients (une compilation par job).
