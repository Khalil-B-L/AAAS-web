import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { adminToken, fakeJwt, memberToken } from '../../../testing/fake-jwt';
import { PAGE_RELOAD } from '../browser/page-reload';
import { NotificationService, PENDING_NOTIFICATION_KEY } from '../notifications/notification.service';
import { ACCOUNT_CHANGED_MESSAGE, AuthService, EXPIRED_REASON, SESSION_EXPIRED_MESSAGE, isProtectedUrl } from './auth.service';
import { SESSION_STORAGE_KEY, readStoredSession } from './session-storage';

describe('AuthService', () => {
  // Horloge du POSTE volontairement très différente de celle du serveur (iat = 1 800 000 000 s, soit 2027) :
  // la session doit fonctionner quand même, car l'échéance est relative (ADR 0015).
  const NOW = new Date('2026-11-23T10:00:00Z').getTime();

  function setup() {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    return { service: TestBed.inject(AuthService), router, navigate, navigateByUrl };
  }

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    localStorage.clear();
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('démarre sans session si rien n’est stocké', () => {
    const { service } = setup();
    expect(service.isLoggedIn()).toBe(false);
    expect(service.accessToken()).toBeNull();
  });

  it('ouvre une session : signals, stockage minimal, échéance = durée du jeton − 60 s', () => {
    const { service } = setup();
    const token = memberToken();
    service.startSession(token);

    expect(service.isLoggedIn()).toBe(true);
    expect(service.isAdmin()).toBe(false);
    expect(service.firstName()).toBe('Salma');
    expect(service.accessToken()).toBe(token);
    expect(readStoredSession()).toEqual({ accessToken: token, localDeadline: NOW + 3600_000 - 60_000 });
    // Rien d'autre que le jeton et l'échéance dans le navigateur (pas de profil).
    expect(Object.keys(JSON.parse(localStorage.getItem(SESSION_STORAGE_KEY) ?? '{}'))).toEqual([
      'accessToken',
      'localDeadline',
    ]);
  });

  it('reconnaît un admin (claim role en tableau)', () => {
    const { service } = setup();
    service.startSession(adminToken());
    expect(service.isAdmin()).toBe(true);
  });

  it('restaure la session au démarrage, sans appel réseau', () => {
    const token = adminToken();
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ accessToken: token, localDeadline: NOW + 10 * 60_000 }));
    const { service } = setup();
    expect(service.isAdmin()).toBe(true);
  });

  it('ignore et efface une session échue', () => {
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ accessToken: memberToken(), localDeadline: NOW - 1 }));
    const { service } = setup();
    expect(service.isLoggedIn()).toBe(false);
    expect(localStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
  });

  it('ignore un contenu de stockage corrompu ou un jeton sans sub', () => {
    localStorage.setItem(SESSION_STORAGE_KEY, '{pas du json');
    expect(setup().service.isLoggedIn()).toBe(false);
    TestBed.resetTestingModule();
    localStorage.setItem(
      SESSION_STORAGE_KEY,
      JSON.stringify({ accessToken: fakeJwt({ given_name: 'X' }), localDeadline: NOW + 60_000 }),
    );
    expect(setup().service.isLoggedIn()).toBe(false);
  });

  it('se déconnecte (US-A3) : revient aux événements PUIS efface tout, sans appel API', async () => {
    const { service, navigateByUrl } = setup();
    service.startSession(memberToken());
    service.logout();
    // La session n'est effacée qu'une fois la navigation acceptée.
    expect(service.isLoggedIn()).toBe(true);
    await vi.waitFor(() => expect(service.isLoggedIn()).toBe(false));
    expect(localStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
    // 'reload' : depuis /evenements, la navigation n'est pas ignorée (sinon la promesse renverrait false).
    expect(navigateByUrl).toHaveBeenCalledWith('/evenements', { onSameUrlNavigation: 'reload' });
  });

  it('déconnexion refusée par la garde de sortie (« Rester ») : la session reste ouverte', async () => {
    const { service, navigateByUrl } = setup();
    navigateByUrl.mockResolvedValue(false);
    service.startSession(adminToken());
    service.logout();
    await Promise.resolve();
    await Promise.resolve();
    expect(service.isLoggedIn()).toBe(true);
    expect(localStorage.getItem(SESSION_STORAGE_KEY)).not.toBeNull();
  });

  it('déconnexion : une erreur de navigation ne bloque pas la sortie', async () => {
    const { service, navigateByUrl } = setup();
    navigateByUrl.mockRejectedValue(new Error('chunk introuvable'));
    service.startSession(memberToken());
    service.logout();
    await vi.waitFor(() => expect(service.isLoggedIn()).toBe(false));
  });

  it('autre compte ouvert dans un autre onglet : message conservé puis rechargement complet de la page', () => {
    const reload = vi.fn();
    TestBed.configureTestingModule({ providers: [{ provide: PAGE_RELOAD, useValue: reload }] });
    const { service, router } = setup();
    vi.spyOn(router, 'url', 'get').mockReturnValue('/mon-compte');
    service.startSession(memberToken()); // sub u-1

    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ accessToken: adminToken(), localDeadline: NOW + 60_000 }));
    window.dispatchEvent(new StorageEvent('storage', { key: SESSION_STORAGE_KEY }));

    expect(service.session()?.userId).toBe('a-1');
    expect(reload).toHaveBeenCalledTimes(1);
    expect(JSON.parse(sessionStorage.getItem(PENDING_NOTIFICATION_KEY) ?? '{}')).toEqual({
      message: ACCOUNT_CHANGED_MESSAGE,
      kind: 'warning',
    });
  });

  it('même compte reconnecté dans un autre onglet (nouveau jeton) : pas de rechargement', () => {
    const reload = vi.fn();
    TestBed.configureTestingModule({ providers: [{ provide: PAGE_RELOAD, useValue: reload }] });
    const { service } = setup();
    service.startSession(memberToken());

    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ accessToken: memberToken({ iat: 1_800_000_100 }), localDeadline: NOW + 60_000 }));
    window.dispatchEvent(new StorageEvent('storage', { key: SESSION_STORAGE_KEY }));

    expect(service.isLoggedIn()).toBe(true);
    expect(reload).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(PENDING_NOTIFICATION_KEY)).toBeNull();
  });

  it("à l'échéance sur une page réservée : renvoie vers la connexion avec le motif et le retour", () => {
    const { service, router, navigate } = setup();
    vi.spyOn(router, 'url', 'get').mockReturnValue('/mon-compte');
    service.startSession(memberToken());

    vi.advanceTimersByTime(3600_000 - 60_000);

    expect(service.isLoggedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/connexion'], {
      queryParams: { returnUrl: '/mon-compte', raison: EXPIRED_REASON },
    });
  });

  it("à l'échéance sur une page publique : simple message, pas de redirection", () => {
    const { service, router, navigate } = setup();
    vi.spyOn(router, 'url', 'get').mockReturnValue('/evenements');
    service.startSession(memberToken());

    vi.advanceTimersByTime(3600_000 - 60_000);

    expect(service.isLoggedIn()).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
    expect(TestBed.inject(NotificationService).current()?.message).toBe(SESSION_EXPIRED_MESSAGE);
  });

  it("expire(true) (401 de l'API) redirige même depuis une page publique", () => {
    const { service, router, navigate } = setup();
    vi.spyOn(router, 'url', 'get').mockReturnValue('/evenements/42');
    service.startSession(memberToken());
    service.expire(true);
    expect(navigate).toHaveBeenCalledWith(['/connexion'], {
      queryParams: { returnUrl: '/evenements/42', raison: EXPIRED_REASON },
    });
  });

  it('reporte une déconnexion faite dans un autre onglet (événement storage)', () => {
    const { service, router, navigate } = setup();
    vi.spyOn(router, 'url', 'get').mockReturnValue('/admin/evenements');
    service.startSession(adminToken());

    localStorage.removeItem(SESSION_STORAGE_KEY);
    window.dispatchEvent(new StorageEvent('storage', { key: SESSION_STORAGE_KEY }));

    expect(service.isLoggedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/connexion'], { queryParams: { returnUrl: '/admin/evenements' } });
  });
});

describe('isProtectedUrl', () => {
  it.each([
    ['/mon-compte', true],
    ['/admin', true],
    ['/admin/evenements/1', true],
    ['/mon-compte?x=1', true],
    ['/evenements', false],
    ['/administrateurs', false],
  ])('%s → %s', (url, expected) => expect(isProtectedUrl(url)).toBe(expected));
});
