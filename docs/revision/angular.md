# Fiche de révision — Angular 22 (standalone, signals, zoneless)

> À relire la veille d'un entretien. Exemples tirés du front AAAS (`front/src/app/`). Décisions : ADR 0014 à 0017, plan `docs/technique/plan-front-angular.md`.
> Version du projet : **Angular 22.3**, TypeScript 6, Node 24, Material 3, Vitest 5.

## 0. Carte mentale : Angular vu depuis .NET

| Angular | Rôle | Analogie .NET | Dans AAAS |
|---|---|---|---|
| `main.ts` + `app.config.ts` | Démarrage, liste des services | `Program.cs` (`builder.Services.Add…`) | `app.config.ts` |
| Composant standalone | Classe + template + styles, déclare ses dépendances (`imports`) | Page Razor / composant Blazor | chaque écran, `<app-pager>` |
| Service `@Injectable({ providedIn: 'root' })` | Singleton injectable | `AddSingleton` | `AuthService`, `EventsApi` |
| `inject(X)` | Récupère une dépendance | injection par constructeur | partout |
| Signal (`signal`, `computed`) | Valeur réactive lue par le template | propriété + `INotifyPropertyChanged` | état local, session |
| `HttpClient` → `Observable` | Appel HTTP paresseux | `HttpClient` + `Task<T>` (mais froid) | `core/api/*.api.ts` |
| Intercepteur fonctionnel | Code autour de chaque requête | `DelegatingHandler` / middleware | `auth.interceptor.ts` |
| Routeur + guards | URL → composant ; autorisation de navigation | routing + `[Authorize]` (côté client = confort) | `app.routes.ts`, `auth.guards.ts` |
| Reactive forms typés | Formulaire = objet TS + validateurs | modèle + DataAnnotations | inscription, événement |
| Pipe | Mise en forme dans le template | `ToString("format")`, convertisseur | `date`, `tndPrice` |
| `environment.ts` | Configuration **figée à la compilation** | `appsettings.json`, mais compilé | `apiBaseUrl: ''` |
| `ng build` | Compile en fichiers statiques (`dist/front/browser`) | `dotnet publish` | Dockerfile du front |

## 1. Composant standalone

**C'est quoi** : une classe décorée par `@Component`, avec un template HTML et des styles, qui liste elle-même dans `imports` les composants, directives et pipes qu'elle utilise. Plus de `NgModule`.

