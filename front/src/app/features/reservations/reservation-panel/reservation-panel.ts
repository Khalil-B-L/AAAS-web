import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { ReservationsApi } from '../../../core/api/reservations.api';
import { AuthService } from '../../../core/auth/auth.service';
import { toApiError } from '../../../core/errors/api-error';
import { EventDetailDto, MyReservationDto } from '../../../core/models/api-models';
import { NotificationService } from '../../../core/notifications/notification.service';
import { DETAIL_DATE_FORMAT } from '../../../core/time/date-formats';
import { TndPricePipe } from '../../../shared/pipes/tnd-price.pipe';
import { isActiveStatus, previousReservationMessage } from '../../../shared/reservations/reservation-labels';
import { ContactCard } from '../../../shared/ui/contact-card';
import { Loading } from '../../../shared/ui/loading';
import { ReservationCancellation } from '../reservation-cancellation';

/** Codes après lesquels l'événement a changé (places, statut) : le parent recharge le détail public. */
const EVENT_CHANGED_CODES = new Set(['event_full', 'reservations_closed', 'event_cancelled']);

/**
 * Panneau « Ma réservation » du détail d'un événement (US-C2 partie membre, US-D1, US-D3, US-D4 ; lève UX-9).
 * « Ma demande » est lue par un appel séparé, seulement pour un connecté (ADR 0021 § 1).
 */
@Component({
  selector: 'app-reservation-panel',
  imports: [DatePipe, RouterLink, MatButtonModule, TndPricePipe, ContactCard, Loading],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reservation-panel.html',
  styleUrl: './reservation-panel.scss',
})
export class ReservationPanel {
  private readonly api = inject(ReservationsApi);
  private readonly cancellation = inject(ReservationCancellation);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);

  /** input() : donnée reçue du parent (≈ [Parameter] de Blazor), sous forme de signal. */
  readonly event = input.required<EventDetailDto>();
  /** output() : événement émis vers le parent (≈ EventCallback) pour qu'il recharge les places restantes. */
  readonly changed = output<void>();

  /**
   * Clé de la lecture « ma demande ». Après une demande, le parent relit le détail : event() devient un NOUVEL objet
   * (même id). Sans `equal`, la clé serait un nouvel objet elle aussi, la ressource repartirait en chargement et
   * relirait l'API pour rien. `equal` compare par valeur (≈ un record C#) : même événement, même compte → aucune
   * notification, la ressource garde sa valeur (celle posée par set()).
   */
  private readonly mineKey = computed(
    () => (this.auth.isLoggedIn() ? { eventId: this.event().id, userId: this.auth.session()?.userId } : undefined),
    { equal: (a, b) => a?.eventId === b?.eventId && a?.userId === b?.userId },
  );

  // Ressource conditionnelle : params undefined (visiteur) → au repos, aucun appel à l'API.
  // L'id de l'utilisateur fait partie de la clé : changer de compte relance la lecture.
  protected readonly mine = rxResource({
    params: () => this.mineKey(),
    stream: ({ params }) => this.api.forEvent(params.eventId),
  });

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Afficher la carte de contact sous le message d'erreur (délai d'annulation dépassé, R8). */
  protected readonly contactAfterError = signal(false);

  protected readonly reservation = computed<MyReservationDto | null>(() =>
    this.mine.hasValue() ? (this.mine.value() ?? null) : null,
  );
  protected readonly hasActive = computed(() => {
    const current = this.reservation();
    return current !== null && isActiveStatus(current.status);
  });
  protected readonly previousMessage = computed(() => {
    const current = this.reservation();
    return current ? previousReservationMessage(current.status) : null;
  });
  protected readonly canRequest = computed(
    () => this.auth.isLoggedIn() && this.event().isBookable && this.mine.hasValue() && !this.hasActive(),
  );
  protected readonly loadError = computed(() => {
    const error = this.mine.error();
    return error ? toApiError(error).message : null;
  });

  protected readonly loginQueryParams = computed(() => ({ returnUrl: `/evenements/${this.event().id}` }));
  protected readonly detailFormat = DETAIL_DATE_FORMAT;

  protected request(): void {
    // Le bouton est désactivé pendant l'envoi ; ce test protège d'un double clic plus rapide que le rendu.
    if (this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.clearError();
    this.api
      .request(this.event().id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.submitting.set(false);
          // set() sur la ressource : on affiche la réponse sans relire l'API.
          this.mine.set(created);
          this.changed.emit();
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          const apiError = toApiError(error);
          if (apiError.code === 'already_reserved') {
            // Double clic ou deuxième onglet (ADR 0019 § 4) : succès silencieux, on montre la demande existante.
            this.mine.reload();
            return;
          }
          this.errorMessage.set(apiError.message);
          if (EVENT_CHANGED_CODES.has(apiError.code)) {
            this.changed.emit();
          }
        },
      });
  }

  protected cancel(reservation: MyReservationDto): void {
    if (this.submitting()) {
      return;
    }
    this.clearError();
    this.submitting.set(true);
    this.cancellation
      .cancel(reservation)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((outcome) => {
        this.submitting.set(false);
        if (outcome.kind === 'cancelled') {
          this.mine.set(outcome.reservation);
          this.notifications.show(outcome.message);
          this.changed.emit();
        } else if (outcome.kind === 'failed') {
          this.errorMessage.set(outcome.error.message);
          this.contactAfterError.set(outcome.error.code === 'cancellation_deadline_passed');
          // L'état réel a changé (expirée, confirmée entre-temps…) : on le relit.
          this.mine.reload();
          this.changed.emit();
        }
      });
  }

  private clearError(): void {
    this.errorMessage.set(null);
    this.contactAfterError.set(false);
  }
}
