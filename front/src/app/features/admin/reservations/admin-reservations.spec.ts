import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { buttonByText, problem, text, type } from '../../../../testing/dom';
import { AdminReservationsApi } from '../../../core/api/admin-reservations.api';
import { FORMATTING_PROVIDERS } from '../../../core/config/formatting.providers';
import {
  AdminReservationDto,
  EventRegistrantsDto,
  PagedResult,
  ReservationStatus,
} from '../../../core/models/api-models';
import { NotificationService } from '../../../core/notifications/notification.service';
import { ConfirmDialogService } from '../../../shared/ui/confirm-dialog';
import { AdminLayout } from '../admin-layout';
import { AdminReservationActions, PendingCountStore } from './admin-reservation-actions';
import { EventRegistrants, paidByName } from './event-registrants/event-registrants';
import { PendingRequests, toggled } from './pending-requests/pending-requests';
import { RefuseDialog } from './refuse-dialog';

const EVENT_ID = '9ec3d743-0805-444d-de27-08df49668b4f';

function reservation(status: ReservationStatus, overrides: Partial<AdminReservationDto> = {}): AdminReservationDto {
  return {
    id: `r-${status}`,
    status,
    requestedAt: '2026-12-01T09:00:00+00:00',
    expiresAt: status === 'Pending' ? '2026-12-03T09:00:00+00:00' : null,
    decidedAt: null,
    refusalReason: null,
    cancelledAt: null,
    isPaid: false,
    paidAt: null,
    paidBy: null,
    member: { id: 'm-1', firstName: 'Amel', lastName: 'Ben Ali', email: 'amel@exemple.tn', phoneNumber: '+21620123456' },
    event: {
      id: EVENT_ID,
      title: 'Atelier couture',
      startsAt: '2026-12-05T09:00:00+00:00',
      endsAt: '2026-12-05T11:00:00+00:00',
      price: 15,
      status: 'Published',
    },
    actions: {
      canConfirm: status === 'Pending',
      canRefuse: status === 'Pending',
      canCancel: status === 'Pending' || status === 'Confirmed',
      canMarkPaid: status === 'Confirmed',
      canUnmarkPaid: false,
    },
    ...overrides,
  };
}

function page(items: AdminReservationDto[], totalCount = items.length): PagedResult<AdminReservationDto> {
  return { items, page: 1, pageSize: 20, totalCount };
}

function registrants(overrides: Partial<EventRegistrantsDto> = {}): EventRegistrantsDto {
  return {
    event: {
      id: EVENT_ID,
      title: 'Atelier couture',
      startsAt: '2026-12-05T09:00:00+00:00',
      endsAt: '2026-12-05T11:00:00+00:00',
      capacity: 30,
      price: 15,
      status: 'Published',
      hasStarted: false,
      hasEnded: false,
    },
    occupancy: { pending: 3, confirmed: 12, active: 15, remaining: 15 },
    pending: [reservation('Pending')],
    confirmed: [
      reservation('Confirmed', {
        id: 'paid',
        isPaid: true,
        paidAt: '2026-12-02T09:00:00+00:00',
        paidBy: { firstName: 'Admin', lastName: 'Association' },
        actions: { canConfirm: false, canRefuse: false, canCancel: true, canMarkPaid: false, canUnmarkPaid: true },
      }),
      reservation('Confirmed', { id: 'deleted', member: null }),
    ],
    others: [reservation('Refused', { refusalReason: 'Réservé aux adhérents' })],
    ...overrides,
  };
}

