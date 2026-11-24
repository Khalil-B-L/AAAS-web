# Front AAAS (Angular 22)

Application Angular de l'association : événements, comptes, espace d'administration. Interface en français.

- Lancer, configurer, dépanner : [`docs/technique/exploitation.md`](../docs/technique/exploitation.md) § 2 et § 2 bis.
- Architecture et décisions : [`docs/technique/architecture.md`](../docs/technique/architecture.md) § 6, ADR [0014](../docs/adr/0014-front-angular-version-structure-et-conventions.md) à [0017](../docs/adr/0017-conteneur-front-nginx-proxy-et-configuration-de-l-api.md).
- Notions à réviser : [`docs/revision/angular.md`](../docs/revision/angular.md), [`docs/revision/docker.md`](../docs/revision/docker.md) § 6 ter, [`docs/revision/flux-ecran-api-base.md`](../docs/revision/flux-ecran-api-base.md).
- État et reprise : [`docs/technique/etat-avancement.md`](../docs/technique/etat-avancement.md).

```bash
npm ci              # dépendances exactes (Node 24, voir .nvmrc)
npm start           # http://localhost:4200, /api relayé vers http://localhost:5080 (API_URL pour changer)
npm test            # Vitest en surveillance ; npm run test:ci pour une passe avec couverture
npm run lint        # ESLint
npm run build       # dist/front/browser
```

Nom, logo et coordonnées de l'association : `src/app/core/config/app-info.ts` (et `<title>` de `src/index.html`).
