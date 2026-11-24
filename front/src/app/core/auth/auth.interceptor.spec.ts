import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { memberToken } from '../../../testing/fake-jwt';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;
  let expire: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        // Même déclaration que app.config.ts ; provideHttpClientTesting remplace le vrai réseau par un faux backend.
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    // On vérifie l'appel, sans exécuter la vraie navigation (aucune route n'est déclarée ici).
    expire = vi.spyOn(auth, 'expire').mockImplementation(() => undefined);
  });

  afterEach(() => {
    backend.verify(); // aucune requête inattendue ou oubliée
    localStorage.clear();
  });

  it('ajoute le jeton Bearer aux requêtes vers /api/ quand une session existe', () => {
    const token = memberToken();
    auth.startSession(token);
    http.get('/api/account/me').subscribe();
    expect(backend.expectOne('/api/account/me').request.headers.get('Authorization')).toBe(`Bearer ${token}`);
  });

  it("n'envoie jamais le jeton à un autre site", () => {
    auth.startSession(memberToken());
    http.get('https://exemple.com/api/x').subscribe();
    expect(backend.expectOne('https://exemple.com/api/x').request.headers.has('Authorization')).toBe(false);
  });

  it("n'ajoute rien sans session", () => {
    http.get('/api/events').subscribe();
    expect(backend.expectOne('/api/events').request.headers.has('Authorization')).toBe(false);
  });

  it("401 sur une requête qui portait un jeton : expiration et redirection, et l'erreur est relayée", () => {
    auth.startSession(memberToken());
    let failed = false;
    http.get('/api/account/me').subscribe({ error: () => (failed = true) });
    backend.expectOne('/api/account/me').flush({ code: 'unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    expect(expire).toHaveBeenCalledWith(true);
    expect(failed).toBe(true);
  });

  it('401 sans jeton (mauvais mot de passe) : laissé à l’écran, pas de redirection', () => {
    let failed = false;
    http.post('/api/auth/login', {}).subscribe({ error: () => (failed = true) });
    backend
      .expectOne('/api/auth/login')
      .flush({ code: 'invalid_credentials', title: 'E-mail ou mot de passe incorrect.' }, { status: 401, statusText: 'Unauthorized' });
    expect(expire).not.toHaveBeenCalled();
    expect(failed).toBe(true);
  });

  it("n'interprète pas un 403 comme une fin de session", () => {
    auth.startSession(memberToken());
    http.get('/api/admin/events').subscribe({ error: () => undefined });
    backend.expectOne((r) => r.url === '/api/admin/events').flush(null, { status: 403, statusText: 'Forbidden' });
    expect(expire).not.toHaveBeenCalled();
    expect(auth.isLoggedIn()).toBe(true);
  });
});
