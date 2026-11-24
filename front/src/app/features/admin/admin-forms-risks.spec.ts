import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { buttonByText, problem, query, submit, type } from '../../../testing/dom';
import { AdminEventsApi } from '../../core/api/admin-events.api';
import { SettingsApi } from '../../core/api/settings.api';
import { NotificationService } from '../../core/notifications/notification.service';
import { AdminEventDto, ContactSettingsDto } from '../../core/models/api-models';
import { AdminEventForm } from './events/admin-event-form/admin-event-form';
import { ContactSettings } from './settings/contact-settings/contact-settings';

// GUID : le formulaire n'appelle pas l'API pour un identifiant mal formé (comme /evenements/:id).
const EVENT_ID = '0b6f7c1e-3a52-4d8e-9f10-2c4b5a6d7e81';

function adminEvent(overrides: Partial<AdminEventDto> = {}): AdminEventDto {
  return {
    id: EVENT_ID,
    title: 'Atelier couture',
    description: 'Initiation.',
    location: 'Tunis',
    startsAt: '2026-12-05T09:00:00+00:00',
    endsAt: '2026-12-05T11:00:00+00:00',
    status: 'Draft',
    capacity: 30,
    price: 12.5,
    pendingCount: 0,
    confirmedCount: 0,
    remainingSeats: 30,
    hasStarted: false,
    hasEnded: false,
    canBeDeleted: true,
    publishedAt: null,
    firstPublishedAt: null,
    createdAt: '2026-11-20T10:00:00+00:00',
    updatedAt: '2026-11-20T10:00:00+00:00',
    version: 'v1',
    ...overrides,
  };
}

const overlay = () => document.querySelector('.cdk-overlay-container');
// Un bouton dans le dialogue, pas seulement le conteneur : en zoneless, le contenu est rendu au cycle suivant.
const dialogOpen = () => !!overlay()?.querySelector('mat-dialog-container button');

