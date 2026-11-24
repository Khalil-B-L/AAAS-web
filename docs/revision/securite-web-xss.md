# Fiche de révision — Sécurité web : XSS, encodage à la sortie, Angular et CSP

> À relire la veille d'un entretien. Exemples tirés de la tranche Actualités d'AAAS (ADR 0023) : un administrateur saisit un texte libre affiché à tous les visiteurs.

## 1. XSS : c'est quoi

**Cross-site scripting** : faire exécuter du JavaScript à la victime **dans le site**, donc avec ses droits. Ici, le jeton JWT est dans `localStorage` (ADR 0015) : un script injecté peut le lire et l'envoyer ailleurs, puis agir au nom de l'utilisateur.

| Famille | D'où vient le script | Exemple AAAS |
|---|---|---|
| **Stockée** | enregistrée en base, servie à tous | le texte d'une actualité contenant `<script>` (compte admin compromis, copier-coller malheureux) |
| **Réfléchie** | vient de l'URL et revient dans la page | `/recherche?q=<script>…` recopié dans la réponse HTML |
| **DOM** | le JavaScript du front insère lui-même une donnée non sûre | `element.innerHTML = location.hash` |

## 2. La règle d'or : encoder à la sortie, selon le contexte

- On **n'encode pas à l'entrée** : le serveur stocke ce qu'il reçoit (ici après `Trim` et `\r\n` → `\n`, rien d'autre).
- C'est le **moteur de rendu** qui encode, au moment d'afficher, **selon le contexte** : texte HTML (`<` → `&lt;`), attribut, URL, JavaScript, CSS. Chaque contexte a ses propres règles.
- Pourquoi pas `HtmlEncode` avant l'`INSERT` ?
  - mauvais contexte pour les autres usages (JSON, export CSV, futur RAG : ils recevraient `&lt;`) ;
  - doubles encodages (`&amp;amp;`) dès qu'un écran encode à nouveau ;
  - fausse sécurité : on croit être protégé alors que l'affichage, lui, ne l'est peut-être pas (attribut, URL…).

**Dans le projet** : `NewsRules.NormalizeContent` ne fait aucun encodage ; le test d'intégration `AdminNewsTests.Malicious_text_is_returned_verbatim` vérifie que `<script>alert(1)</script><img src=x onerror=alert(1)>` est stocké **et** renvoyé à l'identique ; la réponse est en `application/json` (le navigateur ne l'interprète jamais comme du HTML).

## 3. Ce qu'Angular fait pour nous (et comment on perd cette protection)

| Écriture dans le template | Effet | Risque |
|---|---|---|
| `{{ texte }}` (interpolation) | crée un **nœud texte** : `<script>` s'affiche à l'écran | aucun |
| `[attr.title]="x"`, `[value]="x"` | propriété ou attribut, encodé | aucun |
| `[href]="url"` | URL **assainie** : `javascript:…` devient `unsafe:javascript:…` | faible |
| `[innerHTML]="html"` | HTML interprété **après** le `DomSanitizer` (retire `<script>`, `on*`, `javascript:`) | moyen : beaucoup de balises passent (mise en page cassée, liens trompeurs) |
| `bypassSecurityTrustHtml(...)` | **désactive** le sanitizer | élevé : à proscrire |
| `ElementRef.nativeElement.innerHTML = …` | contourne Angular entièrement | élevé |

**Analogie .NET** : `{{ }}` ≈ `@Model.Texte` en Razor (encodé par défaut) ; `[innerHTML]` ≈ `@Html.Raw(...)`.

### Dans le projet

- `shared/news/news-article.ts` : titre et paragraphes affichés **uniquement** par `{{ }}`.
- `shared/news/paragraphs.ts` : `toParagraphs(text)` découpe sur les lignes vides, chaque paragraphe va dans un `<p>` par `@for`. On obtient de vrais paragraphes (lecteurs d'écran) **sans produire de HTML**. Les retours à la ligne simples sont gardés par CSS (`white-space: pre-line`).
- **Règle ESLint** (`front/eslint.config.js`, `no-restricted-syntax`) : `npm run lint` échoue sur `[innerHTML]` / `[outerHTML]` / `[srcdoc]` dans un template, et sur `.innerHTML`, `.outerHTML`, `insertAdjacentHTML`, `document.write`, `bypassSecurityTrust*` dans le TypeScript. Sélecteurs utilisés : `BoundAttribute[name=/^(innerHTML|outerHTML|srcdoc)$/i]` (AST des templates d'angular-eslint) et `MemberExpression[property.name=/^bypassSecurityTrust/]` (AST TypeScript).
- **Test DOM** (`features/news.spec.ts`) : le composant reçoit un texte malveillant, le test vérifie que le texte est visible **et** que `querySelector('script, img, b')` ne trouve rien.

## 4. Défense en profondeur : la CSP

- **Content-Security-Policy** : en-tête HTTP qui dit au navigateur d'où le code peut venir. Ici (nginx, ADR 0017) `script-src 'self'` : un `<script>` **en ligne** injecté ne s'exécuterait pas, ni un `onerror=…`.
- C'est une **seconde barrière**. On ne compte jamais sur elle seule : une CSP peut être élargie demain (un widget, une analyse d'audience), ou contournée par un script déjà autorisé.
- Piste de durcissement (S3, non décidée) : `require-trusted-types-for 'script'` (**Trusted Types**). Le navigateur refuse alors toute affectation de chaîne brute à `innerHTML` et aux autres « puits » dangereux. Angular le prend en charge ; il faut vérifier la compatibilité avec Angular Material.

## 5. Pièges

- « Le sanitizer d'Angular nettoie, donc `[innerHTML]` est sûr » : il retire les scripts, pas les liens trompeurs, les formulaires ni les mises en page cassées. Et un jour, quelqu'un ajoutera `bypassSecurityTrustHtml` « parce que le style ne passe pas ».
- Markdown « pour faire joli » : il faut une bibliothèque, un rendu en HTML, donc `[innerHTML]`, idéalement doublé de DOMPurify, plus des tests XSS. Il faut un nouvel ADR (ADR 0023, alternatives).
- Encoder dans l'API **et** dans le front donne `&lt;` visible à l'écran.
- Chercher les régressions : `grep -rn "innerHTML\|bypassSecurityTrust" front/src` (la règle ESLint le fait à chaque `npm run lint`).

## Questions d'entretien

1. **Pourquoi ne pas encoder en HTML avant d'enregistrer ?** Mauvais contexte pour les autres usages (JSON, exports), doubles encodages, et fausse sécurité. On encode à la sortie, selon le contexte.
2. **Comment Angular protège-t-il de la XSS ?** L'interpolation crée des nœuds texte ; les liaisons `[href]`, `[src]`, `[style]` sont assainies ; `[innerHTML]` passe par le `DomSanitizer`. On perd la protection avec `bypassSecurityTrust*` ou en touchant le DOM directement.
3. **À quoi sert une CSP si le code est déjà sûr ?** Défense en profondeur : si une injection passe malgré tout, `script-src 'self'` empêche les scripts en ligne de s'exécuter.
4. **XSS stockée, réfléchie, DOM : laquelle menace un module d'actualités ?** La stockée : un texte enregistré une fois est servi à tous les visiteurs.

## Exercice (facultatif)

Ajoutez des liens cliquables **sans** `innerHTML` : écrivez une fonction pure `toSegments(text)` qui renvoie `{ kind: 'text' | 'url', value }[]`, puis affichez-la par `@for`, avec `<a [href]>` seulement pour les URL `https?://`. Testez-la avec `javascript:alert(1)` (qui doit rester du texte).
