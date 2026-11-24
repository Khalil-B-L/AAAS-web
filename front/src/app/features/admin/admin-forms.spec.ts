import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { buttonByText, problem, query, submit, text, type } from '../../../testing/dom';
import { AdminEventsApi } from '../../core/api/admin-events.api';
import { SettingsApi } from '../../core/api/settings.api';
import { NotificationService } from '../../core/notifications/notification.service';
import { AdminEventDto, ContactSettingsDto } from '../../core/models/api-models';
import { ConfirmDialogService } from '../../shared/ui/confirm-dialog';
import { AdminEventForm } from './events/admin-event-form/admin-event-form';
import { toEventFormValue, toEventInput } from './events/admin-event-form/event-form-mapping';
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

describe('mapping formulaire ↔ API (événement)', () => {
  it('convertit la fiche UTC en valeurs de saisie à l’heure de Tunis, prix avec virgule', () => {
    expect(toEventFormValue(adminEvent())).toMatchObject({
      startsAt: '2026-12-05T10:00',
      endsAt: '2026-12-05T12:00',
      capacity: '30',
      price: '12,5',
    });
  });

  it('envoie des dates avec +01:00 et des nombres JSON ; prix vide = null', () => {
    const input = toEventInput({ ...toEventFormValue(adminEvent()), price: '' });
    expect(input).toMatchObject({ startsAt: '2026-12-05T10:00+01:00', endsAt: '2026-12-05T12:00+01:00', capacity: 30, price: null });
  });
});