describe('Administration des réservations (tâche B)', () => {
  let api: { pending: ReturnType<typeof vi.fn>; registrants: ReturnType<typeof vi.fn>; act: ReturnType<typeof vi.fn> };
  let confirmDialog: { confirm: ReturnType<typeof vi.fn> };
  let matDialog: { open: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    localStorage.clear();
    api = {
      pending: vi.fn(() => of(page([reservation('Pending')], 1))),
      registrants: vi.fn(() => of(registrants())),
      act: vi.fn((id: string) => of(reservation('Confirmed', { id }))),
    };
    confirmDialog = { confirm: vi.fn(() => of(true)) };
    matDialog = { open: vi.fn(() => ({ afterClosed: () => of({ reason: 'Complet' }) })) };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ...FORMATTING_PROVIDERS,
        { provide: AdminReservationsApi, useValue: api },
        { provide: ConfirmDialogService, useValue: confirmDialog },
        { provide: MatDialog, useValue: matDialog },
      ],
    });
  });

  describe('outils', () => {
    it('toggled renvoie un nouvel ensemble (le signal voit le changement)', () => {
      const empty = new Set<string>();
      const withA = toggled(empty, 'a', true);
      expect(withA).not.toBe(empty);
      expect(withA.has('a')).toBe(true);
      expect(toggled(withA, 'a', false).has('a')).toBe(false);
    });

    it('paidByName : nom de l’admin, ou « un administrateur supprimé »', () => {
      expect(paidByName(reservation('Confirmed', { paidBy: { firstName: 'Admin', lastName: 'Association' } }))).toBe(
        'Admin Association',
      );
      expect(paidByName(reservation('Confirmed', { isPaid: true, paidBy: null }))).toBe('un administrateur supprimé');
    });
  });

  describe('PendingRequests (US-E6)', () => {
    async function render() {
      const fixture = TestBed.createComponent(PendingRequests);
      await fixture.whenStable();
      return fixture;
    }

    it('affiche le membre avec liens tel:, wa.me et mailto, l’événement et les boutons', async () => {
      const fixture = await render();
      const element = fixture.nativeElement as HTMLElement;

      expect(text(fixture)).toContain('Demandes à traiter');
      expect(text(fixture)).toContain('Amel Ben Ali');
      expect(element.querySelector('a[href="tel:+21620123456"]')?.textContent).toContain('+216 20 123 456');
      expect(element.querySelector('a[href="https://wa.me/21620123456"]')?.getAttribute('rel')).toContain('noopener');
      expect(element.querySelector('a[href="mailto:amel@exemple.tn"]')).not.toBeNull();
      expect(element.querySelector(`a[href="/admin/evenements/${EVENT_ID}/inscrits"]`)).not.toBeNull();
      expect(buttonByText(fixture, 'Confirmer').getAttribute('aria-label')).toBe('Confirmer — Amel Ben Ali, Atelier couture');
      expect(buttonByText(fixture, 'Refuser').getAttribute('aria-label')).toBe('Refuser — Amel Ben Ali, Atelier couture');
      expect(element.querySelector('a[href="https://wa.me/21620123456"]')?.getAttribute('aria-label')).toBe(
        'WhatsApp — Amel Ben Ali (nouvel onglet)',
      );
      expect(localStorage.length).toBe(0); // aucune coordonnée hors mémoire
    });

    it('page vidée alors qu’il reste des demandes : redirection vers la dernière page, sans entrée d’historique', async () => {
      api.pending.mockImplementation((p: number) =>
        of(p === 3 ? { items: [], page: 3, pageSize: 20, totalCount: 25 } : page([reservation('Pending')], 25)),
      );
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const fixture = TestBed.createComponent(PendingRequests);
      fixture.componentRef.setInput('page', 3);
      await fixture.whenStable();

      expect(navigate).toHaveBeenCalledWith([], { queryParams: { page: 2 }, queryParamsHandling: 'merge', replaceUrl: true });
      expect(text(fixture)).not.toContain('Aucune demande à traiter.');
    });

    it('page 1 vide ou plus aucune demande : pas de redirection', async () => {
      api.pending.mockReturnValue(of({ items: [], page: 2, pageSize: 20, totalCount: 0 }));
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const fixture = TestBed.createComponent(PendingRequests);
      fixture.componentRef.setInput('page', 2);
      await fixture.whenStable();

      expect(navigate).not.toHaveBeenCalled();
      expect(text(fixture)).toContain('Aucune demande à traiter.');
    });

    it('message vide', async () => {
      api.pending.mockReturnValue(of(page([])));
      const fixture = await render();
      expect(text(fixture)).toContain('Aucune demande à traiter.');
    });

    it('compte supprimé : pas de coordonnées', async () => {
      api.pending.mockReturnValue(of(page([reservation('Pending', { member: null })])));
      const fixture = await render();
      expect(text(fixture)).toContain('Compte supprimé');
      expect((fixture.nativeElement as HTMLElement).querySelector('a[href^="tel:"]')).toBeNull();
    });

    it('Confirmer : un seul appel malgré un double clic, boutons désactivés pendant l’appel, puis message et relecture', async () => {
      const response = new Subject<AdminReservationDto>();
      api.act.mockReturnValue(response);
      const fixture = await render();
      const counter = TestBed.inject(PendingCountStore);
      const refresh = vi.spyOn(counter, 'refresh').mockImplementation(() => undefined);

      buttonByText(fixture, 'Confirmer').click();
      buttonByText(fixture, 'Confirmer').click();
      fixture.detectChanges();

      expect(api.act).toHaveBeenCalledTimes(1);
      expect(api.act).toHaveBeenCalledWith('r-Pending', 'confirm', null);
      expect(buttonByText(fixture, 'Confirmer').disabled).toBe(true);
      expect(buttonByText(fixture, 'Refuser').disabled).toBe(true);

      response.next(reservation('Confirmed', { id: 'r-Pending' }));
      response.complete();
      await fixture.whenStable();

      expect(TestBed.inject(NotificationService).current()?.message).toBe('Demande confirmée.');
      expect(api.pending).toHaveBeenCalledTimes(2);
      expect(refresh).toHaveBeenCalled();
    });

    it('409 reservation_state_changed : message de l’API affiché et liste relue', async () => {
      api.act.mockReturnValue(
        throwError(() => problem(409, 'reservation_state_changed', 'Cette demande a déjà été traitée ou annulée.')),
      );
      const fixture = await render();
      api.pending.mockReturnValue(of(page([])));

      buttonByText(fixture, 'Confirmer').click();
      await fixture.whenStable();

      expect(text(fixture)).toContain('Cette demande a déjà été traitée ou annulée.');
      expect(text(fixture)).toContain('Aucune demande à traiter.');
    });

    it('Refuser : le motif du dialogue est envoyé ; fermer le dialogue n’appelle pas l’API', async () => {
      const fixture = await render();

      buttonByText(fixture, 'Refuser').click();
      await fixture.whenStable();
      expect(api.act).toHaveBeenCalledWith('r-Pending', 'refuse', 'Complet');
      expect(TestBed.inject(NotificationService).current()?.message).toBe('Demande refusée.');

      api.act.mockClear();
      matDialog.open.mockReturnValue({ afterClosed: () => of(undefined) });
      buttonByText(fixture, 'Refuser').click();
      await fixture.whenStable();
      expect(api.act).not.toHaveBeenCalled();
    });
  });

  describe('EventRegistrants (US-E4, E5, E7)', () => {
    async function render(id = EVENT_ID) {
      const fixture = TestBed.createComponent(EventRegistrants);
      fixture.componentRef.setInput('id', id);
      await fixture.whenStable();
      return fixture;
    }

    it('titre, total, rappel, trois sections, paiement et compte supprimé', async () => {
      const fixture = await render();
      const content = text(fixture);

      expect(content).toContain('Inscrits — Atelier couture');
      expect(content).toContain('15 / 30 places occupées (12 confirmées, 3 en attente)');
      expect(content).toContain("Ces coordonnées sont réservées à l'organisation de l'événement.");
      expect(content).toContain('Demandes en attente');
      expect(content).toContain('Réservations confirmées');
      expect(content).toContain('Autres (refusées, expirées, annulées)');
      expect(content).toContain('Payée le 2 décembre 2026 par Admin Association');
      expect(content).toContain('Non payée');
      expect(content).toContain('Compte supprimé');
      expect(content).toContain('Refusée');
      expect(content).toContain('Motif : Réservé aux adhérents');
      expect(buttonByText(fixture, 'Retirer la mention payée').getAttribute('aria-label')).toBe(
        'Retirer la mention payée — Amel Ben Ali',
      );
      expect(buttonByText(fixture, 'Marquer comme payée').getAttribute('aria-label')).toBe(
        'Marquer comme payée — compte supprimé',
      );
      // Pas l'événement dans le nom : il est déjà le titre de la page.
      expect(buttonByText(fixture, 'Confirmer').getAttribute('aria-label')).toBe('Confirmer — Amel Ben Ali');
      expect(buttonByText(fixture, 'Annuler la réservation').getAttribute('aria-label')).toBe(
        'Annuler la réservation — Amel Ben Ali',
      );
      const whatsApp = (fixture.nativeElement as HTMLElement).querySelector('a[href^="https://wa.me/"]');
      expect(whatsApp?.getAttribute('aria-label')).toBe('WhatsApp — Amel Ben Ali (nouvel onglet)');
    });

    it('événement gratuit : pas de colonne Paiement ni de « Marquer comme payée »', async () => {
      const free = registrants();
      free.event.price = null;
      free.confirmed = [
        reservation('Confirmed', { actions: { canConfirm: false, canRefuse: false, canCancel: true, canMarkPaid: false, canUnmarkPaid: false } }),
      ];
      api.registrants.mockReturnValue(of(free));
      const fixture = await render();

      expect(text(fixture)).not.toContain('Paiement');
      expect(() => buttonByText(fixture, 'Marquer comme payée')).toThrow();
    });

    it('événement terminé : message, pas de bouton « Annuler la réservation »', async () => {
      const ended = registrants({ pending: [], others: [] });
      ended.event.hasEnded = true;
      ended.confirmed = [
        reservation('Confirmed', { actions: { canConfirm: false, canRefuse: false, canCancel: false, canMarkPaid: true, canUnmarkPaid: false } }),
      ];
      api.registrants.mockReturnValue(of(ended));
      const fixture = await render();

      expect(text(fixture)).toContain('Cet événement est terminé : les réservations ne peuvent plus être annulées.');
      expect(() => buttonByText(fixture, 'Annuler la réservation')).toThrow();
      expect(buttonByText(fixture, 'Marquer comme payée')).toBeTruthy();
    });

    it('aucune réservation : « Aucune demande pour le moment. »', async () => {
      api.registrants.mockReturnValue(of(registrants({ pending: [], confirmed: [], others: [] })));
      const fixture = await render();
      expect(text(fixture)).toContain('Aucune demande pour le moment.');
    });

    it('identifiant mal formé : introuvable sans appel réseau', async () => {
      const fixture = await render('pas-un-guid');
      expect(text(fixture)).toContain('Contenu introuvable');
      expect(api.registrants).not.toHaveBeenCalled();
    });

    it('Annuler la réservation : dialogue (mention payée), puis appel et relecture ; refus du dialogue sans appel', async () => {
      const fixture = await render();

      confirmDialog.confirm.mockReturnValueOnce(of(false));
      buttonByText(fixture, 'Annuler la réservation').click();
      await fixture.whenStable();
      expect(api.act).not.toHaveBeenCalled();

      const paidRow = fixture.nativeElement.querySelectorAll('tbody tr')[1] as HTMLElement;
      Array.from(paidRow.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Annuler la réservation')!.click();
      await fixture.whenStable();

      const data = confirmDialog.confirm.mock.calls.at(-1)![0] as { title: string; message: string };
      expect(data.title).toBe('Annuler cette réservation ?');
      expect(data.message).toContain('pensez à prévenir la personne par téléphone');
      expect(data.message).toContain("le remboursement éventuel se règle hors de l'application");
      expect(api.act).toHaveBeenCalledWith('paid', 'cancel', null);
      expect(api.registrants).toHaveBeenCalledTimes(2);
      expect(TestBed.inject(NotificationService).current()?.message).toBe('Réservation annulée.');
    });

    it('Marquer comme payée → 409 payment_not_applicable : message affiché', async () => {
      api.act.mockReturnValue(
        throwError(() => problem(409, 'payment_not_applicable', 'Cet événement est gratuit : aucun paiement à enregistrer')),
      );
      const fixture = await render();

      buttonByText(fixture, 'Marquer comme payée').click();
      await fixture.whenStable();

      expect(text(fixture)).toContain('Cet événement est gratuit : aucun paiement à enregistrer');
    });
  });

  describe('AdminLayout : compteur « Demandes (N) »', () => {
    it('affiche le total, ou « Demandes » seul à zéro', async () => {
      api.pending.mockReturnValue(of(page([reservation('Pending')], 3)));
      const fixture = TestBed.createComponent(AdminLayout);
      await fixture.whenStable();
      expect(api.pending).toHaveBeenCalledWith(1, 1);
      expect(text(fixture)).toContain('Demandes (3)');

      api.pending.mockReturnValue(of(page([], 0)));
      TestBed.inject(PendingCountStore).refresh();
      await fixture.whenStable();
      expect(text(fixture)).toContain('Demandes');
      expect(text(fixture)).not.toContain('Demandes (');
    });

    it('erreur de lecture : « Demandes » sans nombre', async () => {
      api.pending.mockReturnValue(throwError(() => problem(500, 'internal_error', 'x')));
      const fixture = TestBed.createComponent(AdminLayout);
      await fixture.whenStable();
      expect(text(fixture)).not.toContain('Demandes (');
    });
  });

  describe('AdminReservationActions', () => {
    it('confirmer et marquer payée : pas de dialogue', async () => {
      const actions = TestBed.inject(AdminReservationActions);
      const outcomes: string[] = [];
      actions.run(reservation('Pending'), 'confirm').subscribe((o) => outcomes.push(o.kind));
      actions.run(reservation('Confirmed'), 'mark-paid').subscribe((o) => outcomes.push(o.kind));
      expect(outcomes).toEqual(['done', 'done']);
      expect(confirmDialog.confirm).not.toHaveBeenCalled();
      expect(matDialog.open).not.toHaveBeenCalled();
    });
  });
});

