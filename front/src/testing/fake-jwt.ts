// Aide aux tests : fabrique un JWT NON signé (la signature n'est jamais vérifiée par le front).
// Importé uniquement par des fichiers *.spec.ts : jamais présent dans le bundle de l'application.

function base64Url(text: string): string {
  const bytes = new TextEncoder().encode(text);
  let binary = '';
  bytes.forEach((b) => (binary += String.fromCharCode(b)));
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function fakeJwt(payload: Record<string, unknown>): string {
  return `${base64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))}.${base64Url(JSON.stringify(payload))}.signature`;
}

/** Jeton d'une heure, émis « maintenant » selon l'horloge du SERVEUR (qui peut différer de celle du poste). */
export function memberToken(overrides: Record<string, unknown> = {}): string {
  const iat = 1_800_000_000;
  return fakeJwt({ sub: 'u-1', given_name: 'Salma', role: 'User', iat, exp: iat + 3600, ...overrides });
}

export function adminToken(overrides: Record<string, unknown> = {}): string {
  return memberToken({ sub: 'a-1', given_name: 'Admin', role: ['Admin', 'User'], ...overrides });
}
