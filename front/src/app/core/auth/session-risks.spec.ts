import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { memberToken } from '../../../testing/fake-jwt';
import { NotificationService } from '../notifications/notification.service';
import { authInterceptor } from './auth.interceptor';
import { AuthService, EXPIRED_REASON } from './auth.service';
import { SESSION_STORAGE_KEY } from './session-storage';

// Scénarios « de bout en bout » du front : vrai AuthService + vrai intercepteur, seul le réseau est simulé.
describe('session : expiration du jeton et 401 (sans doublure d’AuthService)', () => {
  const NOW = new Date('2026-11-23T10:00:00Z').getTime();
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    vi.spyOn(TestBed.inject(Router), 'url', 'get').mockReturnValue('/mon-compte');
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  it('un 401 sur une requête authentifiée déconnecte pour de bon et renvoie vers la connexion', () => {
    auth.startSession(memberToken());
    http.get('/api/account/me').subscribe({ error: () => undefined });
    backend.expectOne('/api/account/me').flush({ code: 'unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isLoggedIn()).toBe(false);
    expect(localStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/connexion'], { queryParams: { returnUrl: '/mon-compte', raison: EXPIRED_REASON } });
  });

  it('après le 401, les requêtes suivantes partent sans jeton périmé', () => {
    auth.startSession(memberToken());
    http.get('/api/account/me').subscribe({ error: () => undefined });
    backend.expectOne('/api/account/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    http.get('/api/events').subscribe();
    expect(backend.expectOne('/api/events').request.headers.has('Authorization')).toBe(false);
  });

  it('plusieurs 401 simultanés : une seule redirection', () => {
    auth.startSession(memberToken());
    http.get('/api/a').subscribe({ error: () => undefined });
    http.get('/api/b').subscribe({ error: () => undefined });
    backend.expectOne('/api/a').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/b').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it('jeton échu côté poste : aucune requête authentifiée n’est envoyée avec lui et la session se ferme', () => {
    auth.startSession(memberToken());
    // On saute l'échéance SANS laisser la minuterie tourner (ordinateur en veille, par exemple).
    vi.setSystemTime(NOW + 3600_000);
    http.get('/api/account/me').subscribe({ error: () => undefined });
    const request = backend.expectOne('/api/account/me').request;
    expect(request.headers.has('Authorization')).toBe(false);
    expect(auth.isLoggedIn()).toBe(false);
  });

  it('session valable jusqu’à la dernière minute puis fermée à l’échéance (durée − 60 s)', () => {
    auth.startSession(memberToken());
    vi.advanceTimersByTime(3600_000 - 60_001);
    expect(auth.isLoggedIn()).toBe(true);
    vi.advanceTimersByTime(2);
    expect(auth.isLoggedIn()).toBe(false);
  });

  it('horloge du poste en avance de 2 h : la session fraîchement ouverte reste utilisable', () => {
    vi.setSystemTime(NOW + 2 * 3600_000);
    auth.startSession(memberToken()); // exp / iat du serveur sont en 2027
    expect(auth.isLoggedIn()).toBe(true);
    expect(auth.accessToken()).not.toBeNull();
  });

  it('expiration sur page publique : message d’avertissement, aucun 401 nécessaire', () => {
    vi.spyOn(TestBed.inject(Router), 'url', 'get').mockReturnValue('/evenements');
    auth.startSession(memberToken());
    vi.advanceTimersByTime(3600_000 - 60_000); // le message dure ensuite 6 s
    expect(navigate).not.toHaveBeenCalled();
    expect(TestBed.inject(NotificationService).current()?.kind).toBe('warning');
  });

  it('un jeton illisible est refusé sans lever d’exception (repli sur 60 minutes, session sans identité refusée)', () => {
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ accessToken: 'pas-un-jwt', localDeadline: NOW + 1000_000 }));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    expect(TestBed.inject(AuthService).isLoggedIn()).toBe(false);
  });
});