```ts
// front/src/app/shared/ui/pager.ts (extrait)
@Component({
  selector: 'app-pager',                       // balise <app-pager> dans les templates
  imports: [MatButtonModule],                   // ce que le template utilise
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (totalPages() > 1) { … Page {{ page() }} sur {{ totalPages() }} … }`,
})
export class Pager {
  readonly page = input.required<number>();     // entrée (≈ [Parameter] Blazor), sous forme de signal
  readonly pageChange = output<number>();       // sortie : (pageChange)="goToPage($event)" chez le parent
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
}
```

Conventions v20+ suivies : fichiers sans suffixe (`event-list.ts`, classe `EventList`), `.ts` + `.html` + `.scss` pour une page, gabarit en ligne pour les petits composants.

## 2. Templates : liaisons et contrôle de flux

| Syntaxe | Sens | Exemple AAAS |
|---|---|---|
| `{{ expr }}` | texte (échappé : pas d'injection HTML) | `{{ e.title }}` |
| `[prop]="expr"` | liaison de propriété | `[disabled]="busy()"` |
| `(event)="méthode()"` | liaison d'événement | `(click)="logout()"` |
| `[attr.aria-expanded]` | attribut HTML (pas une propriété DOM) | menu mobile |
| `@if (x(); as v) { } @else { }` | condition, avec alias | `@if (errorMessage(); as message)` |
| `@for (e of items; track e.id) { } @empty { }` | boucle ; `track` **obligatoire** (identité stable, ≈ clé React) | cartes d'événements |
| `@switch / @case` | aiguillage | états du formulaire admin |
| `@let m = mention(event);` | variable locale de template | `event-list.html` |

`track e.id` remplace l'ancien `trackBy` : Angular ne recrée que les éléments dont l'identité change.

## 3. Signals

**C'est quoi** : une valeur réactive. On la **lit en l'appelant** (`count()`), on l'écrit avec `set` / `update`. Un template, un `computed` ou un `effect` qui lit un signal est recalculé quand il change.

```ts
// front/src/app/core/auth/auth.service.ts
private readonly state = signal<Session | null>(null);   // modifiable, privé
readonly session = this.state.asReadonly();               // exposé en lecture seule
readonly isAdmin = computed(() => this.state()?.roles.includes('Admin') ?? false);  // dérivé, mis en cache
```

- `computed` : dérivé **paresseux et mémorisé** (recalculé seulement si une source change).
- `effect` : effet de bord quand des signals changent (titre de l'onglet dans `event-detail.ts`, rechargement quand `id()` change dans `admin-event-form.ts`). À utiliser peu ; `untracked()` empêche un signal lu dans l'effet de devenir une dépendance.
- `input()` / `output()` / `model()` : entrées et sorties de composant en signals.
- `toSignal(obs$)` / `toObservable(sig)` : ponts avec RxJS (`field-errors.ts` transforme `control.events` en signal).

## 4. Zoneless et `OnPush` (défauts de la v22)

**Avant** : `zone.js` interceptait tous les événements asynchrones (clic, `setTimeout`, XHR) et relançait la détection de changements sur **toute** l'application. **Maintenant** (zoneless) : un composant est réaffiché seulement si
1. un **signal** lu par son template change ;
2. un événement de **son** template survient (`(click)`, `(input)`…) ;
3. une de ses entrées change ;
4. on le demande explicitement (`markForCheck`, `async` pipe).

**Piège n° 1 (vu dans le projet)** : modifier un simple champ de classe dans un `subscribe` → l'écran ne bouge pas. Règle AAAS : **tout état affiché est un signal** (`submitting`, `errorMessage`, `busy`…).

```ts
// login.ts : le bouton se réactive après l'erreur parce que submitting est un signal
error: (error) => { this.errorMessage.set(toApiError(error).message); this.submitting.set(false); }
```

**Piège n° 2** : `form.dirty` ou `control.errors` ne sont **pas** des signals. Pour désactiver « Publier » quand le formulaire est modifié (`admin-event-form.ts`) :

```ts
private readonly formEvents = toSignal(this.form.events);       // flux unifié : valeur, statut, touché, pristine
protected readonly dirty = computed(() => { this.formEvents(); return this.form.dirty; });
```

## 5. Injection de dépendances

```ts
@Injectable({ providedIn: 'root' })        // singleton de l'application, créé à la première injection
export class EventsApi {
  private readonly http = inject(HttpClient);   // ≈ paramètre de constructeur
}
```

- `providers: [...]` d'un **composant** = portée du composant et de ses enfants (≈ scope). Exemple : `FORM_FIELD_DEFAULTS` est fourni par chaque écran à formulaire.
- `inject()` ne marche que dans un **contexte d'injection** (initialiseur de champ, constructeur, fonction de guard/intercepteur). Dans un test : `TestBed.runInInjectionContext(() => authGuard(...))`.

**Piège rencontré (taille du bundle)** : importer le jeton `MAT_FORM_FIELD_DEFAULT_OPTIONS` dans `app.config.ts` a fait entrer le module form-field de Material **et** `@angular/forms` dans le bundle initial (+115 Ko). Un simple `import` d'un symbole suffit à embarquer son module dans le chunk qui l'importe. Solution : le fournir au niveau des composants chargés à la demande. Outil de diagnostic : `ng build --stats-json`, puis lecture de `dist/front/browser-stats.json` (fichier « metafile » d'esbuild).

## 6. `HttpClient`, `Observable`, `rxResource`

- `http.get<T>(url)` renvoie un `Observable` **froid** : rien ne part tant qu'on n'y souscrit pas ; il émet **une** valeur puis se termine. (Différence avec `Task` : une `Task` est déjà lancée.)
- Les services de `core/api/` renvoient l'`Observable` brut : pas de gestion d'erreur, pas de message (c'est l'écran qui sait où afficher).
- Paramètres toujours par `HttpParams` (encodage), jamais par concaténation.
- `DELETE` avec corps : `http.delete(url, { body: { version } })` (sinon 415 de l'API).

**`rxResource`** (stable en v22) : transforme un appel dépendant de signals en « ressource » réactive.

```ts
// event-list.ts
readonly page = input(1, { transform: toPageNumber });   // ?page= de l'URL
protected readonly events = rxResource({
  params: () => ({ page: this.page() }),                 // lu comme un computed : relance si page change
  stream: ({ params }) => this.api.list(params.page),    // l'Observable à exécuter (ancien annulé, comme switchMap)
});
// Template : events.isLoading(), events.error(), events.value(), events.reload()
```

**Pièges** :
- `value()` **lève une exception** quand la ressource est en erreur : tester `error()` d'abord (ou `hasValue()`).
- L'erreur d'une ressource est une `Error` qui **enveloppe** la `HttpErrorResponse` dans `cause` : `toApiError` la déballe.
- En test, `fixture.whenStable()` attend la fin des ressources en cours : pour voir l'état « Chargement », utiliser `fixture.detectChanges()`.

Écriture (POST/PUT/DELETE) : appel explicite dans le gestionnaire (`save()`), avec `subscribe({ next, error })`. Pas besoin de se désabonner d'un appel HTTP (il se termine) ; en revanche un flux infini (`valueChanges`) se coupe avec `takeUntilDestroyed()` (`contact-settings.ts`).

### Chargement qui suit un paramètre de route **et** remplit un formulaire : `toObservable` + `switchMap`

Un `subscribe` lancé depuis un `effect` à chaque changement d'`id` n'est **jamais annulé** : si l'admin passe de la fiche A à la fiche B et que A répond en dernier, **A écrase B** dans le formulaire (course) ; si le composant est détruit, la réponse s'applique quand même. Correction (`admin-event-form.ts`) :

```ts
toObservable(this.id).pipe(                                         // signal → flux
  switchMap((id) => this.reloadRequests.pipe(startWith(undefined), map(() => id))),   // + « Recharger »
  switchMap((id) => this.fetch(id)),     // désabonne la requête précédente : HttpClient l'annule
  takeUntilDestroyed(),                  // composant détruit : plus rien ne s'applique
).subscribe((result) => this.applyLoadResult(result));
```

- Analogie .NET : annuler le `CancellationTokenSource` de la requête précédente avant d'en lancer une nouvelle.
- `catchError` **dans** le `switchMap` (sur la requête) : une erreur dans le flux principal le **terminerait** (plus de rechargement possible).
- Pourquoi pas `rxResource` ici ? Chaque réponse doit **réinitialiser le formulaire** (`form.reset`) : avec une ressource, il faudrait un `effect` pour recopier la valeur, ce qu'on voulait justement éviter. `rxResource` reste le bon choix pour une page en lecture (`event-detail.ts`).
- Test de la course : `api.get` renvoie un `Subject` jamais terminé pour A, `of(B)` pour B ; après le passage à B, `subjectA.observed === false`, et `subjectA.next(A)` ne change pas le formulaire.

### Écriture : ne PAS annuler la requête, mais ignorer sa réponse si l'écran a changé

Lecture et écriture ne se traitent pas pareil. Une lecture obsolète s'annule (`switchMap`). Une **écriture** (POST/PUT/DELETE) partie vers le serveur a peut-être **déjà été appliquée** : la couper côté client (`takeUntilDestroyed`, `unsubscribe`) ne la défait pas, et on perdrait seulement la réponse. Mais si l'admin a quitté la page ou est passé à une autre fiche pendant la requête, la réponse ne doit plus toucher l'écran (`admin-event-form.ts`, `isStale`) :

```ts
private isStale(id: string | undefined): boolean {
  return this.destroyRef.destroyed || this.id() !== id;   // DestroyRef.destroyed : composant déjà détruit ?
}
// dans le next d'une écriture :
this.busy.set(false);                         // sinon la fiche B resterait bloquée par la requête de A
this.notifications.show('Enregistré.');       // message global : vrai quel que soit l'écran
if (!this.isStale(id)) this.applyEvent(updated);   // effets locaux (formulaire, navigation) seulement sur la bonne fiche
```

- Analogie .NET : ne pas passer le `CancellationToken` de la requête HTTP entrante à une écriture qui doit aboutir, mais vérifier avant de mettre à jour un état lié à l'appelant.
- Test : l'API renvoie un `Subject` ; `fixture.destroy()` (ou `setInput('id', B)`), puis `subject.observed === true` (requête maintenue) et `subject.next(...)` : pas de `navigate`, formulaire de B intact.

## 7. Intercepteur fonctionnel

```ts
// front/src/app/core/auth/auth.interceptor.ts
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(API_PREFIX)) return next(req);            // jamais de jeton vers un autre site
  const auth = inject(AuthService);
  const token = auth.accessToken();
  const request = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;  // requêtes immuables
  return next(request).pipe(catchError((error) => {
    if (token && error instanceof HttpErrorResponse && error.status === 401) auth.expire(true);    // session révoquée
    return throwError(() => error);                                   // on relaie toujours l'erreur
  }));
};
// app.config.ts : provideHttpClient(withInterceptors([authInterceptor]))
```

Analogie : un `DelegatingHandler` qui ajoute l'en-tête puis inspecte la réponse. Le 401 **sans** jeton (mauvais mot de passe) n'est pas traité : sinon la page de connexion se redirigerait vers elle-même.

## 8. Routeur, lazy loading, guards

```ts
// app.routes.ts (extraits)
{ path: 'evenements/:id', title: 'Événement',
  loadComponent: () => import('./features/public/event-detail/event-detail').then((m) => m.EventDetail) },
{ path: 'admin', canMatch: [adminGuard],
  loadChildren: () => import('./features/admin/admin.routes').then((m) => m.ADMIN_ROUTES) },
