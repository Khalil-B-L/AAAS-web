import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { problem, query, submit, text, type } from '../../../testing/dom';
import { memberToken } from '../../../testing/fake-jwt';
import { AuthApi } from '../../core/api/auth.api';
import { AuthService } from '../../core/auth/auth.service';
import { AuthResponse } from '../../core/models/api-models';
import { Login } from './login/login';
import { Register } from './register/register';

const PHONE_MESSAGE =
  'Le numéro de téléphone doit être un numéro tunisien au format +216 suivi de 8 chiffres, par exemple +216 20 123 456.';

function authResponse(): AuthResponse {
  return { accessToken: memberToken(), expiresAt: '2026-11-23T11:00:00+00:00', user: {} as AuthResponse['user'] };
}

describe('formulaires de compte', () => {
  let authApi: { login: ReturnType<typeof vi.fn>; register: ReturnType<typeof vi.fn> };
  let navigateByUrl: ReturnType<typeof vi.spyOn>;
  let auth: AuthService;

  beforeEach(() => {
    localStorage.clear();
    authApi = { login: vi.fn(), register: vi.fn() };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthApi, useValue: authApi }],
    });
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => localStorage.clear());

  describe('Login (US-A2)', () => {
    async function render(inputs: Record<string, string> = {}) {
      const fixture = TestBed.createComponent(Login);
      Object.entries(inputs).forEach(([k, v]) => fixture.componentRef.setInput(k, v));
      await fixture.whenStable();
      return fixture;
    }

    it("n'appelle pas l'API si un champ est vide et affiche les messages", async () => {
      const fixture = await render();
      submit(fixture);
      await fixture.whenStable();
      expect(authApi.login).not.toHaveBeenCalled();
      expect(text(fixture)).toContain("L'e-mail est obligatoire.");
      expect(text(fixture)).toContain('Le mot de passe est obligatoire.');
    });

    it('ouvre la session et revient à returnUrl', async () => {
      authApi.login.mockReturnValue(of(authResponse()));
      const fixture = await render({ returnUrl: '/mon-compte' });
      type(fixture, 'input[formControlName=email]', 'salma@exemple.tn');
      type(fixture, 'input[formControlName=password]', 'motdepasse1');
      submit(fixture);
      await fixture.whenStable();
      expect(authApi.login).toHaveBeenCalledWith({ email: 'salma@exemple.tn', password: 'motdepasse1' });
      expect(auth.isLoggedIn()).toBe(true);
      expect(navigateByUrl).toHaveBeenCalledWith('/mon-compte');
    });

    it('refuse un returnUrl externe (redirection ouverte)', async () => {
      authApi.login.mockReturnValue(of(authResponse()));
      const fixture = await render({ returnUrl: '//exemple.com' });
      type(fixture, 'input[formControlName=email]', 'a@b.tn');
      type(fixture, 'input[formControlName=password]', 'x');
      submit(fixture);
      await fixture.whenStable();
      expect(navigateByUrl).toHaveBeenCalledWith('/evenements');
    });

    it('affiche le message de l’API pour invalid_credentials et vide le mot de passe', async () => {
      authApi.login.mockReturnValue(throwError(() => problem(401, 'invalid_credentials', 'E-mail ou mot de passe incorrect.')));
      const fixture = await render();
      type(fixture, 'input[formControlName=email]', 'a@b.tn');
      type(fixture, 'input[formControlName=password]', 'mauvais1');
      submit(fixture);
      await fixture.whenStable();
      expect(query(fixture, '[role=alert]').textContent).toContain('E-mail ou mot de passe incorrect.');
      expect(query<HTMLInputElement>(fixture, 'input[formControlName=password]').value).toBe('');
      expect(auth.isLoggedIn()).toBe(false);
      // Bouton réactivé après l'erreur (signal submitting remis à false, rendu en zoneless).
      expect(query<HTMLButtonElement>(fixture, 'button[type=submit]').disabled).toBe(false);
    });

    it('affiche « session expirée » quand raison=expiree', async () => {
      const fixture = await render({ raison: 'expiree' });
      expect(text(fixture)).toContain('Votre session a expiré. Veuillez vous reconnecter.');
    });
  });

  describe('Register (US-A1)', () => {
    async function renderFilled() {
      const fixture = TestBed.createComponent(Register);
      await fixture.whenStable();
      type(fixture, 'input[formControlName=firstName]', 'Salma');
      type(fixture, 'input[formControlName=lastName]', 'Ben Ali');
      type(fixture, 'input[formControlName=email]', 'salma@exemple.tn');
      type(fixture, 'input[formControlName=phoneNumber]', '+216 20 123 456');
      type(fixture, 'input[formControlName=password]', 'motdepasse1');
      query<HTMLInputElement>(fixture, 'input[type=checkbox]').click();
      await fixture.whenStable();
      return fixture;
    }

    it('préremplit le préfixe +216', async () => {
      const fixture = TestBed.createComponent(Register);
      await fixture.whenStable();
      expect(query<HTMLInputElement>(fixture, 'input[formControlName=phoneNumber]').value).toBe('+216 ');
    });

    it('bloque côté front un téléphone local et la case non cochée, avec les messages de l’API', async () => {
      const fixture = TestBed.createComponent(Register);
      await fixture.whenStable();
      type(fixture, 'input[formControlName=phoneNumber]', '20 123 456');
      submit(fixture);
      await fixture.whenStable();
      expect(authApi.register).not.toHaveBeenCalled();
      expect(text(fixture)).toContain(PHONE_MESSAGE);
      expect(text(fixture)).toContain('Vous devez accepter la politique de confidentialité pour créer un compte.');
    });

    it('crée le compte puis connecte automatiquement', async () => {
      authApi.register.mockReturnValue(of(authResponse()));
      const fixture = await renderFilled();
      submit(fixture);
      await fixture.whenStable();
      expect(authApi.register).toHaveBeenCalledWith({
        firstName: 'Salma',
        lastName: 'Ben Ali',
        email: 'salma@exemple.tn',
        phoneNumber: '+216 20 123 456',
        password: 'motdepasse1',
        acceptPrivacyPolicy: true,
      });
      expect(auth.isLoggedIn()).toBe(true);
      expect(navigateByUrl).toHaveBeenCalledWith('/evenements');
    });

    it('recopie une erreur 400 du serveur sous le champ concerné', async () => {
      authApi.register.mockReturnValue(
        throwError(() =>
          problem(400, 'validation_failed', 'Certains champs sont invalides.', {
            password: ['Le mot de passe doit contenir au moins un chiffre.'],
          }),
        ),
      );
      const fixture = await renderFilled();
      submit(fixture);
      await fixture.whenStable();
      expect(query(fixture, 'mat-error').textContent).toContain('Le mot de passe doit contenir au moins un chiffre.');
    });

    it('place email_taken (409) sous le champ e-mail', async () => {
      authApi.register.mockReturnValue(throwError(() => problem(409, 'email_taken', 'Un compte existe déjà avec cet e-mail.')));
      const fixture = await renderFilled();
      submit(fixture);
      await fixture.whenStable();
      expect(text(fixture)).toContain('Un compte existe déjà avec cet e-mail.');
      expect(auth.isLoggedIn()).toBe(false);
    });
  });
});
