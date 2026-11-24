# ADR 0023 — Contenu des actualités : texte brut, rendu Angular sans HTML, limites et extrait

- **Statut** : Accepté (porteur, 24/11/2026)
- **Date** : 24/11/2026
- **Mise en œuvre** : étape 8, tranche « Actualités » (`technique/plan-actualites.md`)
- **Références** : mvp US-B1 (« un extrait »), US-B2 (« texte complet »), US-E1, R18 (« texte simple, sans image ») ; ADR 0014 (front), 0016 (formulaires), 0017 (CSP nginx), 0022 (modèle)

## Contexte

Une actualité est le **premier contenu long saisi par un administrateur et affiché à tous les visiteurs**. Si ce contenu pouvait contenir du HTML interprété, un compte admin compromis (ou une erreur de copier-coller) suffirait à exécuter du JavaScript dans le navigateur de chaque visiteur : c'est une **XSS stockée** (le script est enregistré en base, puis servi à tout le monde). Le jeton JWT est dans `localStorage` (ADR 0015) : une XSS le volerait.

Trois formats sont possibles : texte brut, Markdown, HTML riche (éditeur WYSIWYG). Il faut aussi fixer les limites de longueur, la façon d'afficher les paragraphes et la façon de produire l'« extrait » de la liste.

### Rappel : comment Angular protège (et comment on perd cette protection)

| Écriture dans le template | Ce qu'Angular fait | Risque |
|---|---|---|
| `{{ texte }}` (interpolation) | insère un **nœud texte** : `<script>` s'affiche tel quel, rien n'est interprété | aucun |
| `[href]="url"` | « assainit » l'URL : `javascript:...` devient `unsafe:javascript:...` | faible |
| `[innerHTML]="html"` | interprète le HTML **après** passage dans le `DomSanitizer` (retire `<script>`, les attributs `on*`, les URL `javascript:`) | moyen : le sanitizer laisse passer beaucoup de balises (mise en page cassée, liens trompeurs) |
| `bypassSecurityTrustHtml(...)` | **désactive** le sanitizer | élevé : à proscrire |

La CSP de nginx (`script-src 'self'`, ADR 0017) est une **seconde barrière** : un `<script>` injecté en ligne ne s'exécuterait pas. Mais une défense en profondeur ne remplace pas la première : on ne doit jamais compter sur elle.

## Décision

### 1. Texte brut, affiché uniquement par interpolation

- Le contenu est du **texte brut**. Aucun balisage n'est interprété : `<b>`, `**gras**`, `<script>` s'affichent tels qu'ils ont été tapés.
- Le front ne l'affiche **que** par interpolation `{{ }}`. **Interdits dans tout le front** : `[innerHTML]`, `bypassSecurityTrust*`, `ElementRef.nativeElement.innerHTML`. Point de contrôle du `code-reviewer` (recherche dans `front/src`).
- **Paragraphes** : une fonction pure `toParagraphs(text): string[]` (front, `shared/news/`) découpe le texte sur les lignes vides ; chaque paragraphe est rendu dans un `<p>` par `@for`, avec `white-space: pre-line` pour garder les retours à la ligne simples. On obtient de vrais paragraphes (lecteurs d'écran : navigation par paragraphe) **sans** produire de HTML.
- Un composant de présentation unique `<app-news-article>` affiche titre, date et texte : il sert au détail public **et** à l'aperçu du formulaire admin, ce qui garantit le même rendu.
- Les adresses web restent du texte (pas de lien cliquable au MVP ; voir QN-5 du plan).

### 2. Le serveur stocke le texte tel quel

- **Aucun encodage HTML côté serveur** : on encode à l'**affichage**, selon le contexte (HTML, attribut, URL), pas au stockage. Encoder au stockage produirait `&lt;` à l'écran et des doubles encodages (`&amp;amp;`), et ne protégerait pas d'autres contextes (JSON, futur RAG).
- Normalisation à l'écriture, dans le Domain (`NewsRules.NormalizeContent`) : `\r\n` et `\r` → `\n` (le navigateur envoie `\n` ; `curl` ou un copier-coller Windows peuvent envoyer `\r\n`, ce qui fausserait le décompte des caractères), puis `Trim()`. Le titre est seulement `Trim()`-é.
- Réponses JSON (`application/json`) : le navigateur ne les interprète jamais comme du HTML.

### 3. Limites de longueur

| Champ | Limite | Où |
|---|---|---|
| Titre | 1 à **200** caractères après `Trim` (comme un événement, RG-13) | `NewsRules.Validate` (Domain) ; DTO `[Required]` |
| Texte | 1 à **8 000** caractères après normalisation | idem ; colonne `nvarchar(max)` |

- La longueur est contrôlée **après** normalisation, dans le Domain (une seule source), et renvoyée en 400 par champ (`errors.title`, `errors.content`) avec les messages « Le titre est obligatoire. », « Le titre ne doit pas dépasser 200 caractères. », « Le texte est obligatoire. », « Le texte ne doit pas dépasser 8 000 caractères. ».
- Pas d'attribut `[MaxLength]` sur le texte du DTO : il compterait les `\r` avant normalisation et refuserait un texte valide (même raisonnement que le motif de refus, T-B5).
- 8 000 caractères ≈ 1 300 mots, deux pages : assez pour un compte rendu d'assemblée générale. Au-delà de 4 000 caractères, SQL Server impose `nvarchar(max)` (`nvarchar(n)` s'arrête à 4 000) ; c'est sans conséquence ici (la colonne n'est ni indexée ni triée).
- Le front reprend les mêmes limites (validateurs et compteur « 123 / 8 000 ») à titre de confort.

