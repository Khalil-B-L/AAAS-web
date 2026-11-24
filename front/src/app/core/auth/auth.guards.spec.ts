import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Route,
  Router,
  RouterStateSnapshot,
  UrlSegment,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { adminToken, memberToken } from '../../../testing/fake-jwt';
import { adminGuard, authGuard, guestGuard } from './auth.guards';
import { AuthService } from './auth.service';

describe('guards', () => {
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  afterEach(() => localStorage.clear());

  // Un guard fonctionnel appelle inject() : il doit s'exécuter dans un contexte d'injection.
  const runAuth = (url: string) =>
    TestBed.runInInjectionContext(() => authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot));
  const runAdmin = (path: string) =>
    TestBed.runInInjectionContext(() =>
      // 3e argument (v22) : instantané partiel de la route, inutilisé par notre guard.
      adminGuard({} as Route, [new UrlSegment(path, {})], {} as Parameters<typeof adminGuard>[2]),
    );
  const runGuest = () =>
    TestBed.runInInjectionContext(() => guestGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));
  const asUrl = (result: unknown) => router.serializeUrl(result as UrlTree);

  describe('authGuard', () => {
    it('laisse passer un membre connecté', () => {
      auth.startSession(memberToken());
      expect(runAuth('/mon-compte')).toBe(true);
    });

    it('renvoie un visiteur vers la connexion avec le retour', () => {
      expect(asUrl(runAuth('/mon-compte'))).toBe('/connexion?returnUrl=%2Fmon-compte');
    });
  });

  describe('adminGuard (canMatch)', () => {
    it('laisse passer un admin', () => {
      auth.startSession(adminToken());
      expect(runAdmin('admin')).toBe(true);
    });

    it('envoie un membre vers « Accès réservé »', () => {
      auth.startSession(memberToken());
      expect(asUrl(runAdmin('admin'))).toBe('/acces-refuse');
    });

    it('envoie un visiteur vers la connexion', () => {
      expect(asUrl(runAdmin('admin'))).toBe('/connexion?returnUrl=%2Fadmin');
    });
  });

  describe('guestGuard', () => {
    it('laisse un visiteur ouvrir la connexion', () => {
      expect(runGuest()).toBe(true);
    });

    it('renvoie un membre déjà connecté vers les événements', () => {
      auth.startSession(memberToken());
      expect(asUrl(runGuest())).toBe('/evenements');
    });
  });
});
