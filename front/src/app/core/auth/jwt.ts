// Décodage de la charge utile d'un JWT, sans bibliothèque (ADR 0015).
// La signature n'est PAS vérifiée : le front n'en a pas la clé, et ces informations ne servent qu'à l'affichage
// (prénom, menu) et aux guards. La sécurité réelle est vérifiée par l'API à chaque requête.

export interface JwtClaims {
  sub?: string;
  given_name?: string;
  /** Une chaîne pour un seul rôle, un tableau pour plusieurs (sérialisation standard des claims répétés). */
  role?: string | string[];
  exp?: number;
  iat?: number;
}

export function decodeJwtPayload(token: string): JwtClaims | null {
  const parts = token.split('.');
  if (parts.length !== 3) {
    return null;
  }
  try {
    // Base64url → Base64 classique, puis octets → texte UTF-8 (le prénom peut contenir des accents).
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const bytes = Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
    const payload: unknown = JSON.parse(new TextDecoder().decode(bytes));
    return payload && typeof payload === 'object' ? (payload as JwtClaims) : null;
  } catch {
    return null;
  }
}

/** Normalise le claim `role` (chaîne ou tableau) en tableau. */
export function normalizeRoles(role: unknown): string[] {
  if (typeof role === 'string') {
    return [role];
  }
  if (Array.isArray(role)) {
    return role.filter((r): r is string => typeof r === 'string');
  }
  return [];
}

/** Durée de vie du jeton en millisecondes, lue dans le jeton (exp − iat) ; repli sur 60 min (ADR 0002). */
export function tokenLifetimeMs(claims: JwtClaims): number {
  if (typeof claims.exp === 'number' && typeof claims.iat === 'number' && claims.exp > claims.iat) {
    return (claims.exp - claims.iat) * 1000;
  }
  return 60 * 60_000;
}