describe('AdminEventForm : risques (dialogue réel, double clic, garde de sortie)', () => {
  let api: Record<'get' | 'create' | 'update' | 'publish' | 'unpublish' | 'delete', ReturnType<typeof vi.fn>>;

  beforeEach(() => {
    api = { get: vi.fn(), create: vi.fn(), update: vi.fn(), publish: vi.fn(), unpublish: vi.fn(), delete: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AdminEventsApi, useValue: api },
        // Dialogue sans animation : on attend une condition (dialogue ouvert, appel reçu), jamais un délai.
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  async function renderExisting(event = adminEvent()) {
    api.get.mockReturnValue(of(event));
    const fixture = TestBed.createComponent(AdminEventForm);
    fixture.componentRef.setInput('id', event.id);
    await fixture.whenStable();
    return fixture;
  }

  function dialogButton(label: string): HTMLButtonElement {
    const found = Array.from(document.querySelectorAll('.cdk-overlay-container button')).find((b) => b.textContent?.trim() === label);
    if (!found) throw new Error(`Bouton du dialogue introuvable : ${label}`);
    return found as HTMLButtonElement;
  }

  it('double clic sur « Enregistrer » : un seul PUT tant que le premier est en cours', async () => {
    const fixture = await renderExisting();
    const pending = new Subject<AdminEventDto>();
    api.update.mockReturnValue(pending);
    type(fixture, 'input[formControlName=title]', 'Modifié');

    submit(fixture);
    submit(fixture);
    await fixture.whenStable();
    expect(api.update).toHaveBeenCalledTimes(1);

    pending.next(adminEvent({ title: 'Modifié', version: 'v2' }));
    pending.complete();
    await fixture.whenStable();

    // Après la réponse, une nouvelle modification peut être enregistrée.
    api.update.mockReturnValue(of(adminEvent({ version: 'v3' })));
    type(fixture, 'input[formControlName=title]', 'Encore');
    submit(fixture);
    await fixture.whenStable();
    expect(api.update).toHaveBeenCalledTimes(2);
  });

  it('double clic sur « Enregistrer » après une erreur : le bouton est de nouveau utilisable', async () => {
    const fixture = await renderExisting();
    api.update.mockReturnValueOnce(throwError(() => problem(500, 'server_error', 'Erreur.')));
    type(fixture, 'input[formControlName=title]', 'Modifié');
    submit(fixture);
    await fixture.whenStable();
    api.update.mockReturnValue(of(adminEvent({ version: 'v2' })));
    submit(fixture);
    await fixture.whenStable();
    expect(api.update).toHaveBeenCalledTimes(2);
  });

  it('double clic sur « Publier » : un seul appel', async () => {
    const fixture = await renderExisting();
    api.publish.mockReturnValue(new Subject());
    buttonByText(fixture, 'Publier').click();
    buttonByText(fixture, 'Publier').click();
    await fixture.whenStable();
    expect(api.publish).toHaveBeenCalledTimes(1);
  });

  it('double soumission de la création : un seul POST', async () => {
    api.create.mockReturnValue(new Subject());
    const fixture = TestBed.createComponent(AdminEventForm);
    await fixture.whenStable();
    type(fixture, 'input[formControlName=title]', 'Atelier');
    type(fixture, 'input[formControlName=location]', 'Tunis');
    type(fixture, 'textarea[formControlName=description]', 'Texte');
    type(fixture, 'input[formControlName=startsAt]', '2026-12-05T10:00');
    type(fixture, 'input[formControlName=endsAt]', '2026-12-05T12:00');
    type(fixture, 'input[formControlName=capacity]', '30');
    submit(fixture);
    submit(fixture);
    await fixture.whenStable();
    expect(api.create).toHaveBeenCalledTimes(1);
  });

  it('registrants_warning avec le VRAI dialogue : « Confirmer » renvoie la même requête avec confirmRegistrantsWarning = true', async () => {
    const fixture = await renderExisting(adminEvent({ status: 'Published', confirmedCount: 3 }));
    api.update
      .mockReturnValueOnce(throwError(() => problem(409, 'registrants_warning', '3 personnes sont inscrites…')))
      .mockReturnValueOnce(of(adminEvent({ version: 'v2' })));
    type(fixture, 'input[formControlName=price]', '15');
    submit(fixture);
    await fixture.whenStable();
    await vi.waitFor(() => expect(dialogOpen()).toBe(true));

    expect(overlay()!.textContent).toContain('Des personnes sont inscrites');
    expect(overlay()!.textContent).toContain('3 personnes sont inscrites…');
    expect(api.update).toHaveBeenCalledTimes(1);

    dialogButton('Confirmer').click();
    await vi.waitFor(() => expect(api.update).toHaveBeenCalledTimes(2));
    await fixture.whenStable();
    const [first, second] = api.update.mock.calls.map((c) => c[1]);
    expect(first.confirmRegistrantsWarning).toBe(false);
    expect(second).toEqual({ ...first, confirmRegistrantsWarning: true });
  });

  it('registrants_warning avec le VRAI dialogue : « Annuler » n’envoie rien et la saisie reste', async () => {
    const fixture = await renderExisting();
    api.update.mockReturnValue(throwError(() => problem(409, 'registrants_warning', 'x')));
    type(fixture, 'input[formControlName=price]', '15');
    submit(fixture);
    await fixture.whenStable();
    await vi.waitFor(() => expect(dialogOpen()).toBe(true));
    dialogButton('Annuler').click();
    // Rien à attendre « positivement » côté API : on attend la fermeture du dialogue, puis on vérifie l'absence d'appel.
    await vi.waitFor(() => expect(dialogOpen()).toBe(false));
    await fixture.whenStable();
    expect(api.update).toHaveBeenCalledTimes(1);
    expect(query<HTMLInputElement>(fixture, 'input[formControlName=price]').value).toBe('15');
  });

  it('hasUnsavedChanges : faux à l’ouverture, vrai après saisie, faux après enregistrement', async () => {
    const fixture = await renderExisting();
    const form = fixture.componentInstance;
    expect(form.hasUnsavedChanges()).toBe(false);
    type(fixture, 'input[formControlName=title]', 'Modifié');
    expect(form.hasUnsavedChanges()).toBe(true);
    api.update.mockReturnValue(of(adminEvent({ title: 'Modifié', version: 'v2' })));
    submit(fixture);
    await fixture.whenStable();
    expect(form.hasUnsavedChanges()).toBe(false);
  });

  it('hasUnsavedChanges : faux sur un événement terminé (lecture seule)', async () => {
    const fixture = await renderExisting(adminEvent({ hasStarted: true, hasEnded: true }));
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('création : après enregistrement, la garde ne redemande pas confirmation', async () => {
    api.create.mockReturnValue(of(adminEvent({ id: '99' })));
    const fixture = TestBed.createComponent(AdminEventForm);
    await fixture.whenStable();
    type(fixture, 'input[formControlName=title]', 'Atelier');
    type(fixture, 'input[formControlName=location]', 'Tunis');
    type(fixture, 'textarea[formControlName=description]', 'Texte');
    type(fixture, 'input[formControlName=startsAt]', '2026-12-05T10:00');
    type(fixture, 'input[formControlName=endsAt]', '2026-12-05T12:00');
    type(fixture, 'input[formControlName=capacity]', '30');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    submit(fixture);
    await fixture.whenStable();
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('enregistrement réussi : notification « Enregistré. »', async () => {
    const fixture = await renderExisting();
    api.update.mockReturnValue(of(adminEvent({ version: 'v2' })));
    type(fixture, 'input[formControlName=title]', 'Modifié');
    submit(fixture);
    await fixture.whenStable();
    expect(TestBed.inject(NotificationService).current()?.message).toBe('Enregistré.');
  });
});

describe('ContactSettings : risques (garde de sortie, double clic)', () => {
  const configured: ContactSettingsDto = {
    phoneNumber: '+21620123456',
    whatsApp: true,
    isConfigured: true,
    version: 'c1',
    updatedAt: '2026-11-20T10:00:00+00:00',
  };
  let api: Record<'getContact' | 'saveContact' | 'removeContact', ReturnType<typeof vi.fn>>;

  beforeEach(() => {
    api = { getContact: vi.fn(() => of(configured)), saveContact: vi.fn(), removeContact: vi.fn() };
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: SettingsApi, useValue: api }] });
  });

  it('hasUnsavedChanges : faux à l’ouverture, vrai après saisie', async () => {
    const fixture = TestBed.createComponent(ContactSettings);
    await fixture.whenStable();
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    type(fixture, 'input[formControlName=phoneNumber]', '+216 71 123 456');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('double clic sur « Enregistrer » : un seul appel', async () => {
    api.saveContact.mockReturnValue(new Subject());
    const fixture = TestBed.createComponent(ContactSettings);
    await fixture.whenStable();
    type(fixture, 'input[formControlName=phoneNumber]', '+216 71 123 456');
    submit(fixture);
    submit(fixture);
    await fixture.whenStable();
    expect(api.saveContact).toHaveBeenCalledTimes(1);
  });
});
