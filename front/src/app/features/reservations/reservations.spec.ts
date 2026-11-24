import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { buttonByText, problem, text } from '../../../testing/dom';
import { memberToken } from '../../../testing/fake-jwt';
import { ReservationsApi } from '../../core/api/reservations.api';
import { SettingsApi } from '../../core/api/settings.api';
import { AuthService } from '../../core/auth/auth.service';
import { FORMATTING_PROVIDERS } from '../../core/config/formatting.providers';
import {
  ContactSettingsDto,
  EventDetailDto,
  MyReservationDto,
  PagedResult,
  ReservationStatus,
} from '../../core/models/api-models';
import {
  adminStatusLabel,
  memberStatusLabel,
  previousReservationMessage,
} from '../../shared/reservations/reservation-labels';
import { ContactCard } from '../../shared/ui/contact-card';
import { ConfirmDialogService } from '../../shared/ui/confirm-dialog';
import { MyReservations } from './my-reservations/my-reservations';
import { ReservationPanel } from './reservation-panel/reservation-panel';

const EVENT_ID = '9ec3d743-0805-444d-de27-08df49668b4f';

function eventDetail(overrides: Partial<EventDetailDto> = {}): EventDetailDto {
  return {
    id: EVENT_ID,
    title: 'Atelier couture',
    location: 'Tunis',
    description: 'Initiation.',
    startsAt: '2026-12-05T09:00:00+00:00',
    endsAt: '2026-12-05T11:00:00+00:00',
    price: 15,
    capacity: 20,
    remainingSeats: 8,
    status: 'Published',
    hasStarted: false,
    hasEnded: false,
    isFull: false,
    isBookable: true,
    ...overrides,
  };
}

function reservation(status: ReservationStatus, overrides: Partial<MyReservationDto> = {}): MyReservationDto {
  return {
    id: 'r-1',
    status,
    requestedAt: '2026-12-01T09:00:00+00:00',
    expiresAt: status === 'Pending' ? '2026-12-03T09:00:00+00:00' : null,
    decidedAt: null,
    refusalReason: null,
    cancelledAt: null,
    isPaid: false,
    paidAt: null,
    canCancel: status === 'Pending' || status === 'Confirmed',
    cancellableUntil: status === 'Confirmed' ? '2026-12-04T09:00:00+00:00' : null,
    event: {
      id: EVENT_ID,
      title: 'Atelier couture',
      location: 'Tunis',
      startsAt: '2026-12-05T09:00:00+00:00',
      endsAt: '2026-12-05T11:00:00+00:00',
      price: 15,
      status: 'Published',
      hasStarted: false,
      hasEnded: false,
      isVisible: true,
    },
    ...overrides,
  };
}

const CONTACT: ContactSettingsDto = {
  phoneNumber: '+21620123456',
  whatsApp: true,
  isConfigured: true,
  version: 'AAAAAAAAB9E=',
  updatedAt: null,
};

