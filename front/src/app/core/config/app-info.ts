/**
 * Identité de l'association affichée par le front : SEUL endroit à modifier pour changer le nom ou le logo.
 * Valeurs neutres en attendant le choix du porteur (question UX-2 du plan front).
 * Le titre de index.html (affiché avant le démarrage d'Angular) reprend `name` : le changer aussi là.
 */
export const APP_INFO = {
  /** Nom complet, affiché dans l'en-tête, le pied de page et les titres d'onglet. */
  name: 'Association',
  /**
   * Logo facultatif : chemin d'un fichier placé dans front/public/ (ex. '/logo.svg'), ou null pour n'afficher que le nom.
   * Fichier local obligatoire : la Content-Security-Policy interdit les images d'un autre site (ADR 0017).
   */
  logoUrl: null as string | null,
} as const;
