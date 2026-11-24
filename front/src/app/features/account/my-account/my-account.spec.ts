import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { buttonByText, problem, query, text } from '../../../../testing/dom';
import { AccountApi } from '../../../core/api/account.api';
import { FORMATTING_PROVIDERS } from '../../../core/config/formatting.providers';
import { AccountDto } from '../../../core/models/api-models';
import { MyAccount } from './my-account';

function account(overrides: Partial<AccountDto> = {}): AccountDto {
  return {
    id: 'u-1',
    firstName: 'Salma',
    lastName: 'Ben Ali',
    email: 'salma@exemple.tn',
    phoneNumber: '+21620123456',
    roles: ['User'],
    createdAt: '2026-11-20T10:00:00+00:00',
    privacyConsentAt: '2026-11-20T10:00:00+00:00',
    privacyPolicyVersion: '1.0',
    lastLoginAt: '2026-11-23T08:30:00+00:00',
    ...overrides,
  };
}

describe('MyAccount (US-F3)', () => {
  let api: { me: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    api = { me: vi.fn() };
    TestBed.configureTestingModule({ providers: [...FORMATTING_PROVIDERS, { provide: AccountApi, useValue: api }] });
  });

  it('affiche un indicateur pendant le chargement', async () => {
    api.me.mockReturnValue(new Subject());
    const fixture = TestBed.createComponent(MyAccount);
    fixture.detectChanges(); // pas de whenStable : la requête ne se termine jamais
    expect(text(fixture)).not.toContain('salma@exemple.tn');
    expect(fixture.nativeElement.querySelector('app-loading')).not.toBeNull();
  });

  it('affiche le profil, le téléphone formaté et la dernière connexion à l’heure de Tunis', async () => {
    api.me.mockReturnValue(of(account()));
    const fixture = TestBed.createComponent(MyAccount);
    await fixture.whenStable();
    const content = text(fixture);
    expect(content).toContain('Salma');
    expect(content).toContain('Ben Ali');
    expect(content).toContain('salma@exemple.tn');
    expect(content).toContain('+216 20 123 456');
    expect(content).toContain('version 1.0');
    expect(content).toContain('Dernière connexion');
    expect(content).toContain('09:30'); // 08:30 UTC = 09:30 à Tunis
  });

  it('masque « Dernière connexion » quand elle est inconnue', async () => {
    api.me.mockReturnValue(of(account({ lastLoginAt: null })));
    const fixture = TestBed.createComponent(MyAccount);
    await fixture.whenStable();
    expect(text(fixture)).not.toContain('Dernière connexion');
  });

  it('affiche le message de l’API en erreur puis recharge avec « Réessayer »', async () => {
    api.me.mockReturnValueOnce(throwError(() => problem(500, 'server_error', 'Une erreur est survenue.')));
    const fixture = TestBed.createComponent(MyAccount);
    await fixture.whenStable();
    expect(query(fixture, '[role=alert]').textContent).toContain('Une erreur est survenue.');

    api.me.mockReturnValue(of(account()));
    buttonByText(fixture, 'Réessayer').click();
    await fixture.whenStable();
    expect(api.me).toHaveBeenCalledTimes(2);
    expect(text(fixture)).toContain('salma@exemple.tn');
    expect(fixture.nativeElement.querySelector('[role=alert]')).toBeNull();
  });
});
