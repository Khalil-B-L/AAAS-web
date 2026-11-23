/** Seule donnée gardée dans le navigateur : le jeton et son échéance locale (ADR 0015, données minimales). */
export interface StoredSession {
  accessToken: string;
  /** Échéance en millisecondes, calculée avec l'horloge du poste au moment de la connexion (Date.now()). */
  localDeadline: number;
}

export const SESSION_STORAGE_KEY = 'aaas.session';

// localStorage peut lever une exception (navigation privée, quota, stockage désactivé) : on ne plante jamais.

export function readStoredSession(): StoredSession | null {
  try {
    const raw = localStorage.getItem(SESSION_STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const value: unknown = JSON.parse(raw);
    if (
      value &&
      typeof value === 'object' &&
      typeof (value as StoredSession).accessToken === 'string' &&
      typeof (value as StoredSession).localDeadline === 'number'
    ) {
      return value as StoredSession;
    }
    return null;
  } catch {
    return null;
  }
}

export function writeStoredSession(session: StoredSession): void {
  try {
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
  } catch {
    // Session gardée en mémoire seulement : elle sera perdue au rechargement de la page.
  }
}

export function clearStoredSession(): void {
  try {
    localStorage.removeItem(SESSION_STORAGE_KEY);
  } catch {
    // rien à faire
  }
}
