import { NewsStatus } from '../../core/models/api-models';

// Libellés : docs/fonctionnel/libelles-ecrans.md § 13.
export const NEWS_STATUS_LABELS: Record<NewsStatus, string> = {
  Draft: 'Brouillon',
  Published: 'Publiée',
};

export const NEWS_TITLE_MAX_LENGTH = 200;
export const NEWS_CONTENT_MAX_LENGTH = 8000;

/**
 * Longueur comptée comme l'API (NewsRules.NormalizeContent) : \r\n → \n puis espaces de début et de fin retirés.
 * Le compteur et le validateur du front ne contredisent ainsi jamais la réponse du serveur.
 */
export function newsContentLength(text: string): number {
  return text.replace(/\r\n?/g, '\n').trim().length;
}