describe('AdminEventForm (US-E2)', () => {
  let api: Record<'get' | 'create' | 'update' | 'publish' | 'unpublish' | 'delete', ReturnType<typeof vi.fn>>;
  let confirm: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    api = { get: vi.fn(), create: vi.fn(), update: vi.fn(), publish: vi.fn(), unpublish: vi.fn(), delete: vi.fn() };
    confirm = vi.fn(() => of(true));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AdminEventsApi, useValue: api },
        { provide: ConfirmDialogService, useValue: { confirm } },
      ],
    });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  async function renderExisting(event = adminEvent()) {
    api.get.mockReturnValue(of(event));
    const fixture = TestBed.createComponent(AdminEventForm);
    fixture.componentRef.setInput('id', event.id);
    await fixture.whenStable();
    return fixture;
  }

  it('préremplit le formulaire à l’heure de Tunis', async () => {
    const fixture = await renderExisting();
    expect(query<HTMLInputElement>(fixture, 'input[formControlName=startsAt]').value).toBe('2026-12-05T10:00');
    expect(query<HTMLInputElement>(fixture, 'input[formControlName=price]').value).toBe('12,5');
  });

  it('affiche « Contenu introuvable » sur un 404', async () => {
    api.get.mockReturnValue(throwError(() => problem(404, 'not_found', 'Événement introuvable.')));
    const fixture = TestBed.createComponent(AdminEventForm);
    fixture.componentRef.setInput('id', '7d3e2f10-0000-4000-8000-000000000404');
    await fixture.whenStable();
    expect(text(fixture)).toContain('Contenu introuvable');
  });

  it('identifiant qui n’est pas un GUID : « Contenu introuvable » sans appel à l’API', async () => {
    const fixture = TestBed.createComponent(AdminEventForm);
    fixture.componentRef.setInput('id', 'abc');
    await fixture.whenStable();
    expect(api.get).not.toHaveBeenCalled();
    expect(text(fixture)).toContain('Contenu introuvable');
  });

  it('course entre deux fiches : la réponse tardive de la fiche A n’écrase pas la fiche B', async () => {
    const idB = '1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f';
    const slowA = new Subject<AdminEventDto>();
    api.get.mockImplementation((id: string) => (id === EVENT_ID ? slowA : of(adminEvent({ id: idB, title: 'Fiche B' }))));
    const fixture = TestBed.createComponent(AdminEventForm);
    fixture.componentRef.setInput('id', EVENT_ID);
    await fixture.whenStable();
    fixture.componentRef.setInput('id', idB);
    await fixture.whenStable();

    // switchMap s'est désabonné de A : HttpClient aurait annulé la requête.
    expect(slowA.observed).toBe(false);
    slowA.next(adminEvent({ title: 'Fiche A (réponse tardive)' }));
    await fixture.whenStable();
    expect(query<HTMLInputElement>(fixture, 'input[formControlName=title]').value).toBe('Fiche B');
  });

  it('composant détruit pendant le chargement : la requête est abandonnée', async () => {
    const pending = new Subject<AdminEventDto>();
    api.get.mockReturnValue(pending);
    const fixture = TestBed.createComponent(AdminEventForm);
    fixture.componentRef.setInput('id', EVENT_ID);
    await fixture.whenStable();
    expect(pending.observed).toBe(true);
    fixture.destroy();
    expect(pending.observed).toBe(false);
  });

  it('« Réessayer » après une erreur réseau recharge la fiche', async () => {
    api.get.mockReturnValueOnce(throwError(() => problem(500, 'server_error', 'Le serveur ne répond pas.')));
    const fixture = TestBed.createComponent(AdminEventForm);
    fixture.componentRef.setInput('id', EVENT_ID);
    await fixture.whenStable();
    expect(text(fixture)).toContain('Le serveur ne répond pas.');

    api.get.mockReturnValue(of(adminEvent()));
    buttonByText(fixture, 'Réessayer').click();
    await fixture.whenStable();
    expect(api.get).toHaveBeenCalledTimes(2);
    expect(query<HTMLInputElement>(fixture, 'input[formControlName=title]').value).toBe('Atelier couture');
  });

  it('refuse côté front une fin avant le début', async () => {
    const fixture = await renderExisting();
    type(fixture, 'input[formControlName=endsAt]', '2026-12-05T09:00');
    submit(fixture);
    await fixture.whenStable();
    expect(api.update).not.toHaveBeenCalled();
    expect(text(fixture)).toContain('La fin doit être après le début.');
  });

  it('enregistre avec la version, puis remplace la version par celle de la réponse', async () => {
    const fixture = await renderExisting();
    api.update.mockReturnValue(of(adminEvent({ title: 'Nouveau', version: 'v2' })));
    type(fixture, 'input[formControlName=title]', 'Nouveau');
    submit(fixture);
    await fixture.whenStable();
    expect(api.update).toHaveBeenCalledWith(EVENT_ID, expect.objectContaining({ title: 'Nouveau', version: 'v1', confirmRegistrantsWarning: false, startsAt: '2026-12-05T10:00+01:00' }));

    api.update.mockReturnValue(of(adminEvent({ version: 'v3' })));
    type(fixture, 'input[formControlName=title]', 'Encore');
    submit(fixture);
    await fixture.whenStable();
    expect(api.update).toHaveBeenLastCalledWith(EVENT_ID, expect.objectContaining({ version: 'v2' }));
  });

  it('registrants_warning : confirmation, puis MÊME requête avec confirmRegistrantsWarning = true', async () => {
    const fixture = await renderExisting(adminEvent({ status: 'Published', confirmedCount: 3 }));
    api.update
      .mockReturnValueOnce(throwError(() => problem(409, 'registrants_warning', '3 personnes sont inscrites…')))
      .mockReturnValueOnce(of(adminEvent({ version: 'v2' })));
    type(fixture, 'input[formControlName=price]', '15');
    submit(fixture);
    await fixture.whenStable();

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Des personnes sont inscrites', message: '3 personnes sont inscrites…' }));
    expect(api.update).toHaveBeenCalledTimes(2);
    const [first, second] = api.update.mock.calls.map((call) => call[1]);
    expect(first.confirmRegistrantsWarning).toBe(false);
    expect(second).toEqual({ ...first, confirmRegistrantsWarning: true });
  });

  it("registrants_warning annulé : rien n'est renvoyé", async () => {
    confirm.mockReturnValue(of(false));
    const fixture = await renderExisting();
    api.update.mockReturnValue(throwError(() => problem(409, 'registrants_warning', 'x')));
    type(fixture, 'input[formControlName=price]', '15');
    submit(fixture);
    await fixture.whenStable();
    expect(api.update).toHaveBeenCalledTimes(1);
  });

  it('concurrent_update : « Recharger » relit la fiche', async () => {
    const fixture = await renderExisting();
    api.update.mockReturnValue(throwError(() => problem(409, 'concurrent_update', 'Cette fiche a été modifiée…')));
    api.get.mockReturnValue(of(adminEvent({ title: 'Version de l’autre admin', version: 'v9' })));
    type(fixture, 'input[formControlName=title]', 'Ma version');
    submit(fixture);
    await fixture.whenStable();
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ confirmLabel: 'Recharger', cancelLabel: 'Fermer' }));
    expect(api.get).toHaveBeenCalledTimes(2);
    expect(query<HTMLInputElement>(fixture, 'input[formControlName=title]').value).toBe('Version de l’autre admin');
  });

  it('publication : version remplacée, avertissement contact_not_configured affiché', async () => {
    const fixture = await renderExisting();
    api.publish.mockReturnValue(
      of({
        event: adminEvent({ status: 'Published', version: 'v2', canBeDeleted: false }),
        warnings: [{ code: 'contact_not_configured', message: 'Aucun numéro de contact n’est configuré…' }],
      }),
    );
    buttonByText(fixture, 'Publier').click();
    await fixture.whenStable();
    expect(text(fixture)).toContain('Aucun numéro de contact n’est configuré…');
    expect(text(fixture)).toContain('Configurer le numéro de contact');

    api.update.mockReturnValue(of(adminEvent({ status: 'Published', version: 'v3' })));
    type(fixture, 'input[formControlName=title]', 'Après publication');
    submit(fixture);
    await fixture.whenStable();
    // Sans le remplacement de la version, ce PUT partirait avec v1 et recevrait un faux concurrent_update.
    expect(api.update).toHaveBeenCalledWith(EVENT_ID, expect.objectContaining({ version: 'v2' }));
  });

  it('désactive Publier tant que le formulaire contient des modifications (UX-7)', async () => {
    const fixture = await renderExisting();
    expect(buttonByText(fixture, 'Publier').disabled).toBe(false);
    type(fixture, 'input[formControlName=title]', 'Modifié');
    await fixture.whenStable();
    expect(buttonByText(fixture, 'Publier').disabled).toBe(true);
    expect(text(fixture)).toContain("Enregistrez d'abord vos modifications.");
  });

  it('état changé côté serveur (event_ended) : message de l’API et rechargement', async () => {
    const fixture = await renderExisting();
    api.update.mockReturnValue(throwError(() => problem(409, 'event_ended', 'Cet événement est terminé : il ne peut plus être modifié.')));
    api.get.mockReturnValue(of(adminEvent({ hasStarted: true, hasEnded: true, version: 'v5' })));
    type(fixture, 'input[formControlName=title]', 'x');
    submit(fixture);
    await fixture.whenStable();
    expect(text(fixture)).toContain('Cet événement est terminé : il ne peut plus être modifié.');
    expect(query<HTMLInputElement>(fixture, 'input[formControlName=title]').disabled).toBe(true);
  });

  it('crée un brouillon puis ouvre sa fiche', async () => {
    api.create.mockReturnValue(of(adminEvent({ id: '99' })));
    const fixture = TestBed.createComponent(AdminEventForm);
    await fixture.whenStable();
    type(fixture, 'input[formControlName=title]', 'Atelier');
    type(fixture, 'input[formControlName=location]', 'Tunis');
    type(fixture, 'textarea[formControlName=description]', 'Texte');
    type(fixture, 'input[formControlName=startsAt]', '2026-12-05T10:00');
    type(fixture, 'input[formControlName=endsAt]', '2026-12-05T12:00');
    type(fixture, 'input[formControlName=capacity]', '30');
    submit(fixture);
    await fixture.whenStable();
    expect(api.create).toHaveBeenCalledWith({
      title: 'Atelier',
      description: 'Texte',
      location: 'Tunis',
      startsAt: '2026-12-05T10:00+01:00',
      endsAt: '2026-12-05T12:00+01:00',
      capacity: 30,
      price: null,
    });
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/admin/evenements', '99']);
  });

  describe('réponse d’une écriture arrivée après un changement d’écran', () => {
    const ID_B = '1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f';

    it('création : composant détruit pendant le POST → requête maintenue, pas de navigation', async () => {
      const pending = new Subject<AdminEventDto>();
      api.create.mockReturnValue(pending);
      const fixture = TestBed.createComponent(AdminEventForm);
      await fixture.whenStable();
      type(fixture, 'input[formControlName=title]', 'Atelier');
      type(fixture, 'input[formControlName=location]', 'Tunis');
      type(fixture, 'textarea[formControlName=description]', 'Texte');
      type(fixture, 'input[formControlName=startsAt]', '2026-12-05T10:00');
      type(fixture, 'input[formControlName=endsAt]', '2026-12-05T12:00');
      type(fixture, 'input[formControlName=capacity]', '30');
      submit(fixture);
      fixture.destroy();

      // Écriture non annulée : l'abonnement survit à la destruction du composant.
      expect(pending.observed).toBe(true);
      pending.next(adminEvent({ id: '99' }));
      pending.complete();
      expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
      expect(TestBed.inject(NotificationService).current()?.message).toBe('Enregistré.');
    });

    it('suppression : composant détruit pendant le DELETE → requête maintenue, pas de navigation', async () => {
      const fixture = await renderExisting();
      const pending = new Subject<void>();
      api.delete.mockReturnValue(pending);
      buttonByText(fixture, 'Supprimer ce brouillon').click();
      fixture.destroy();

      expect(pending.observed).toBe(true);
      pending.next();
      pending.complete();
      expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
    });

    it('enregistrement : passage à la fiche B pendant le PUT → la réponse de A n’écrase pas B, B reste utilisable', async () => {
      const fixture = await renderExisting();
      const pending = new Subject<AdminEventDto>();
      api.update.mockReturnValue(pending);
      type(fixture, 'input[formControlName=title]', 'Modif de A');
      submit(fixture);

      api.get.mockReturnValue(of(adminEvent({ id: ID_B, title: 'Fiche B', version: 'b1' })));
      fixture.componentRef.setInput('id', ID_B);
      await fixture.whenStable();
      expect(pending.observed).toBe(true);

      pending.next(adminEvent({ title: 'Modif de A', version: 'v2' }));
      pending.complete();
      await fixture.whenStable();
      expect(query<HTMLInputElement>(fixture, 'input[formControlName=title]').value).toBe('Fiche B');
      // busy remis à false : la fiche B n'est pas bloquée par la requête de A.
      expect(buttonByText(fixture, 'Publier').disabled).toBe(false);
    });

    it('erreur après destruction : pas de dialogue, échec signalé par la notification globale', async () => {
      const fixture = await renderExisting();
      const pending = new Subject<AdminEventDto>();
      api.update.mockReturnValue(pending);
      type(fixture, 'input[formControlName=title]', 'x');
      submit(fixture);
      fixture.destroy();

      pending.error(problem(409, 'concurrent_update', 'Cette fiche a été modifiée…'));
      expect(confirm).not.toHaveBeenCalled();
      expect(TestBed.inject(NotificationService).current()).toMatchObject({ message: 'Cette fiche a été modifiée…', kind: 'warning' });
    });
  });
});