describe('RefuseDialog', () => {
  let close: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    close = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: MatDialogRef, useValue: { close } },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(RefuseDialog);
    await fixture.whenStable();
    return fixture;
  }

  it('libellés, compteur 0 / 500, motif facultatif (vide → null)', async () => {
    const fixture = await render();
    expect(text(fixture)).toContain('Refuser cette demande ?');
    expect(text(fixture)).toContain('Motif (facultatif)');
    expect(text(fixture)).toContain('Le motif sera visible par le membre.');
    expect(text(fixture)).toContain('0 / 500');

    type(fixture, 'textarea', '   ');
    fixture.detectChanges();
    buttonByText(fixture, 'Refuser la demande').click();
    expect(close).toHaveBeenCalledWith({ reason: null });
  });

  it('motif saisi : espaces retirés ; compteur mis à jour', async () => {
    const fixture = await render();
    type(fixture, 'textarea', '  Réservé aux adhérents ');
    await fixture.whenStable();
    expect(text(fixture)).toContain('24 / 500');
    buttonByText(fixture, 'Refuser la demande').click();
    expect(close).toHaveBeenCalledWith({ reason: 'Réservé aux adhérents' });
  });

  it('501 caractères (collage) : bouton désactivé, message d’erreur', async () => {
    const fixture = await render();
    type(fixture, 'textarea', 'a'.repeat(501));
    await fixture.whenStable();
    expect(buttonByText(fixture, 'Refuser la demande').disabled).toBe(true);
    expect(text(fixture)).toContain('Le motif ne doit pas dépasser 500 caractères.');
    expect((fixture.nativeElement as HTMLElement).querySelector('textarea')?.getAttribute('maxlength')).toBe('500');
  });
});