```

- `loadComponent` / `loadChildren` : le code de la page est un fichier JS séparé (*chunk*), téléchargé à la première visite.
- `withComponentInputBinding()` : `:id` et `?page=` arrivent dans des `input()` du composant (plus besoin d'`ActivatedRoute`).
- Guard = fonction qui renvoie `true` ou un `UrlTree` (redirection). **Confort uniquement** : la sécurité est vérifiée par l'API (401/403).
- **`canActivate` vs `canMatch`** : `canActivate` refuse l'activation d'une route **déjà chargée** ; `canMatch` fait comme si la route n'existait pas, donc le `loadChildren` n'est **pas téléchargé** (le code admin n'arrive jamais chez un membre).
- `canDeactivate` : confirmation avant de quitter un formulaire modifié (`unsaved-changes.guard.ts`).
- Ordre des routes : `evenements/nouveau` **avant** `evenements/:id` (premier qui correspond).
- `TitleStrategy` personnalisée (`app-title.strategy.ts`) : « Événements — Association ».
- **Focus après navigation** (accessibilité, `app.ts`) : une SPA ne recharge rien, le focus resterait sur le lien cliqué. On écoute `router.events` filtré sur `NavigationEnd`, puis `afterNextRender(...)` (le `<h1>` de la nouvelle page n'existe qu'après le prochain rendu) donne le focus au `<h1>` (`tabindex="-1"` : focusable par programme, hors de l'ordre de tabulation), à défaut à `<main>`.
- **Piège `<base href="/">`** : un lien `href="#contenu"` se résout en `/#contenu` → navigation vers l'accueil. Le lien d'évitement garde son `href` mais son `(click)` fait `preventDefault()` puis `main.focus()`.
- `navigateByUrl` vers **l'URL courante** est ignoré et renvoie `false` (`onSameUrlNavigation: 'ignore'` par défaut) : passer `{ onSameUrlNavigation: 'reload' }` si l'on dépend du résultat (`AuthService.logout()`).

## 9. Reactive forms typés

```ts
// register.ts
protected readonly form = inject(NonNullableFormBuilder).group({
  email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
  phoneNumber: ['+216 ', [Validators.required, tunisianPhone]],          // validateur maison
  acceptPrivacyPolicy: [false, [Validators.requiredTrue]],
});
// form.controls.email : FormControl<string> ; form.getRawValue() : { email: string; … } typé
```

