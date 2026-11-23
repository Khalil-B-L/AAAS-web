import { environment } from '../../../environments/environment';

/** Construit l'URL d'un endpoint : `apiUrl('/api/events')` → '/api/events' en local (chemin relatif, ADR 0017). */
export function apiUrl(path: string): string {
  return `${environment.apiBaseUrl}${path}`;
}

/** Préfixe des requêtes qui reçoivent le jeton (intercepteur, ADR 0015). */
export const API_PREFIX = apiUrl('/api/');