describe('Réservations (tâche A)', () => {
  let reservations: {
    forEvent: ReturnType<typeof vi.fn>;
    request: ReturnType<typeof vi.fn>;
    cancel: ReturnType<typeof vi.fn>;
    list: ReturnType<typeof vi.fn>;
  };
  let settings: { getContact: ReturnType<typeof vi.fn> };
  let dialog: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    localStorage.clear();
    reservations = { forEvent: vi.fn(() => of(null)), request: vi.fn(), cancel: vi.fn(), list: vi.fn() };
    settings = { getContact: vi.fn(() => of(CONTACT)) };
    dialog = { confirm: vi.fn(() => of(true)) };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ...FORMATTING_PROVIDERS,
        { provide: ReservationsApi, useValue: reservations },
        { provide: SettingsApi, useValue: settings },
        { provide: ConfirmDialogService, useValue: dialog },
      ],
    });
  });

  afterEach(() => localStorage.clear());

  function login(): void {
    TestBed.inject(AuthService).startSession(memberToken());
  }

  describe('libellés', () => {
    it('distingue la vue membre et la vue admin', () => {
      expect(memberStatusLabel('Pending')).toBe('En attente de confirmation');
      expect(memberStatusLabel('CancelledByUser')).toBe('Annulée par vous');
      expect(adminStatusLabel('Pending')).toBe('En attente');
      expect(adminStatusLabel('CancelledByUser')).toBe('Annulée par le membre');
      expect(adminStatusLabel('CancelledByAssociation')).toBe("Annulée par l'association");
    });

    it('donne un message pour la dernière demande terminée, aucun pour une active', () => {
      expect(previousReservationMessage('Refused')).toBe('Votre demande précédente a été refusée.');
      expect(previousReservationMessage('Expired')).toContain('expiré');
      expect(previousReservationMessage('Pending')).toBeNull();
      expect(previousReservationMessage('Confirmed')).toBeNull();
    });
  });

  describe('ContactCard (US-D4, R8)', () => {
    it('affiche le numéro, un lien tel: et un lien wa.me seulement si WhatsApp', async () => {
      login();
      const fixture = TestBed.createComponent(ContactCard);
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      expect(text(fixture)).toContain('+216 20 123 456');
      expect(element.querySelector('a[href="tel:+21620123456"]')).not.toBeNull();
      expect(element.querySelector('a[href="https://wa.me/21620123456"]')?.getAttribute('rel')).toContain('noopener');
    });

    it('sans WhatsApp : pas de lien wa.me', async () => {
      settings.getContact.mockReturnValue(of({ ...CONTACT, whatsApp: false }));
      login();
      const fixture = TestBed.createComponent(ContactCard);
      await fixture.whenStable();
      expect((fixture.nativeElement as HTMLElement).querySelector('a[href^="https://wa.me"]')).toBeNull();
    });

    it('sans numéro configuré : message R8', async () => {
      settings.getContact.mockReturnValue(of({ ...CONTACT, phoneNumber: null, whatsApp: false, isConfigured: false }));
      login();
      const fixture = TestBed.createComponent(ContactCard);
      await fixture.whenStable();
      expect(text(fixture)).toContain("L'association n'a pas communiqué de numéro pour le moment.");
    });

    it('un seul appel pour plusieurs cartes (ContactStore partagé)', async () => {
      login();
      const first = TestBed.createComponent(ContactCard);
      const second = TestBed.createComponent(ContactCard);
      await first.whenStable();
      await second.whenStable();
      expect(settings.getContact).toHaveBeenCalledTimes(1);
    });
  });

  describe('ReservationPanel (US-D1, US-D3)', () => {
    async function render(detail = eventDetail()) {
      const fixture = TestBed.createComponent(ReservationPanel);
      fixture.componentRef.setInput('event', detail);
      const changed = vi.fn();
      fixture.componentInstance.changed.subscribe(changed);
      await fixture.whenStable();
      return { fixture, changed };
    }

    it('visiteur : lien vers la connexion, sans appeler /api/me', async () => {
      const { fixture } = await render();
      expect((fixture.nativeElement as HTMLElement).querySelector('a[href^="/connexion"]')?.textContent).toContain(
        'Demander une place',
      );
      expect(text(fixture)).toContain('Vous devez être connecté pour demander une place.');
      expect(reservations.forEvent).not.toHaveBeenCalled();
    });

    it('membre sans demande : bouton ; double clic = un seul appel ; puis demande en attente et numéro', async () => {
      login();
      const response = new Subject<MyReservationDto>();
      reservations.request.mockReturnValue(response);
      const { fixture, changed } = await render();

      const button = buttonByText(fixture, 'Demander une place');
      button.click();
      button.click();
      await fixture.whenStable();
      expect(reservations.request).toHaveBeenCalledTimes(1);
      expect(text(fixture)).toContain('Envoi en cours…');

      response.next(reservation('Pending'));
      response.complete();
      await fixture.whenStable();

      const content = text(fixture);
      expect(content).toContain('Demande en attente de confirmation');
      expect(content).toContain('jeudi 3 décembre 2026 à 10:00'); // échéance à l'heure de Tunis
      expect(content).toContain('Prix : 15 TND, à régler sur place.');
      expect(content).toContain('+216 20 123 456');
      expect(content).not.toContain('Envoi en cours');
      expect(changed).toHaveBeenCalledTimes(1);
    });

    it('409 already_reserved : aucun message, la demande existante est relue', async () => {
      login();
      reservations.request.mockReturnValue(
        throwError(() => problem(409, 'already_reserved', 'Vous avez déjà une demande en cours ou une réservation pour cet événement')),
      );
      const { fixture } = await render();
      reservations.forEvent.mockReturnValue(of(reservation('Pending')));

      buttonByText(fixture, 'Demander une place').click();
      await fixture.whenStable();

      expect(text(fixture)).not.toContain('Vous avez déjà');
      expect(text(fixture)).toContain('Demande en attente de confirmation');
      expect(reservations.forEvent).toHaveBeenCalledTimes(2);
    });

    it('409 event_full : message de l’API et rechargement du détail', async () => {
      login();
      reservations.request.mockReturnValue(throwError(() => problem(409, 'event_full', 'Événement complet')));
      const { fixture, changed } = await render();

      buttonByText(fixture, 'Demander une place').click();
      await fixture.whenStable();

      expect(text(fixture)).toContain('Événement complet');
      expect(changed).toHaveBeenCalledTimes(1);
    });

    it('confirmée annulable : date limite et bouton « Annuler ma réservation »', async () => {
      login();
      reservations.forEvent.mockReturnValue(of(reservation('Confirmed')));
      const { fixture } = await render();
      expect(text(fixture)).toContain('Réservation confirmée');
      expect(text(fixture)).toContain("Vous pouvez annuler jusqu'au vendredi 4 décembre 2026 à 10:00.");
      expect(buttonByText(fixture, 'Annuler ma réservation')).toBeTruthy();
      expect(text(fixture)).not.toContain('Demander une place');
    });

    it('confirmée, délai dépassé : pas de bouton, numéro à contacter', async () => {
      login();
      reservations.forEvent.mockReturnValue(of(reservation('Confirmed', { canCancel: false, isPaid: true })));
      const { fixture } = await render();
      expect(text(fixture)).toContain("Le délai d'annulation est dépassé. Pour vous désister, contactez l'association.");
      expect(text(fixture)).toContain('Payée');
      expect(text(fixture)).toContain('+216 20 123 456');
      expect(text(fixture)).not.toContain('Annuler ma réservation');
    });

    it('dernière demande refusée avec motif : information et bouton si réservable (QF-3)', async () => {
      login();
      reservations.forEvent.mockReturnValue(of(reservation('Refused', { refusalReason: 'Atelier réservé aux débutants' })));
      const { fixture } = await render();
      expect(text(fixture)).toContain('Votre demande précédente a été refusée.');
      expect(text(fixture)).toContain('Motif : Atelier réservé aux débutants');
      expect(buttonByText(fixture, 'Demander une place')).toBeTruthy();
    });

    it('événement complet sans demande : pas de bouton, « Cet événement est complet. »', async () => {
      login();
      const { fixture } = await render(eventDetail({ isFull: true, isBookable: false, remainingSeats: 0 }));
      expect(text(fixture)).toContain('Cet événement est complet.');
      expect(text(fixture)).not.toContain('Demander une place');
    });

    it('annulation confirmée dans le dialogue : appel, message, panneau mis à jour', async () => {
      login();
      reservations.forEvent.mockReturnValue(of(reservation('Pending')));
      reservations.cancel.mockReturnValue(of(reservation('CancelledByUser', { canCancel: false })));
      const { fixture, changed } = await render();

      buttonByText(fixture, 'Annuler ma demande').click();
      await fixture.whenStable();

      expect(dialog.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Annuler cette demande ?' }));
      expect(reservations.cancel).toHaveBeenCalledWith('r-1');
      expect(text(fixture)).toContain('Vous avez annulé votre demande précédente.');
      expect(changed).toHaveBeenCalled();
    });

    it('dialogue refusé : aucun appel', async () => {
      login();
      dialog.confirm.mockReturnValue(of(false));
      reservations.forEvent.mockReturnValue(of(reservation('Pending')));
      const { fixture } = await render();

      buttonByText(fixture, 'Annuler ma demande').click();
      await fixture.whenStable();

      expect(reservations.cancel).not.toHaveBeenCalled();
    });

    it('409 cancellation_deadline_passed : message et carte de contact', async () => {
      login();
      reservations.forEvent.mockReturnValue(of(reservation('Confirmed', { isPaid: true })));
      reservations.cancel.mockReturnValue(
        throwError(() => problem(409, 'cancellation_deadline_passed', "Le délai d'annulation est dépassé : contactez l'association")),
      );
      const { fixture } = await render();

      buttonByText(fixture, 'Annuler ma réservation').click();
      await fixture.whenStable();

      expect(dialog.confirm).toHaveBeenCalledWith(
        expect.objectContaining({ message: expect.stringContaining('le remboursement éventuel') }),
      );
      expect(text(fixture)).toContain("Le délai d'annulation est dépassé : contactez l'association");
      expect(text(fixture)).toContain('Contacter l');
    });
  });

  describe('MyReservations (US-D2)', () => {
    function pageOf(items: MyReservationDto[]): PagedResult<MyReservationDto> {
      return { items, page: 1, pageSize: 20, totalCount: items.length };
    }

    it('deux sections, libellés membre, prix ou « Payée », « Terminé »', async () => {
      login();
      reservations.list.mockImplementation((scope: string) =>
        of(
          scope === 'current'
            ? pageOf([reservation('Pending')])
            : pageOf([
                reservation('CancelledByUser', { id: 'r-2', canCancel: false }),
                reservation('Confirmed', {
                  id: 'r-3',
                  canCancel: false,
                  isPaid: true,
                  paidAt: '2026-12-05T10:00:00+00:00',
                  event: { ...reservation('Confirmed').event, hasStarted: true, hasEnded: true, isVisible: false },
                }),
              ]),
        ),
      );
      const fixture = TestBed.createComponent(MyReservations);
      await fixture.whenStable();

      const content = text(fixture);
      expect(content).toContain('À venir');
      expect(content).toContain('Historique');
      expect(content).toContain('En attente de confirmation');
      expect(content).toContain('15 TND, à régler sur place');
      expect(content).toContain('À faire confirmer avant le jeudi 3 décembre 2026 à 10:00');
      expect(content).toContain('Annulée par vous');
      expect(content).toContain('Confirmée — Terminé');
      expect(content).toContain('Payée le 5 décembre 2026');
      expect(content).toContain('Événement indisponible');
      expect(reservations.list).toHaveBeenCalledWith('current', 1);
      expect(reservations.list).toHaveBeenCalledWith('history', 1);
    });

    it('aucune réservation : message et lien vers les événements', async () => {
      login();
      reservations.list.mockReturnValue(of(pageOf([])));
      const fixture = TestBed.createComponent(MyReservations);
      await fixture.whenStable();
      expect(text(fixture)).toContain("Vous n'avez aucune réservation.");
      expect(text(fixture)).toContain('Voir les événements à venir');
    });

    it('annuler depuis la liste : recharge les deux sections', async () => {
      login();
      reservations.list.mockImplementation((scope: string) =>
        of(scope === 'current' ? pageOf([reservation('Confirmed')]) : pageOf([])),
      );
      reservations.cancel.mockReturnValue(of(reservation('CancelledByUser')));
      const fixture = TestBed.createComponent(MyReservations);
      await fixture.whenStable();

      buttonByText(fixture, 'Annuler ma réservation').click();
      await fixture.whenStable();

      expect(dialog.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Annuler cette réservation ?' }));
      expect(reservations.list).toHaveBeenCalledTimes(4);
    });
  });
});
