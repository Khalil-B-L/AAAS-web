# ADR 0010 — Prix des événements en dinars tunisiens

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 18/11/2026
- **Mise en œuvre** : étape 2
- **Références** : cahier RG-08 ; mvp R12, US-E2 (« 12,500 TND »), US-C1 (« 15 TND », « Gratuit »)

## Contexte

Le prix est facultatif, en TND, ≥ 0, avec au plus 3 décimales (le dinar se divise en 1 000 millimes). Vide ou 0 = gratuit. Il n'y a qu'une devise et aucun calcul (pas de paiement en ligne, pas de total) : le prix est affiché, et l'admin note « payée » à la main (étape 7). La version 6 de `architecture.md` prévoyait `decimal(9,3)` et un objet valeur `Money` ; la demande de conception de l'étape 2 évoquait `decimal(10,3)`.

## Décision

1. **Colonne** `Events.Price decimal(9,3) NULL`, contrainte `CK_Events_Price CHECK (Price IS NULL OR Price > 0)`.
   - Précision 9 : jusqu'à 999 999,999 TND, largement au-dessus de tout prix associatif. SQL Server stocke un `decimal` de précision 1 à 9 sur **5 octets**, de 10 à 19 sur **9 octets** : `decimal(10,3)` coûterait 4 octets par ligne sans aucun gain. On garde donc `(9,3)`.
   - `NULL` = gratuit ; **0 est normalisé en `NULL`** par le Domain, d'où `> 0` dans la contrainte : une seule représentation de « gratuit ».
2. **Domain** : propriété `decimal? Price` sur `Event`, normalisée et validée par `EventRules` (ADR 0009) :
   - négatif → erreur sur `price` : « Le prix ne peut pas être négatif. » ;
   - plus de 3 décimales → « Le prix est en dinars, avec au plus 3 décimales (millimes). » (refus, **pas d'arrondi silencieux**) ;
   - au-dessus de 99 999,999 → « Le prix ne doit pas dépasser 99 999,999 TND. » (borne de saisie anti-erreur, *à confirmer, E2-5*) ;
   - 0 → `null`.
   - Propriété calculée `IsFree => Price is null`.
   **Pas d'objet valeur `Money`** : une seule devise, une seule utilisation, aucune arithmétique. Un `Money` ajouterait un convertisseur EF et du code sans règle supplémentaire. À réintroduire si un jour on calcule des totaux d'encaissement (hors MVP).
3. **API** : `price` est un **nombre JSON** (`12.5`) ou `null`. `System.Text.Json` lit un nombre JSON directement en `decimal`, sans passer par `double`, donc sans perte. Le nombre de décimales est vérifié sur le `decimal` reçu (`decimal.Round(p, 3) == p`).
4. **Affichage (front, S2)** : pipe `tndPrice` :
   - `null` → « Gratuit » ;
   - entier → « 15 TND, à régler sur place » ;
   - sinon → 3 décimales avec la virgule française : « 12,500 TND, à régler sur place ».
   *Cette règle concilie US-C1 (« 15 TND ») et US-E2 (« 12,500 TND ») ; à confirmer par l'`expert-fonctionnel` (E2-3).*
   Saisie : champ texte acceptant la virgule ou le point, converti en nombre par le front.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| `decimal(10,3)` | 9 octets au lieu de 5, aucun prix réaliste ne le justifie. |
| Entier en millimes (`int`) | Évite toute question de décimales, mais oblige à convertir partout (DTO, affichage, saisie) ; source d'erreurs « ×1000 » et moins lisible en base pour une association. |
| `money` / `smallmoney` SQL Server | 4 décimales fixes, arrondis propres à SQL Server, déconseillés en général. |
| Prix en chaîne dans le JSON (`"12.500"`) | Conserverait les zéros affichés, mais l'affichage est de toute façon fait par le front ; un nombre est plus simple à valider et à typer en TypeScript. |
| Objet valeur `Money` (prévu en v6) | Voir point 2 : pas de règle qui le justifie au MVP. |
| Arrondir à 3 décimales au lieu de refuser | Un prix modifié en silence est pire qu'un message d'erreur. |

## Conséquences

- La règle « payée » de l'étape 7 (R13) s'appuie sur `IsFree` : la mention n'est proposée que si l'événement est payant.
- Tests Domain : `null`, 0 → `null`, 12.5, 12.500, 12.5001 (refusé), −1 (refusé), 99 999.999 (accepté), 100 000 (refusé).
- `architecture.md` v7 retire `Money` de la structure du Domain.

## Reste à valider

- Par le porteur : `decimal(9,3)` plutôt que `(10,3)`, abandon de `Money`.
- Par l'`expert-fonctionnel` : E2-3 (format d'affichage), E2-5 (borne maximale).