```html
<form [formGroup]="form" (ngSubmit)="submit()" novalidate>
  <input matInput formControlName="email" />
  <mat-error><app-field-errors [control]="form.controls.email" field="email" /></mat-error>
```

- **NonNullable** : `reset()` remet la valeur initiale et non `null`.
- **Validateur** = `(control) => null | { clé: détail }` (≈ `ValidationAttribute.IsValid`). Validateur de **groupe** pour comparer deux champs (`endAfterStart`), affiché sur le champ « Fin » par un `ErrorStateMatcher` Material.
- **Mêmes noms que les clés JSON** de l'API : une erreur 400 se recopie avec `applyServerErrors(form, apiError)` → `control.setErrors({ server: [...] })`. Cette erreur disparaît dès la saisie suivante (les erreurs sont recalculées à partir des validateurs).
- **Mêmes messages** que l'API (`validation-messages.ts`), vérifiés par des tests.
- Patron d'envoi : `markAllAsTouched()` → si invalide, stop → `submitting.set(true)` → appel → succès / `toApiError` → `submitting.set(false)`.
- Étape suivante à connaître : **Signal Forms** (stables en v22), écartés ici car les reactive forms dominent les projets existants (ADR 0014).

## 10. Pipes

```html
{{ event.startsAt | date: 'EEE d MMM y, HH:mm' }}   <!-- « sam. 5 déc. 2026, 10:00 » -->
{{ event.price | tndPrice }}                         <!-- « 12,500 TND, à régler sur place » -->
```

- Pipe **pur** (défaut) : recalculé seulement si l'entrée change.
- Le `DatePipe` reçoit la locale `fr` et le fuseau `+0100` une fois pour toutes (`FORMATTING_PROVIDERS`) : l'heure de Tunis s'affiche quel que soit le fuseau du poste. La locale doit être **enregistrée** (`registerLocaleData(localeFr)`, `core/config/locale.ts`).

## 11. Dates : le piège du fuseau

- `<input type="datetime-local">` renvoie `"2026-12-05T10:00"` **sans fuseau**. `new Date("2026-12-05T10:00")` l'interpréterait dans le fuseau du navigateur : faux d'une heure pour un admin en France l'été.
- `toTunisIso()` ajoute `+01:00` à l'envoi ; `toTunisLocalInput()` part de l'instant UTC, ajoute 1 h et lit les **getters UTC** (`getUTCHours`). Testé avec `TZ=America/New_York` et `TZ=Pacific/Kiritimati`.

## 12. Session JWT côté front (ADR 0015)

- `localStorage` `aaas.session = { accessToken, localDeadline }` ; **pas** d'appel `/me` au démarrage (restauration synchrone, pages publiques indépendantes de l'API).
- Décodage Base64url de la charge utile, **sans vérifier la signature** (le front n'a pas la clé ; l'API vérifie tout).
- Claim `role` : chaîne pour un rôle, tableau pour plusieurs → normalisé.
- Échéance **relative** : `Date.now() + (exp − iat) × 1000 − 60 s`, jamais `exp` comparé à l'horloge du poste (téléphone mal réglé).
- Onglets synchronisés par l'événement `storage`.
- `returnUrl` validé (`/…` mais pas `//…`) : pas de redirection ouverte.

## 13. Tests (Vitest + TestBed)

| Cible | Outil | Exemple |
|---|---|---|
| Fonction pure | `expect` | `toTunisIso('2026-12-05T10:00')` |
| Service HTTP | `provideHttpClientTesting()` + `HttpTestingController` | `removeContact` envoie un DELETE avec corps |
| Intercepteur | idem + `provideHttpClient(withInterceptors([...]))` | 401 avec jeton → `expire(true)` |
| Guard | `TestBed.runInInjectionContext` | membre sur `admin` → `/acces-refuse` |
| Composant | `TestBed.createComponent`, `setInput`, `await fixture.whenStable()` (zoneless) | flux `registrants_warning` |
| Minuterie | `vi.useFakeTimers()`, `vi.advanceTimersByTime()` | expiration de session |
| Environnement | `vi.stubEnv('TZ', …)` | indépendance du fuseau |

Commandes : `npm test` (surveillance), `npm run test:ci` (une passe + couverture), `npm run lint`.

