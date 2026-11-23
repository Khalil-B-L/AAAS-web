/** Page d'arrivée par défaut après connexion ou inscription (UX-10). */
export const DEFAULT_AFTER_LOGIN = '/evenements';

/**
 * N'accepte qu'un chemin interne (« /mon-compte ») : pas de redirection ouverte vers un autre site.
 * Refusés : '//exemple.com' et '/\exemple.com' (les navigateurs les lisent comme une autre origine),
 * 'https://…', 'javascript:…', et les pages de connexion elles-mêmes (boucle).
 */
export function safeReturnUrl(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return DEFAULT_AFTER_LOGIN;
  }
  if (value.startsWith('/connexion') || value.startsWith('/inscription')) {
    return DEFAULT_AFTER_LOGIN;
  }
  return value;
}