describe('AdminReservationsApi', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('pending, registrants et actions : bonnes routes ; seul le refus a un corps', () => {
    const api = TestBed.inject(AdminReservationsApi);
    api.pending(2).subscribe();
    expect(backend.expectOne((r) => r.url === '/api/admin/reservations/pending').request.urlWithParams).toBe(
      '/api/admin/reservations/pending?page=2&pageSize=20',
    );

    api.registrants(EVENT_ID).subscribe();
    expect(backend.expectOne(`/api/admin/events/${EVENT_ID}/reservations`).request.method).toBe('GET');

    api.act('r1', 'confirm').subscribe();
    const confirm = backend.expectOne('/api/admin/reservations/r1/confirm');
    expect(confirm.request.method).toBe('POST');
    expect(confirm.request.body).toBeNull();

    api.act('r1', 'refuse', 'Complet').subscribe();
    expect(backend.expectOne('/api/admin/reservations/r1/refuse').request.body).toEqual({ reason: 'Complet' });

    api.act('r1', 'refuse').subscribe();
    expect(backend.expectOne('/api/admin/reservations/r1/refuse').request.body).toEqual({ reason: null });

    for (const action of ['cancel', 'mark-paid', 'unmark-paid'] as const) {
      api.act('r1', action).subscribe();
      expect(backend.expectOne(`/api/admin/reservations/r1/${action}`).request.body).toBeNull();
    }
  });
});
