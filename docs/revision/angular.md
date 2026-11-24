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
