/**
 * Identité de l'association affichée par le front : SEUL endroit à modifier pour changer le nom, le logo
 * ou les coordonnées (question UX-2 du plan front). Les couleurs sont dans src/theme/_brand.scss.
 * Le titre de index.html (affiché avant le démarrage d'Angular) reprend `name` : le changer aussi là.
 */
export const APP_INFO = {
  /** Nom affiché dans l'en-tête, le pied de page et les titres d'onglet. */
  name: 'A.A.A.S',
  /** Nom officiel (statuts), utilisé dans la politique de confidentialité et le texte alternatif du logo. */
  legalName: "Association d'amitié et d'action sociale",
  /** Coordonnées pour les demandes sur les données personnelles (politique de confidentialité, Q13). */
  postalAddress: '[ADRESSE POSTALE]',
  contactEmail: '[E-MAIL DE CONTACT]',
  /** Date d'entrée en vigueur de la politique de confidentialité affichée. */
  privacyPolicyEffectiveDate: '[DATE DE MISE EN LIGNE]',
  /**
   * Logo officiel : fichier de front/public/ (servi à la racine du site), ou null pour n'afficher que le nom.
   * Fichier local obligatoire : la Content-Security-Policy interdit les images d'un autre site (ADR 0017).
   * Largeur et hauteur d'origine : le navigateur réserve la place avant le chargement (pas de décalage).
   */
  logo: {
    url: '/logo-aaas.jpg',
    width: 466,
    height: 429,
  } as { readonly url: string; readonly width: number; readonly height: number } | null,
} as const;

/**
 * Texte alternatif du logo : le texte écrit dans l'image (nom complet). Le sigle « A.A.A.S » est déjà affiché
 * à côté du logo : le répéter ferait lire deux fois la même chose au lecteur d'écran.
 */
export const LOGO_ALT = APP_INFO.legalName;