### 4. Extrait calculé, pas stocké

- La liste publique renvoie `excerpt` : le texte, retours à la ligne remplacés par des espaces, **coupé à 200 caractères au plus sur une fin de mot**, suivi de « … » s'il a été tronqué. Un mot de plus de 200 caractères (une URL) est coupé net. La coupe ne sépare jamais une paire de substitution UTF-16 (émoji).
- Calcul dans le Domain (`NewsRules.Excerpt(string)`, fonction pure testée sans base).
- La requête de liste ne charge pas le texte entier : la projection lit `Content.Substring(0, 400)` (traduit en `SUBSTRING` par EF), puis le Domain coupe. Pas de colonne `Excerpt` à tenir à jour.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| **Markdown** (bibliothèque `marked` ou `ngx-markdown`, rendu par `[innerHTML]`) | Exige une bibliothèque de plus (taille du bundle, mises à jour de sécurité), un passage par `[innerHTML]` (donc une confiance dans le sanitizer, idéalement doublé de DOMPurify), et une syntaxe que des bénévoles ne connaissent pas. R18 demande « texte simple ». Possible plus tard sans migration : la colonne contient déjà du texte. |
| **Éditeur riche HTML** (Quill, TinyMCE) | Même risque que le Markdown en plus fort (HTML arbitraire stocké), dépendance lourde, CSP à élargir pour certains éditeurs. |
| `white-space: pre-line` sur un seul bloc (comme la description d'un événement) | Fonctionne visuellement, mais un seul `<p>` géant pour un texte de plusieurs paragraphes est moins accessible ; `toParagraphs` coûte quelques lignes. |
| Liens cliquables détectés automatiquement (fonction qui découpe le texte en segments texte / URL, rendus par `@for` avec `<a [href]>` limité à `https?://`) | Sûr (aucun `innerHTML`), mais hors des user stories ; proposé en question (QN-5). |
| Champ « résumé » saisi par l'admin | Un champ obligatoire de plus pour un bénévole ; l'extrait automatique suffit à US-B1 (QN-3). |
| Colonne `Excerpt` calculée et stockée | À recalculer à chaque modification ; la projection `SUBSTRING` est aussi efficace au volume attendu. |
| Encodage HTML au stockage | Voir § 2 : mauvais endroit, doubles encodages. |
| Limite de 4 000 caractères (`nvarchar(4000)`, comme la description d'un événement) | Plus homogène, mais court pour un compte rendu ; à reprendre si l'`expert-fonctionnel` le préfère (QN-1). |

## Conséquences

- Aucune dépendance npm ni NuGet nouvelle ; CSP inchangée.
- Test front obligatoire : un texte contenant `<script>alert(1)</script>` et `<img src=x onerror=alert(1)>` s'affiche **en texte**, et le DOM rendu ne contient **aucun** élément `script` ni `img`.
- Test d'intégration : ce même texte est stocké et renvoyé **à l'identique** par l'API (aucun encodage serveur).
- Si le Markdown est demandé un jour : nouvel ADR (bibliothèque, sanitizer, DOMPurify, tests XSS), sans migration de données.
- Piste de durcissement pour S3 (non décidée ici) : CSP `require-trusted-types-for 'script'` (Trusted Types), compatible avec Angular, à vérifier avec Angular Material.

## Reste à valider

- Par le porteur : texte brut, limites 200 / 8 000, extrait de 200 caractères calculé.
- Par l'`expert-fonctionnel` : QN-1 (8 000), QN-3 (extrait), QN-5 (liens cliquables) du plan.
