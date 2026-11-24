import { fakeJwt } from '../../../testing/fake-jwt';
import { decodeJwtPayload, normalizeRoles, tokenLifetimeMs } from './jwt';
import { DEFAULT_AFTER_LOGIN, safeReturnUrl } from './return-url';

describe('jwt', () => {
  it('décode la charge utile, accents compris (UTF-8)', () => {
    const claims = decodeJwtPayload(fakeJwt({ sub: 'u-1', given_name: 'Héla', role: 'User' }));
    expect(claims).toMatchObject({ sub: 'u-1', given_name: 'Héla', role: 'User' });
  });

  it('renvoie null pour un jeton illisible', () => {
    expect(decodeJwtPayload('abc')).toBeNull();
    expect(decodeJwtPayload('a.%%%.c')).toBeNull();
  });

  it('normalise le claim role, chaîne (un rôle) ou tableau (plusieurs)', () => {
    expect(normalizeRoles('User')).toEqual(['User']);
    expect(normalizeRoles(['Admin', 'User'])).toEqual(['Admin', 'User']);
    expect(normalizeRoles(undefined)).toEqual([]);
    expect(normalizeRoles([1, 'Admin'])).toEqual(['Admin']);
  });

  it('calcule la durée de vie à partir de exp − iat, repli sur 60 minutes', () => {
    expect(tokenLifetimeMs({ iat: 100, exp: 1900 })).toBe(1_800_000);
    expect(tokenLifetimeMs({ exp: 1900 })).toBe(3_600_000);
  });
});

describe('safeReturnUrl', () => {
  it('accepte un chemin interne', () => {
    expect(safeReturnUrl('/mon-compte')).toBe('/mon-compte');
    expect(safeReturnUrl('/evenements?page=2')).toBe('/evenements?page=2');
  });

  it.each(['//exemple.com', '/\\exemple.com', 'https://exemple.com', 'javascript:alert(1)', '', null, undefined])(
    'refuse %s (redirection ouverte) et renvoie vers les événements',
    (value) => {
      expect(safeReturnUrl(value)).toBe(DEFAULT_AFTER_LOGIN);
    },
  );

  it('évite une boucle vers la connexion ou l’inscription', () => {
    expect(safeReturnUrl('/connexion?returnUrl=/x')).toBe(DEFAULT_AFTER_LOGIN);
    expect(safeReturnUrl('/inscription')).toBe(DEFAULT_AFTER_LOGIN);
  });
});