**Tester un composant Material à overlay (dialogue, `mat-select`) sans test instable** :
- Le dialogue et le panneau d'options s'affichent dans `.cdk-overlay-container`, **hors** du composant testé : on les cherche dans `document`.
- Leurs animations prennent un temps réel : un `await wait(400)` passe sur un poste rapide et échoue sur une CI chargée. Désactiver les animations dans la spec : `{ provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } }` (jeton exporté par `@angular/material/core`, vérifié en v22.3 ; les composants Material n'utilisent plus `@angular/animations`, donc plus `provideNoopAnimations`).
- Puis attendre une **condition**, pas une durée : `await vi.waitFor(() => expect(results).toEqual([true]))` (relance la fonction jusqu'à ce qu'elle ne lève plus, 1 s max par défaut). Analogie xUnit : un « poll until » plutôt qu'un `Task.Delay`.
- Piège zoneless : le `mat-dialog-container` apparaît **avant** son contenu (rendu au cycle suivant) ; attendre le titre ou un bouton, pas le conteneur seul.
- Pour vérifier une **absence** (« Annuler n'envoie rien »), attendre d'abord un fait positif (dialogue fermé), puis vérifier `toHaveBeenCalledTimes(1)`.
- Alternative plus robuste encore : les **harnesses** (`TestbedHarnessEnvironment.documentRootLoader(fixture)`, `MatDialogHarness`, `MatSelectHarness`), qui attendent la stabilité d'eux-mêmes.

## 14. Questions d'entretien

- **Différence entre signal et Observable ?** Un signal a **toujours une valeur courante**, lue de façon synchrone, et sert l'état affiché. Un Observable est un **flux** d'événements dans le temps (paresseux, annulable, opérateurs). On fait le pont avec `toSignal` / `rxResource`.
- **Que change le mode zoneless ?** Plus de `zone.js` : Angular ne réaffiche que sur signal, événement du template ou entrée. Avantages : moins de travail, piles d'appels lisibles. Contrainte : l'état affiché doit être un signal.
- **`OnPush` ?** Le composant n'est vérifié que si ses entrées changent (par référence), qu'un événement de son template survient ou qu'un signal lu change. Défaut en v22.
- **`canActivate` ou `canMatch` pour un espace admin chargé à la demande ?** `canMatch` : le code n'est même pas téléchargé si l'accès est refusé.
- **`switchMap`, `mergeMap`, `concatMap`, `exhaustMap` ?** Respectivement : annule le précédent (recherche, `rxResource`), parallèle, à la file, ignore tant qu'en cours (anti double clic). Ici le double envoi est évité par le signal `submitting`.
- **Faut-il se désabonner d'un `http.get` ?** Non, il se termine. Oui pour un flux infini (`valueChanges`, `interval`) : `takeUntilDestroyed()`. **Mais** une requête lancée à chaque changement de paramètre doit être annulée quand le paramètre change (sinon réponse dans le désordre) : `switchMap` ou `rxResource`.
- **Où mettre le jeton JWT ?** `localStorage` (choix AAAS, ADR 0002/0015) : simple, partagé entre onglets, mais lisible par un script injecté (XSS) → CSP stricte, aucun `bypassSecurityTrust*`. Alternative : cookie `HttpOnly` + protection CSRF.
- **Pourquoi `track` dans `@for` ?** Pour qu'Angular réutilise les éléments DOM existants au lieu de tout recréer quand la liste change.
- **Comment Angular protège-t-il de l'injection HTML ?** L'interpolation `{{ }}` échappe le texte ; `[innerHTML]` est assaini ; seul `bypassSecurityTrust*` contourne (interdit dans AAAS).
- **Que contient `environment.ts` et quand est-il lu ?** Des constantes compilées dans le JS : changer l'URL de l'API exige une recompilation (contrairement à `appsettings.json`).

## 15. Exercices (facultatifs)

1. Ajouter un compteur « N caractères restants » qui passe en rouge au-delà de 190 sur le titre d'un événement (signal + `computed` sur `toSignal(form.controls.title.valueChanges)`).
2. Transformer `NotificationService` pour garder une **file** de messages (signal de tableau, `update`).
3. Remplacer `rxResource` par `httpResource` dans `MyAccount` et comparer (moins de code, mais plus de service `Observable`).
4. Écrire le test « double clic sur Enregistrer : un seul PUT ».
5. Ajouter `--aaas-green-light` dans `_brand.scss` et `styles.scss`, puis l'utiliser comme fond des cartes d'événements ; vérifier le contraste du texte avec un outil (ratio ≥ 4,5:1).

## 16. Thème Material 3 et variables CSS (couleurs de l'association)

**C'est quoi** : Material 3 ne lit pas des classes de couleur mais des **variables CSS** (`--mat-sys-primary`, `--mat-sys-surface`, `--mat-sys-on-primary`…). `mat.theme()` (Sass) les génère à partir de **palettes tonales** : 13 nuances par couleur (tons 0 à 100) ; `primary` = ton 40, `primary-container` = ton 90, etc. Analogie .NET : un `appsettings.json` dont chaque composant lit ses clés ; changer une clé change tous les composants qui la lisent.

Dans AAAS, trois niveaux (`front/src/styles.scss`) :

```scss
@use './theme/brand';          // _brand.scss : les 5 couleurs en dur, seule source
@use './theme/theme-colors';   // palettes générées par ng generate @angular/material:theme-color

html {
  @include mat.theme((color: (theme-type: light, primary: theme-colors.$primary-palette,
                              tertiary: theme-colors.$tertiary-palette), …));
  @include mat.theme-overrides((primary: brand.$green, surface: brand.$white));   // valeurs exactes
  @include mat.button-overrides((filled-container-color: brand.$orange,           // un seul composant
                                 filled-label-text-color: brand.$on-orange));
  --aaas-green: #{brand.$green};   // pour NOS styles : color: var(--aaas-green)
}
```

- **Palette générée ≠ couleur de marque** : à partir de `#0F5C2E`, le générateur donne un `primary` (ton 40) `#236b3b`. `theme-overrides` remet la valeur exacte.
- **`*-overrides`** : jetons d'un composant (`mat.button-overrides`, `mat.form-field-overrides`…) ; portée = le sélecteur où on l'inclut. Pour un seul bouton : redéfinir la variable sur une classe (`.danger { --mat-button-filled-container-color: var(--mat-sys-error); }` dans `confirm-dialog.ts`).
- **Variables CSS en cascade** : une variable redéfinie sur un élément vaut pour tous ses descendants. Le pied de page fait `--aaas-focus: white` : le cadre de focus global (`outline: 2px solid var(--aaas-focus)`) devient blanc sur fond vert, sans règle spéciale.
- Sass (`$green`) est résolu **à la compilation** ; une variable CSS (`--aaas-green`) **dans le navigateur** (modifiable par zone, visible dans les outils de développement, onglet « Calculé »).
- **Boutons M3** : forme « pilule » par défaut (`corner-full`) ; rien à faire pour l'arrondi.

**Contraste (WCAG 2.x, niveau AA)** : 4,5:1 pour le texte normal, 3:1 pour le gros texte (≥ 24 px, ou ≥ 18,66 px en gras) et les éléments d'interface. Mesuré dans AAAS : blanc sur orange `#FF7A00` = **2,61:1**, insuffisant **même** en gros texte gras → texte brun très foncé `#2B1600` (6,60:1). Vert `#0F5C2E` / blanc = 8,11:1 ; brun-orange `#B45309` / blanc = 5,02:1. Tableau complet : `docs/technique/etat-avancement.md` § 1.4.

**Logo sans décalage de mise en page** : `<img width="52" height="48">` (composant `shared/ui/brand-logo.ts`) : le navigateur réserve la place avant le téléchargement (indicateur CLS de Core Web Vitals). Fichier dans `front/public/` (copié tel quel à la racine du site) : servi par le même site, la CSP `img-src 'self'` l'autorise.

**Pièges** :
- `mat.theme-overrides` **ignore sans erreur** une clé inconnue (faute de frappe) : vérifier le CSS compilé (`grep -- --mat-sys-primary dist/front/browser/styles-*.css`) ou l'onglet « Calculé » du navigateur.
- Changer la couleur d'un bouton plein change aussi son **voile de survol** (`filled-state-layer-color`) : le régler avec le texte.
- Ne jamais lire une couleur de marque en dur dans un composant (`#0f5c2e`) : passer par `var(--aaas-…)` ou `var(--mat-sys-…)`.

**Commandes** : `npx ng generate @angular/material:theme-color --primary-color="#0F5C2E" --tertiary-color="#FF7A00" --directory="src/theme/"` (génère `_theme-colors.scss`) ; `npm run build` puis lecture de `styles-*.css`.

**Questions d'entretien** :
- *Comment personnaliser les couleurs d'Angular Material 3 ?* Palettes passées à `mat.theme()` (générées depuis un hex par le schematic `theme-color`), puis `mat.theme-overrides()` pour des valeurs exactes et `mat.<composant>-overrides()` pour un composant.
- *Variable Sass ou variable CSS ?* Sass : constante de compilation, disparaît du CSS. CSS : résolue par le navigateur, héritée, redéfinissable par zone et à l'exécution (thème sombre, zone colorée).
- *Comment éviter le décalage de mise en page dû à une image ?* Donner `width` et `height` (ou `aspect-ratio`) pour réserver la place.

## 17. Réservations (tâche A) : composant enfant, ressource conditionnelle, store partagé

Code : `front/src/app/features/reservations/`, `core/contact/contact.store.ts`, `shared/ui/contact-card.ts`, `core/api/reservations.api.ts`.

### Composant enfant : `input()` / `output()`

- **C'est quoi** : le panneau « Ma réservation » (`<app-reservation-panel>`) est un composant **enfant** du détail. Il **reçoit** l'événement par `readonly event = input.required<EventDetailDto>()` et **prévient** son parent par `readonly changed = output<void>()`.
- **Pourquoi** : après une demande ou une annulation, le nombre de places restantes change ; seul le parent possède la ressource du détail. Le parent écoute : `<app-reservation-panel [event]="e" (changed)="event.reload()" />`.
- **Analogie .NET** : un composant Blazor avec `[Parameter]` (entrée) et `EventCallback` (sortie). `[event]="e"` = liaison de propriété (parent → enfant) ; `(changed)="…"` = liaison d'événement (enfant → parent).
- **Piège rencontré** : le détail affichait le spinner pendant `event.reload()`, ce qui **détruisait** le panneau (et son message d'erreur). Correctif : `@else if (event.isLoading() && !event.hasValue())` — une ressource garde sa valeur pendant un rechargement.

### `rxResource` conditionnelle

```ts
protected readonly mine = rxResource({
  params: () => (this.auth.isLoggedIn() ? { eventId: this.event().id, userId: this.auth.session()?.userId } : undefined),
  stream: ({ params }) => this.api.forEvent(params.eventId),
});
```

- `params` renvoie `undefined` → ressource **au repos** : un visiteur n'appelle jamais `/api/me` (il recevrait 401).
- `params` lit des signals (`isLoggedIn`, `session`, `event`) : quand l'un change (connexion, autre compte, autre événement), la ressource **se relance seule**. Mettre `userId` dans les params garantit qu'on ne montre jamais la demande d'un autre compte.
- `this.mine.set(created)` : on remplace la valeur par la réponse du `POST` sans relire l'API ; `this.mine.reload()` relit (après un 409).

### Piège : `params` qui recrée un objet → `computed` avec `equal`

Après une demande, le parent relit le détail : `event()` devient un **nouvel objet** (même `id`). Le code ci-dessus recrée alors `{ eventId, userId }`, la ressource voit des params « différents » (comparaison par **référence**, `Object.is`), repasse en chargement et relit l'API : le `set()` est perdu. Correctif (revue du 24/11) :

```ts
private readonly mineKey = computed(
  () => (this.auth.isLoggedIn() ? { eventId: this.event().id, userId: this.auth.session()?.userId } : undefined),
  { equal: (a, b) => a?.eventId === b?.eventId && a?.userId === b?.userId },
);
protected readonly mine = rxResource({ params: () => this.mineKey(), stream: ... });
```

- **`equal`** : fonction d'égalité d'un `computed` (ou `signal`). Si elle répond « égal », le signal garde l'ancienne valeur et **ne prévient personne**.
- **Analogie .NET** : passer d'une `class` (égalité de référence) à un `record` (égalité de valeur) ; ou un `IEqualityComparer<T>`.
- Test : `public-pages.spec.ts`, « après une demande : … n'est PAS relue » (un seul appel à `forEvent`).

### `effect()` qui écrit dans un signal : revenir à la dernière page

« Mes réservations » : annuler la seule ligne de la page 2 laisse une page vide. `effect(() => this.backToLastPage(this.current, this.currentPage))` relit la ressource à chaque valeur et, si `items` est vide avec `page > 1`, fait `currentPage.set(dernière page)`. Écrire dans un signal depuis un `effect` est permis (Angular 19+) ; pas de boucle, car `set()` d'une valeur identique ne notifie rien.

### `id` unique par instance (`aria-labelledby`)

Plusieurs `<app-contact-card>` dans la même page : un `id="contact-card-title"` fixe serait dupliqué. Un compteur au niveau du module (`let nextId = 0;`, ≈ un champ `static`) donne `contact-card-title-1`, `-2`… liés par `[attr.aria-labelledby]="titleId"` et `[id]="titleId"`.

### `HttpClient` et la réponse 204

`GET /api/me/events/{id}/reservation` répond 204 (sans corps) s'il n'y a pas de demande. `HttpClient` émet alors **`null`** comme valeur, **sans erreur** : on type `Observable<MyReservationDto | null>` et le template teste `@if (reservation(); as r)`.

### État « occupé » et double clic

Signal `submitting` : le bouton est `[disabled]="submitting()"` et affiche « Envoi en cours… » ; la méthode vérifie aussi `if (this.submitting()) return;` (deux clics plus rapides que le rendu). Si un second `POST` passe quand même (deuxième onglet), l'API répond 409 `already_reserved` avec `reservationId` : le front **relit** la demande et n'affiche **aucune** erreur (ADR 0019 § 4). Le confort est côté front, la garantie côté API.

### Store partagé : `ContactStore`

- Un service `providedIn: 'root'` (singleton, ≈ `AddSingleton`) qui contient une `rxResource` **paresseuse** : `params` vaut `undefined` tant qu'aucun écran n'a appelé `ensureLoaded()`. Plusieurs cartes de contact sur une page = **un seul** appel HTTP.
- Analogie .NET : un cache mémoire (`IMemoryCache`) rempli au premier accès, vidé à la déconnexion (les `params` dépendent de l'utilisateur).

### Enchaîner un dialogue puis un appel : `switchMap`

```ts
return this.dialog.confirm({ ... }).pipe(
  switchMap((confirmed) => (confirmed ? this.api.cancel(id).pipe(map(...), catchError(...)) : of({ kind: 'dismissed' }))),
);
```

`switchMap` = « quand la première opération émet, lance la seconde » (≈ `await` de deux appels successifs). `catchError` transforme l'erreur HTTP en valeur `{ kind: 'failed', error }` : l'écran n'a qu'un seul `subscribe` à écrire.

### `ng-template` + `ngTemplateOutlet`

« Mes réservations » affiche deux sections identiques (« À venir », « Historique ») : un seul fragment `<ng-template #section let-scope>` réutilisé par `<ng-container *ngTemplateOutlet="section; context: { $implicit: 'current' }" />`. Analogie : une vue partielle Razor avec son modèle.

### Questions d'entretien

- **`input()` vs `@Input()` ?** `input()` (Angular 17+) renvoie un **signal** en lecture seule : on le lit `event()` et il s'utilise dans `computed`/`rxResource` ; `@Input()` est une simple propriété.
- **Comment éviter une requête pour un visiteur ?** `params` qui renvoie `undefined` dans `rxResource`.
- **Que renvoie `HttpClient` pour un 204 ?** `null`, sans erreur.

### Exercice (facultatif)

Afficher le nombre de demandes « À venir » à côté du lien « Mes réservations » de l'en-tête : un `computed` sur une ressource `list('current', 1)` chargée seulement si l'utilisateur est connecté (même technique que `ContactStore`).

## 18. Réservations côté admin (tâche B) : dialogue avec résultat, état « occupé » par ligne, `@let`

Code : `front/src/app/features/admin/reservations/` (`pending-requests/`, `event-registrants/`, `refuse-dialog.ts`, `admin-reservation-actions.ts`), `core/api/admin-reservations.api.ts`.

### `MatDialog` qui **renvoie** une valeur (dialogue de refus)

- **C'est quoi** : `dialog.open(RefuseDialog)` crée le composant dans une couche au-dessus de la page et renvoie une `MatDialogRef`. Le dialogue se ferme avec `ref.close({ reason })` ; l'écran lit le résultat avec `ref.afterClosed()` (un `Observable` qui émet une seule fois : le résultat, ou `undefined` si l'admin ferme).
- **Pourquoi** : le dialogue ne connaît pas l'API ; il **rend** le motif, l'écran décide quoi en faire. Même schéma pour « Annuler la réservation » (`ConfirmDialogService`, résultat booléen).
- **Analogie .NET** : `ShowDialog()` en WinForms/WPF qui renvoie un `DialogResult` ; ou une page Blazor qui attend `await modal.Result`.
- Dans le composant du dialogue, `inject(MatDialogRef)` (≈ injection par constructeur) donne la référence pour se fermer. En test, on fournit `{ provide: MatDialogRef, useValue: { close: vi.fn() } }` et on vérifie `close` (spec `admin-reservations.spec.ts`).

### Formulaire réactif **typé** d'un seul champ

```ts
protected readonly reason = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });
```

- `nonNullable: true` : la valeur est toujours une `string` (un `reset()` remet `''`, pas `null`) → `reason.value.length` sans `?.`.
- `[formControl]="reason"` dans le template (pas besoin de `FormGroup` pour un champ). Compteur : `{{ reason.value.length }} / 500` dans un `<mat-hint align="end">`.
- Double protection : `maxlength` natif (le navigateur bloque la frappe) + `Validators.maxLength` (un collage peut passer) + le serveur (400 `errors.reason`, longueur comptée **après** suppression des espaces).

### État « occupé » **par ligne** : `signal<ReadonlySet<string>>`

```ts
protected readonly busyIds = signal<ReadonlySet<string>>(new Set());
this.busyIds.update((ids) => toggled(ids, id, true));   // nouvel ensemble à chaque fois
```

- **Piège** : `this.busyIds().add(id)` **muterait** l'ensemble sans changer la référence : le signal ne préviendrait personne (égalité par `Object.is`) et le bouton resterait actif. D'où la fonction pure `toggled` qui **copie** l'ensemble.
- **Analogie .NET** : `ImmutableHashSet<T>` — chaque ajout renvoie une nouvelle instance.
- Un clic sur la ligne A ne désactive pas la ligne B ; deux clics rapides sur A n'envoient qu'**un** appel (garde `if (busyIds().has(id)) return;` + `[disabled]`).

### Un service d'actions partagé, qui ne lève jamais

`AdminReservationActions.run(reservation, action)` enchaîne « dialogue éventuel → appel HTTP » avec `switchMap` et transforme toute erreur en `{ kind: 'failed', error }` (`catchError`). Les deux pages (« Demandes à traiter », « Inscrits ») n'ont qu'un `subscribe` : succès → message court ; échec → bandeau avec le message de l'API (409 `reservation_state_changed` : « Cette demande a déjà été traitée ou annulée. ») ; dans les deux cas → `resource.reload()` pour afficher le statut réel.

### Compteur du menu : un store singleton

`PendingCountStore` (`providedIn: 'root'`) garde `count = signal<number | null>`. `AdminLayout` l'affiche via un `computed` (« Demandes (3) » ou « Demandes ») et appelle `refresh()` à sa création ; les pages appellent `refresh()` après chaque action. `refresh()` fait `GET .../pending?pageSize=1` : on ne lit que `totalCount`.

### `@let` et `ng-template` avec plusieurs variables

- `@let data = registrants.value();` déclare une variable locale de template (Angular 18.1+), ≈ `@{ var data = Model.X; }` en Razor.
- `<ng-template #rows let-items let-group="group">` : `$implicit` → `items`, et une variable nommée `group`. Le contexte d'un template n'est **pas typé** : on redonne le type par une petite méthode (`asRows(items)`) pour que la vérification stricte du template reste active.

### Données personnelles côté front

Les coordonnées des inscrits ne vivent **que** dans la valeur de la ressource (mémoire). Rien dans `localStorage`/`sessionStorage` (test : `localStorage.length === 0` après affichage). L'API les sert en `Cache-Control: no-store` : le navigateur ne les écrit pas sur disque.

### Page vidée dont l'état est dans l'URL : `computed` + `effect` qui **navigue**

« Demandes à traiter » garde `?page=` dans l'URL (contrairement au § 17, signal local). Un `computed` calcule la dernière page valide quand la page revient vide alors que `totalCount > 0` ; un `effect` appelle `router.navigate([], { queryParams: { page }, replaceUrl: true })`. Règle : la **valeur** dans un `computed`, l'**effet de bord** (navigation) dans un `effect`. `replaceUrl` : le bouton Précédent ne ramène pas à la page vide. Garde-fou `lastPage < page` : jamais de navigation vers la même URL (sinon chargement sans fin).

### Noms accessibles des boutons répétés

Dix lignes = dix boutons « Confirmer » identiques pour un lecteur d'écran. `[attr.aria-label]="'Confirmer — ' + nom"` (`attr.` lie l'**attribut** HTML, et le retire si la valeur vaut `null` ; forme historique, toujours valable). Le texte visible reste **en tête** du nom (WCAG 2.5.3) pour la commande vocale ; un lien `target="_blank"` annonce « (nouvel onglet) ». Fonctions dans `reservations/row-labels.ts`.

### Questions d'entretien

- **`computed` ou `effect` ?** `computed` pour dériver une valeur (pur, mémorisé, paresseux) ; `effect` pour agir sur l'extérieur (navigation, stockage, log). Dériver une valeur dans un `effect` + `set()` est un anti-pattern.
- **Comment récupérer une valeur saisie dans un `MatDialog` ?** `dialogRef.afterClosed()` émet la valeur passée à `close(...)` (ou `undefined`).
- **Pourquoi un signal ne voit-il pas `set.add(x)` ?** Il compare les références (`Object.is`) ; il faut créer un nouvel objet (`new Set(old)`), ou fournir une fonction `equal`.
- **Où mettre une valeur partagée par le menu et des pages chargées à la demande ?** Dans un service `providedIn: 'root'` qui expose un signal en lecture seule (`asReadonly()`).

### Exercice (facultatif)

Dans « Inscrits », mettre à jour la ligne **sans relire toute la liste** après un succès : remplacer l'élément par la réponse de l'API (`AdminReservationDto`) et le déplacer dans le bon groupe selon son `status` (indice : `registrants.update(...)`), puis comparer avec la solution actuelle (`reload()`) en nombre d'appels et en simplicité.
