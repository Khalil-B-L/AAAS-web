import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { buttonByText, problem, query, text } from '../../../testing/dom';
import { memberToken } from '../../../testing/fake-jwt';
import { EventsApi } from '../../core/api/events.api';
import { ReservationsApi } from '../../core/api/reservations.api';
import { SettingsApi } from '../../core/api/settings.api';
import { FORMATTING_PROVIDERS } from '../../core/config/formatting.providers';
import { AuthService } from '../../core/auth/auth.service';
import { EventDetailDto, MyReservationDto, PagedResult, EventSummaryDto } from '../../core/models/api-models';
import { EventDetail } from './event-detail/event-detail';
import { EventList } from './event-list/event-list';

const GUID = '9ec3d743-0805-444d-de27-08df49668b4f';

function summary(overrides: Partial<EventDetailDto> = {}): EventDetailDto {
  return {
    id: GUID,
    title: 'Atelier couture',
    location: 'Tunis',
    description: 'Initiation.',
    startsAt: '2026-12-05T09:00:00+00:00',
    endsAt: '2026-12-05T11:00:00+00:00',
    price: null,
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

function page(items: EventSummaryDto[], totalCount = items.length, pageNumber = 1): PagedResult<EventSummaryDto> {
  return { items, page: pageNumber, pageSize: 20, totalCount };
}

describe('pages publiques', () => {
  let api: { list: ReturnType<typeof vi.fn>; get: ReturnType<typeof vi.fn> };
  let reservations: { forEvent: ReturnType<typeof vi.fn>; request: ReturnType<typeof vi.fn>; cancel: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    localStorage.clear();
    api = { list: vi.fn(), get: vi.fn() };
    // Panneau « Ma réservation » : aucune demande (204 → null).
    reservations = { forEvent: vi.fn(() => of(null)), request: vi.fn(), cancel: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ...FORMATTING_PROVIDERS,
        { provide: EventsApi, useValue: api },
        { provide: ReservationsApi, useValue: reservations },
        // Carte de contact affichée sous une demande en attente.
        {
          provide: SettingsApi,
          useValue: {
            getContact: vi.fn(() =>
              of({ phoneNumber: '+21620123456', whatsApp: false, isConfigured: true, version: 'v', updatedAt: null }),
            ),
          },
        },
      ],
    });
  });

  afterEach(() => localStorage.clear());

  describe('EventList (US-C1)', () => {
    it('affiche titre, date de Tunis, prix et mention ; se met à jour à l’arrivée de la réponse', async () => {
      // Réponse différée : on vérifie que l'écran se met à jour APRÈS coup (zoneless, signals).
      const response = new Subject<PagedResult<EventSummaryDto>>();
      api.list.mockReturnValue(response);
      const fixture = TestBed.createComponent(EventList);
      // Pas whenStable() ici : il attend la fin des ressources en cours (PendingTasks), donc la réponse.
      fixture.detectChanges();
      expect(text(fixture)).toContain('Chargement');

      response.next(page([summary(), summary({ id: 'e2', title: 'Concert', price: 15, hasStarted: true })]));
      response.complete();
      await fixture.whenStable();

      const content = text(fixture);
      expect(content).toContain('Atelier couture');
      expect(content).toContain('sam. 5 déc. 2026, 10:00');
      expect(content).toContain('Gratuit');
      expect(content).toContain('8 places restantes');
      expect(content).toContain('15 TND, à régler sur place');
      expect(content).toContain('Réservations closes');
    });

    it('lit la page dans l’URL et ramène une valeur invalide à 1', async () => {
      api.list.mockReturnValue(of(page([])));
      const fixture = TestBed.createComponent(EventList);
      fixture.componentRef.setInput('page', 'abc');
      await fixture.whenStable();
      expect(api.list).toHaveBeenLastCalledWith(1);
      fixture.componentRef.setInput('page', '3');
      await fixture.whenStable();
      expect(api.list).toHaveBeenLastCalledWith(3);
    });

    it('affiche le message vide', async () => {
      api.list.mockReturnValue(of(page([])));
      const fixture = TestBed.createComponent(EventList);
      await fixture.whenStable();
      expect(text(fixture)).toContain('Aucun événement prévu pour le moment.');
    });

    it('affiche le pager quand il y a plusieurs pages', async () => {
      api.list.mockReturnValue(of(page([summary()], 45, 2)));
      const fixture = TestBed.createComponent(EventList);
      fixture.componentRef.setInput('page', '2');
      await fixture.whenStable();
      expect(text(fixture)).toContain('Page 2 sur 3');
    });

    it('propose de réessayer si l’API est injoignable', async () => {
      api.list.mockReturnValue(throwError(() => problem(503, 'x', 'x')));
      const fixture = TestBed.createComponent(EventList);
      await fixture.whenStable();
      expect(text(fixture)).toContain('Le service est momentanément indisponible.');
      expect(text(fixture)).toContain('Réessayer');
    });
  });

  describe('EventDetail (US-C2, visiteur)', () => {
    async function render(id = GUID) {
      const fixture = TestBed.createComponent(EventDetail);
      fixture.componentRef.setInput('id', id);
      await fixture.whenStable();
      return fixture;
    }

    it('montre « Demander une place » à un visiteur, vers la connexion avec retour', async () => {
      api.get.mockReturnValue(of(summary()));
      const fixture = await render();
      const link = query<HTMLAnchorElement>(fixture, 'a[href^="/connexion"]');
      expect(link.textContent).toContain('Demander une place');
      expect(link.getAttribute('href')).toBe(`/connexion?returnUrl=%2Fevenements%2F${GUID}`);
    });

    it('UX-9 levé : un membre connecté sans demande voit le bouton « Demander une place » (pas un lien de connexion)', async () => {
      TestBed.inject(AuthService).startSession(memberToken());
      api.get.mockReturnValue(of(summary()));
      const fixture = await render();
      expect(fixture.nativeElement.querySelector('a[href^="/connexion"]')).toBeNull();
      expect(text(fixture)).toContain('Demander une place');
    });

    it('après une demande : le détail est relu (places), « ma demande » n’est PAS relue ni remise en chargement', async () => {
      TestBed.inject(AuthService).startSession(memberToken());
      api.get.mockReturnValueOnce(of(summary())).mockReturnValueOnce(of(summary({ remainingSeats: 7 })));
      const created: MyReservationDto = {
        id: 'r-1',
        status: 'Pending',
        requestedAt: '2026-12-01T09:00:00+00:00',
        expiresAt: '2026-12-03T09:00:00+00:00',
        decidedAt: null,
        refusalReason: null,
        cancelledAt: null,
        isPaid: false,
        paidAt: null,
        canCancel: true,
        cancellableUntil: null,
        event: {
          id: GUID,
          title: 'Atelier couture',
          location: 'Tunis',
          startsAt: '2026-12-05T09:00:00+00:00',
          endsAt: '2026-12-05T11:00:00+00:00',
          price: null,
          status: 'Published',
          hasStarted: false,
          hasEnded: false,
          isVisible: true,
        },
      };
      reservations.request.mockReturnValue(of(created));
      const fixture = await render();
      expect(reservations.forEvent).toHaveBeenCalledTimes(1);

      buttonByText(fixture, 'Demander une place').click();
      await fixture.whenStable();

      expect(api.get).toHaveBeenCalledTimes(2);
      expect(text(fixture)).toContain('7 places restantes');
      expect(reservations.forEvent).toHaveBeenCalledTimes(1);
      expect(text(fixture)).toContain('Demande en attente de confirmation');
    });

    it('signale un événement terminé, sans bouton', async () => {
      api.get.mockReturnValue(of(summary({ hasStarted: true, hasEnded: true, isBookable: false })));
      const fixture = await render();
      expect(text(fixture)).toContain('Cet événement est terminé.');
      expect(text(fixture)).not.toContain('Demander une place');
    });

    it('affiche « Contenu introuvable » pour un brouillon ou un identifiant inconnu (404)', async () => {
      api.get.mockReturnValue(throwError(() => problem(404, 'not_found', 'Contenu introuvable.')));
      const fixture = await render('00000000-0000-0000-0000-00000000dead');
      expect(text(fixture)).toContain('Contenu introuvable');
      expect(text(fixture)).toContain('Retour aux événements');
    });

    it("affiche « Contenu introuvable » sans appeler l'API pour un identifiant mal formé", async () => {
      // L'API répondrait 404 (correctif D1) : le front s'épargne simplement l'appel.
      const fixture = await render('abc');
      expect(api.get).not.toHaveBeenCalled();
      expect(text(fixture)).toContain('Contenu introuvable');
    });
  });
});