describe('ContactSettings (US-E8)', () => {
  let api: Record<'getContact' | 'saveContact' | 'removeContact', ReturnType<typeof vi.fn>>;
  const configured: ContactSettingsDto = {
    phoneNumber: '+21620123456',
    whatsApp: true,
    isConfigured: true,
    version: 'c1',
    updatedAt: '2026-11-20T10:00:00+00:00',
  };

  beforeEach(() => {
    api = { getContact: vi.fn(() => of(configured)), saveContact: vi.fn(), removeContact: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        { provide: SettingsApi, useValue: api },
        { provide: ConfirmDialogService, useValue: { confirm: vi.fn(() => of(true)) } },
      ],
    });
  });

  it('affiche le numéro formaté et les liens tel: / wa.me', async () => {
    const fixture = TestBed.createComponent(ContactSettings);
    await fixture.whenStable();
    expect(query<HTMLAnchorElement>(fixture, 'a[href^="tel:"]').getAttribute('href')).toBe('tel:+21620123456');
    expect(query<HTMLAnchorElement>(fixture, 'a[href^="https://wa.me"]').getAttribute('href')).toBe('https://wa.me/21620123456');
    expect(query<HTMLInputElement>(fixture, 'input[formControlName=phoneNumber]').value).toBe('+216 20 123 456');
  });

  it('retire le numéro avec la version, puis affiche l’état vide', async () => {
    api.removeContact.mockReturnValue(of({ ...configured, phoneNumber: null, whatsApp: false, isConfigured: false, version: 'c2' }));
    const fixture = TestBed.createComponent(ContactSettings);
    await fixture.whenStable();
    buttonByText(fixture, 'Retirer le numéro').click();
    await fixture.whenStable();
    expect(api.removeContact).toHaveBeenCalledWith('c1');
    expect(text(fixture)).toContain("Aucun numéro n'est défini pour le moment.");
  });

  it('enregistre un numéro fixe avec la version courante', async () => {
    api.saveContact.mockReturnValue(of({ ...configured, phoneNumber: '+21671123456', version: 'c2' }));
    const fixture = TestBed.createComponent(ContactSettings);
    await fixture.whenStable();
    type(fixture, 'input[formControlName=phoneNumber]', '+216 71 123 456');
    submit(fixture);
    await fixture.whenStable();
    expect(api.saveContact).toHaveBeenCalledWith({ phoneNumber: '+216 71 123 456', whatsApp: true, version: 'c1' });
  });

  it('désactive WhatsApp sans numéro, et refuse un numéro invalide côté front', async () => {
    const fixture = TestBed.createComponent(ContactSettings);
    await fixture.whenStable();
    type(fixture, 'input[formControlName=phoneNumber]', '+216 ');
    await fixture.whenStable();
    expect(query<HTMLInputElement>(fixture, 'input[type=checkbox]').disabled).toBe(true);
    type(fixture, 'input[formControlName=phoneNumber]', '+216 20 12');
    submit(fixture);
    await fixture.whenStable();
    expect(api.saveContact).not.toHaveBeenCalled();
  });
});
