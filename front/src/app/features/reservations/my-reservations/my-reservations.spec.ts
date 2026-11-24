import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { buttonByText, problem, text } from '../../../../testing/dom';
import { memberToken } from '../../../../testing/fake-jwt';
import { ReservationsApi } from '../../../core/api/reservations.api';
import { SettingsApi } from '../../../core/api/settings.api';
import { AuthService } from '../../../core/auth/auth.service';
import { FORMATTING_PROVIDERS } from '../../../core/config/formatting.providers';
import { MyReservationDto, PagedResult, ReservationStatus } from '../../../core/models/api-models';
import { NotificationService } from '../../../core/notifications/notification.service';
import { ConfirmDialogService } from '../../../shared/ui/confirm-dialog';
import { MyReservations } from './my-reservations';

const EVENT_ID = '9ec3d743-0805-444d-de27-08df49668b4f';

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
    cancellableUntil: null,
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

function page(items: MyReservationDto[], pageNumber = 1, totalCount = items.length): PagedResult<MyReservationDto> {
  return { items, page: pageNumber, pageSize: 20, totalCount };
}

/** Pagination, erreurs de la liste et branches du gabarit de « Mes réservations » (US-D2, US-D3, ADR 0019 § 5). */
describe('MyReservations (pagination, erreurs, branches)', () => {
  let reservations: {
    list: ReturnType<typeof vi.fn>;
    cancel: ReturnType<typeof vi.fn>;
    forEvent: ReturnType<typeof vi.fn>;
    request: ReturnType<typeof vi.fn>;
  };
  let dialog: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    localStorage.clear();
    reservations = { list: vi.fn(), cancel: vi.fn(), forEvent: vi.fn(), request: vi.fn() };
    dialog = { confirm: vi.fn(() => of(true)) };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ...FORMATTING_PROVIDERS,
        { provide: ReservationsApi, useValue: reservations },
        {
          provide: SettingsApi,
          useValue: {
            getContact: vi.fn(() =>
              of({ phoneNumber: '+21620123456', whatsApp: false, isConfigured: true, version: 'v', updatedAt: null }),
            ),
          },
        },
        { provide: ConfirmDialogService, useValue: dialog },
      ],
    });
    TestBed.inject(AuthService).startSession(memberToken());
  });

  afterEach(() => localStorage.clear());

  /** list(scope, page) : une fonction par section, pour des réponses différentes selon la page. */
  function serve(current: (p: number) => PagedResult<MyReservationDto>, history: (p: number) => PagedResult<MyReservationDto>) {
    reservations.list.mockImplementation((scope: string, p: number) => of(scope === 'current' ? current(p) : history(p)));
  }

  async function render() {
    const fixture = TestBed.createComponent(MyReservations);
    await fixture.whenStable();
    return fixture;
  }

  describe('pagination', () => {
    it('45 réservations à venir : « Page 1 sur 3 », Précédent désactivé ; Suivant relit seulement la section « À venir »', async () => {
      serve(
        (p) => page([reservation('Pending', { id: `c-${p}` })], p, 45),
        () => page([reservation('Refused', { id: 'h-1', canCancel: false })]),
      );
      const fixture = await render();
      expect(text(fixture)).toContain('Page 1 sur 3');
      expect(buttonByText(fixture, 'Précédent').disabled).toBe(true);
      expect(reservations.list).toHaveBeenCalledTimes(2);

      buttonByText(fixture, 'Suivant').click();
      await fixture.whenStable();

      expect(reservations.list).toHaveBeenLastCalledWith('current', 2);
      expect(reservations.list).toHaveBeenCalledTimes(3); // l'historique n'est pas relu
      expect(text(fixture)).toContain('Page 2 sur 3');
      expect(buttonByText(fixture, 'Précédent').disabled).toBe(false);
    });

    it('Précédent revient à la page 1', async () => {
      serve(
        (p) => page([reservation('Pending')], p, 45),
        () => page([]),
      );
      const fixture = await render();
      buttonByText(fixture, 'Suivant').click();
      await fixture.whenStable();

      buttonByText(fixture, 'Précédent').click();
      await fixture.whenStable();

      expect(reservations.list).toHaveBeenLastCalledWith('current', 1);
      expect(text(fixture)).toContain('Page 1 sur 3');
    });

    it('la pagination de l’historique est indépendante de celle de « À venir »', async () => {
      serve(
        () => page([reservation('Pending')]),
        (p) => page([reservation('CancelledByUser', { id: `h-${p}`, canCancel: false })], p, 21),
      );
      const fixture = await render();
      expect(text(fixture)).toContain('Page 1 sur 2');

      buttonByText(fixture, 'Suivant').click();
      await fixture.whenStable();

      expect(reservations.list).toHaveBeenLastCalledWith('history', 2);
      expect(reservations.list).toHaveBeenCalledTimes(3);
      expect(reservations.list).toHaveBeenCalledWith('current', 1);
    });

    it('annulation de la seule ligne de la page 2 : retour à la dernière page restante (page 1)', async () => {
      let remaining = 21; // 20 en page 1, 1 en page 2
      reservations.list.mockImplementation((scope: string, p: number) => {
        if (scope === 'history') {
          return of(page([]));
        }
        if (p === 1) {
          return of(page([reservation('Pending', { id: 'c-1' })], 1, remaining));
        }
        return of(page(remaining > 20 ? [reservation('Confirmed', { id: 'c-21' })] : [], p, remaining));
      });
      reservations.cancel.mockImplementation(() => {
        remaining = 20;
        return of(reservation('CancelledByUser', { id: 'c-21' }));
      });
      const fixture = await render();
      buttonByText(fixture, 'Suivant').click();
      await fixture.whenStable();
      expect(text(fixture)).toContain('Page 2 sur 2');

      buttonByText(fixture, 'Annuler ma réservation').click();
      await fixture.whenStable();

      expect(reservations.list).toHaveBeenLastCalledWith('current', 1);
      expect(text(fixture)).not.toContain('Aucune réservation à venir.');
      expect(text(fixture)).toContain('Annuler ma demande');
    });

    it('page 1 vide : on y reste (pas de page 0)', async () => {
      serve(
        () => page([]),
        () => page([reservation('Refused', { id: 'h-1', canCancel: false })]),
      );
      const fixture = await render();
      expect(reservations.list).toHaveBeenCalledTimes(2);
      expect(text(fixture)).toContain('Aucune réservation à venir.');
    });

    it('une seule page : aucun pager', async () => {
      serve(
        () => page([reservation('Pending')], 1, 20),
        () => page([]),
      );
      const fixture = await render();
      expect(text(fixture)).not.toContain('Page 1 sur');
    });
  });

  describe('erreurs de chargement', () => {
    it('échec de la liste : message de l’API et « Réessayer » relit les deux sections', async () => {
      reservations.list.mockReturnValue(throwError(() => problem(500, 'unexpected', 'Un problème est survenu.')));
      const fixture = await render();
      expect(text(fixture)).toContain('Un problème est survenu.');
      expect(text(fixture)).not.toContain('À venir');

      serve(
        () => page([reservation('Pending')]),
        () => page([]),
      );
      buttonByText(fixture, 'Réessayer').click();
      await fixture.whenStable();

      expect(text(fixture)).not.toContain('Un problème est survenu.');
      expect(text(fixture)).toContain('À venir');
      expect(text(fixture)).toContain('En attente de confirmation');
    });

    it('serveur injoignable : message réseau', async () => {
      reservations.list.mockReturnValue(throwError(() => problem(0, 'x', 'x')));
      const fixture = await render();
      expect(text(fixture)).toContain('Impossible de joindre le serveur.');
    });

    it('chargement en cours : indicateur, sans message « aucune réservation »', async () => {
      const pending = new Subject<PagedResult<MyReservationDto>>();
      reservations.list.mockReturnValue(pending);
      const fixture = TestBed.createComponent(MyReservations);
      fixture.detectChanges();
      expect(text(fixture)).not.toContain("Vous n'avez aucune réservation.");
      expect(text(fixture)).not.toContain('Aucune réservation à venir.');
      expect((fixture.nativeElement as HTMLElement).querySelector('app-loading')).not.toBeNull();

      pending.next(page([reservation('Pending')]));
      pending.complete();
      await fixture.whenStable();
      expect(text(fixture)).toContain('En attente de confirmation');
    });
  });

  describe('annulation depuis la liste', () => {
    function twoSections(current: MyReservationDto[]) {
      serve(
        () => page(current),
        () => page([reservation('Refused', { id: 'h-1', canCancel: false })]),
      );
    }

    it('reservation_state_changed : message de l’API sous la ligne, liste relue, bouton à nouveau actif', async () => {
      twoSections([reservation('Confirmed')]);
      reservations.cancel.mockReturnValue(
        throwError(() => problem(409, 'reservation_state_changed', 'Cette demande a déjà été traitée ou annulée.')),
      );
      const fixture = await render();
      expect(reservations.list).toHaveBeenCalledTimes(2);

      buttonByText(fixture, 'Annuler ma réservation').click();
      await fixture.whenStable();

      expect(text(fixture)).toContain('Cette demande a déjà été traitée ou annulée.');
      expect(reservations.list).toHaveBeenCalledTimes(4);
      expect(buttonByText(fixture, 'Annuler ma réservation').disabled).toBe(false);
      expect(text(fixture)).not.toContain('Contacter l'); // pas de carte de contact pour ce code
    });

    it('reservation_state_changed (demande échue) : la relecture remplace la ligne par son statut effectif', async () => {
      let calls = 0;
      reservations.list.mockImplementation((scope: string) => {
        if (scope === 'history') {
          return of(page([reservation('Expired', { id: 'h-1', canCancel: false })]));
        }
        calls++;
        return of(calls === 1 ? page([reservation('Pending')]) : page([]));
      });
      reservations.cancel.mockReturnValue(
        throwError(() => problem(409, 'reservation_state_changed', 'Votre demande a expiré. Vous pouvez en faire une nouvelle.')),
      );
      const fixture = await render();

      buttonByText(fixture, 'Annuler ma demande').click();
      await fixture.whenStable();

      expect(text(fixture)).toContain('Aucune réservation à venir.');
    });

    it('cancellation_deadline_passed : message, carte de contact, pas de relecture', async () => {
      twoSections([reservation('Confirmed')]);
      reservations.cancel.mockReturnValue(
        throwError(() => problem(409, 'cancellation_deadline_passed', "Le délai d'annulation est dépassé : contactez l'association")),
      );
      const fixture = await render();

      buttonByText(fixture, 'Annuler ma réservation').click();
      await fixture.whenStable();

      expect(text(fixture)).toContain("Le délai d'annulation est dépassé : contactez l'association");
      expect(text(fixture)).toContain('+216 20 123 456');
      expect(reservations.list).toHaveBeenCalledTimes(2);
    });

    it('autre erreur (500) : message sous la ligne, pas de relecture, pas de carte de contact', async () => {
      twoSections([reservation('Confirmed')]);
      reservations.cancel.mockReturnValue(throwError(() => problem(500, 'unexpected', 'Un problème est survenu.')));
      const fixture = await render();

      buttonByText(fixture, 'Annuler ma réservation').click();
      await fixture.whenStable();

      expect(text(fixture)).toContain('Un problème est survenu.');
      expect(reservations.list).toHaveBeenCalledTimes(2);
      expect(text(fixture)).not.toContain('Contacter l');
    });

    it('l’erreur ne s’affiche que sous la ligne concernée', async () => {
      twoSections([reservation('Confirmed', { id: 'r-1' }), reservation('Confirmed', { id: 'r-2' })]);
      reservations.cancel.mockReturnValue(throwError(() => problem(500, 'unexpected', 'Un problème est survenu.')));
      const fixture = await render();

      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
        .filter((b) => b.textContent?.trim() === 'Annuler ma réservation')[1]
        .click();
      await fixture.whenStable();

      const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('li.reservation'));
      expect(rows[0].textContent).not.toContain('Un problème est survenu.');
      expect(rows[1].textContent).toContain('Un problème est survenu.');
    });

    it('dialogue refusé : aucun appel, aucune relecture, bouton actif', async () => {
      twoSections([reservation('Pending')]);
      dialog.confirm.mockReturnValue(of(false));
      const fixture = await render();

      buttonByText(fixture, 'Annuler ma demande').click();
      await fixture.whenStable();

      expect(reservations.cancel).not.toHaveBeenCalled();
      expect(reservations.list).toHaveBeenCalledTimes(2);
      expect(buttonByText(fixture, 'Annuler ma demande').disabled).toBe(false);
    });

    it('double clic : une seule annulation, boutons désactivés pendant l’appel', async () => {
      twoSections([reservation('Pending'), reservation('Confirmed', { id: 'r-2' })]);
      const response = new Subject<MyReservationDto>();
      reservations.cancel.mockReturnValue(response);
      const fixture = await render();

      const button = buttonByText(fixture, 'Annuler ma demande');
      button.click();
      button.click();
      buttonByText(fixture, 'Annuler ma réservation').click(); // une autre ligne pendant l'appel
      await fixture.whenStable();

      expect(dialog.confirm).toHaveBeenCalledTimes(1);
      expect(reservations.cancel).toHaveBeenCalledTimes(1);
      expect(buttonByText(fixture, 'Annuler ma demande').disabled).toBe(true);
      expect(buttonByText(fixture, 'Annuler ma réservation').disabled).toBe(true);

      response.next(reservation('CancelledByUser'));
      response.complete();
      await fixture.whenStable();
      expect(buttonByText(fixture, 'Annuler ma demande').disabled).toBe(false);
    });

    it('succès : notification « Votre demande est annulée. » (demande) ou « Votre réservation est annulée. »', async () => {
      twoSections([reservation('Confirmed')]);
      reservations.cancel.mockReturnValue(of(reservation('CancelledByUser')));
      const notifications = TestBed.inject(NotificationService);
      const fixture = await render();

      buttonByText(fixture, 'Annuler ma réservation').click();
      await fixture.whenStable();

      expect(notifications.current()?.message).toBe('Votre réservation est annulée.');
    });

    it('une erreur précédente est effacée à la tentative suivante', async () => {
      twoSections([reservation('Confirmed')]);
      reservations.cancel
        .mockReturnValueOnce(throwError(() => problem(500, 'unexpected', 'Un problème est survenu.')))
        .mockReturnValueOnce(new Subject());
      const fixture = await render();
      buttonByText(fixture, 'Annuler ma réservation').click();
      await fixture.whenStable();
      expect(text(fixture)).toContain('Un problème est survenu.');

      buttonByText(fixture, 'Annuler ma réservation').click();
      await fixture.whenStable();

      expect(text(fixture)).not.toContain('Un problème est survenu.');
    });
  });

  describe('affichage selon le statut', () => {
    async function renderHistory(items: MyReservationDto[], current: MyReservationDto[] = []) {
      serve(
        () => page(current),
        () => page(items),
      );
      return render();
    }

    it('refusée avec motif : motif affiché ; sans motif : pas de ligne « Motif »', async () => {
      const fixture = await renderHistory([
        reservation('Refused', { id: 'a', canCancel: false, refusalReason: 'Complet pour les débutants' }),
        reservation('Refused', { id: 'b', canCancel: false }),
      ]);
      const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('li.reservation'));
      expect(rows[0].textContent).toContain('Motif : Complet pour les débutants');
      expect(rows[1].textContent).not.toContain('Motif');
    });

    it('annulée par l’association et payée : mention du remboursement ; non payée : rien', async () => {
      const fixture = await renderHistory([
        reservation('CancelledByAssociation', { id: 'a', canCancel: false, isPaid: true }),
        reservation('CancelledByAssociation', { id: 'b', canCancel: false }),
      ]);
      const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('li.reservation'));
      expect(rows[0].textContent).toContain('le remboursement éventuel');
      expect(rows[1].textContent).not.toContain('remboursement');
    });

    it('payée sans date : « Payée » seul ; événement gratuit : « Gratuit »', async () => {
      const fixture = await renderHistory([
        reservation('Confirmed', { id: 'a', canCancel: false, isPaid: true, paidAt: null }),
        reservation('Expired', { id: 'b', canCancel: false, event: { ...reservation('Expired').event, price: null } }),
      ]);
      const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('li.reservation'));
      expect(rows[0].textContent).toContain('Payée');
      expect(rows[0].textContent).not.toContain('Payée le');
      expect(rows[1].textContent).toContain('Gratuit');
    });

    it('payée avec date : « Payée le 2 décembre 2026 » (format jour)', async () => {
      const fixture = await renderHistory([
        reservation('Confirmed', { id: 'a', canCancel: false, isPaid: true, paidAt: '2026-12-02T10:00:00+00:00' }),
      ]);
      expect(text(fixture)).toContain('Payée le 2 décembre 2026');
    });

    it('confirmée non annulable, événement pas commencé : explication et numéro ; commencé : rien', async () => {
      const fixture = await renderHistory(
        [],
        [
          reservation('Confirmed', { id: 'a', canCancel: false }),
          reservation('Confirmed', {
            id: 'b',
            canCancel: false,
            event: { ...reservation('Confirmed').event, hasStarted: true },
          }),
        ],
      );
      const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('li.reservation'));
      expect(rows[0].textContent).toContain("Le délai d'annulation est dépassé.");
      expect(rows[0].textContent).toContain('+216 20 123 456');
      expect(rows[1].textContent).not.toContain("Le délai d'annulation");
      expect(rows[1].querySelector('button')).toBeNull();
    });

    it('demande en attente : échéance et carte de contact ; lien vers l’événement visible, texte simple sinon', async () => {
      const fixture = await renderHistory(
        [reservation('CancelledByUser', { id: 'h', canCancel: false, event: { ...reservation('Pending').event, isVisible: false } })],
        [reservation('Pending')],
      );
      const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('li.reservation'));
      expect(rows[0].textContent).toContain('À faire confirmer avant le jeudi 3 décembre 2026 à 10:00');
      expect(rows[0].textContent).toContain('+216 20 123 456');
      expect(rows[0].querySelector('a.reservation__title')?.getAttribute('href')).toBe(`/evenements/${EVENT_ID}`);
      expect(rows[1].querySelector('a.reservation__title')).toBeNull();
      expect(rows[1].textContent).toContain('Événement indisponible');
    });

    it('deux cartes de contact : id de titre distincts, chaque aria-labelledby désigne son propre titre', async () => {
      const fixture = await renderHistory([], [reservation('Pending', { id: 'a' }), reservation('Pending', { id: 'b' })]);
      const cards = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('section.contact-card'));
      expect(cards).toHaveLength(2);
      const ids = cards.map((c) => c.getAttribute('aria-labelledby'));
      expect(new Set(ids).size).toBe(2);
      cards.forEach((card, i) => expect(card.querySelector('h3')?.id).toBe(ids[i]));
    });

    it('historique vide mais « À venir » non vide : message « Aucune réservation passée. »', async () => {
      const fixture = await renderHistory([], [reservation('Pending')]);
      expect(text(fixture)).toContain('Aucune réservation passée.');
      expect(text(fixture)).not.toContain("Vous n'avez aucune réservation.");
    });
  });
});
