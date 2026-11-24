import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { memberToken } from '../../../testing/fake-jwt';
import { SettingsApi } from '../api/settings.api';
import { AuthService } from '../auth/auth.service';
import { ContactSettingsDto } from '../models/api-models';
import { ContactStore } from './contact.store';

function contact(phoneNumber: string): ContactSettingsDto {
  return { phoneNumber, whatsApp: false, isConfigured: true, version: 'AAAAAAAAB9E=', updatedAt: null };
}

/** Le numéro est lu une fois par session, jamais pour un visiteur, et relu quand le compte change (US-D4, R12). */
describe('ContactStore', () => {
  let settings: { getContact: ReturnType<typeof vi.fn> };
  let auth: AuthService;
  let store: ContactStore;

  async function settle(): Promise<void> {
    TestBed.tick();
    await Promise.resolve();
    await Promise.resolve();
    TestBed.tick();
  }

  beforeEach(() => {
    localStorage.clear();
    settings = { getContact: vi.fn(() => of(contact('+21620123456'))) };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: SettingsApi, useValue: settings }],
    });
    auth = TestBed.inject(AuthService);
    store = TestBed.inject(ContactStore);
  });

  afterEach(() => localStorage.clear());

  it('ne demande rien tant qu’aucun écran n’en a besoin', async () => {
    auth.startSession(memberToken());
    await settle();

    expect(settings.getContact).not.toHaveBeenCalled();
    expect(store.contact()).toBeNull();
  });

  it('visiteur : aucun appel (il recevrait 401), puis chargement à la connexion', async () => {
    store.ensureLoaded();
    await settle();
    expect(settings.getContact).not.toHaveBeenCalled();
    expect(store.contact()).toBeNull();

    auth.startSession(memberToken());
    await settle();

    expect(settings.getContact).toHaveBeenCalledTimes(1);
    expect(store.contact()?.phoneNumber).toBe('+21620123456');
  });

  it('plusieurs appels à ensureLoaded : une seule requête', async () => {
    auth.startSession(memberToken());
    store.ensureLoaded();
    store.ensureLoaded();
    await settle();
    store.ensureLoaded();
    await settle();

    expect(settings.getContact).toHaveBeenCalledTimes(1);
  });

  it('changement de compte (autre onglet) : le numéro est relu, pas de valeur de l’ancien compte', async () => {
    settings.getContact.mockReturnValueOnce(of(contact('+21620111111'))).mockReturnValueOnce(of(contact('+21620222222')));
    auth.startSession(memberToken({ sub: 'u-1' }));
    store.ensureLoaded();
    await settle();
    expect(store.contact()?.phoneNumber).toBe('+21620111111');

    auth.startSession(memberToken({ sub: 'u-2' }));
    await settle();

    expect(settings.getContact).toHaveBeenCalledTimes(2);
    expect(store.contact()?.phoneNumber).toBe('+21620222222');
  });

  it('déconnexion : la ressource est au repos, le numéro n’est plus exposé et aucun appel n’est fait', async () => {
    auth.startSession(memberToken());
    store.ensureLoaded();
    await settle();
    expect(store.contact()).not.toBeNull();

    auth.expire();
    await settle();

    expect(auth.isLoggedIn()).toBe(false);
    expect(store.contact()).toBeNull();
    expect(settings.getContact).toHaveBeenCalledTimes(1);
  });

  it('erreur de chargement : hasError, puis reload() relit et efface l’erreur', async () => {
    settings.getContact
      .mockReturnValueOnce(throwError(() => new Error('réseau')))
      .mockReturnValueOnce(of(contact('+21620123456')));
    auth.startSession(memberToken());
    store.ensureLoaded();
    await settle();
    expect(store.hasError()).toBe(true);
    expect(store.contact()).toBeNull();

    store.reload();
    await settle();

    expect(store.hasError()).toBe(false);
    expect(store.contact()?.phoneNumber).toBe('+21620123456');
    expect(settings.getContact).toHaveBeenCalledTimes(2);
  });
});
