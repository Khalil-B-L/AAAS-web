/**
 * Découpe un texte brut en paragraphes sur les lignes vides (ou ne contenant que des espaces), ADR 0023 § 1.
 * Chaque paragraphe est ensuite affiché dans un <p> par interpolation : on obtient de vrais paragraphes
 * (navigation au lecteur d'écran) SANS produire de HTML, donc sans risque XSS.
 * Les retours à la ligne simples restent dans le paragraphe (affichés par `white-space: pre-line`).
 */
export function toParagraphs(text: string | null | undefined): string[] {
  return (text ?? '')
    .replace(/\r\n?/g, '\n')
    .split(/\n[ \t]*\n/)
    .map((paragraph) => paragraph.trim())
    .filter((paragraph) => paragraph.length > 0);
}
