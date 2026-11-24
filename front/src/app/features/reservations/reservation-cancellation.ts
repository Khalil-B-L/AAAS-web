import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, switchMap } from 'rxjs';
import { ReservationsApi } from '../../core/api/reservations.api';
import { ApiError, toApiError } from '../../core/errors/api-error';
import { MyReservationDto } from '../../core/models/api-models';
import { ConfirmDialogService } from '../../shared/ui/confirm-dialog';

export type CancelOutcome =
  | { kind: 'dismissed' }
  | { kind: 'cancelled'; reservation: MyReservationDto; message: string }
  | { kind: 'failed'; error: ApiError };

const PAID_NOTE =
  "Cette réservation est marquée payée : le remboursement éventuel se règle directement avec l'association.";

/**
 * Annulation par le membre (US-D3), commune au panneau du détail et à « Mes réservations » :
 * dialogue de confirmation (libelles-ecrans.md § 8), appel, puis un résultat que l'écran affiche à sa façon.
 * Les erreurs ne sont jamais levées : elles deviennent { kind: 'failed' } (un seul chemin à tester par écran).
 */
@Injectable({ providedIn: 'root' })
export class ReservationCancellation {
  private readonly api = inject(ReservationsApi);
  private readonly dialog = inject(ConfirmDialogService);

  cancel(reservation: MyReservationDto): Observable<CancelOutcome> {
    const isRequest = reservation.status === 'Pending';
    const message = reservation.isPaid ? `Votre place sera libérée. ${PAID_NOTE}` : 'Votre place sera libérée.';
    return this.dialog
      .confirm({
        title: isRequest ? 'Annuler cette demande ?' : 'Annuler cette réservation ?',
        message,
        confirmLabel: 'Oui, annuler',
        cancelLabel: 'Non, garder ma place',
        danger: true,
      })
      .pipe(
        // switchMap : on enchaîne l'appel HTTP seulement si le dialogue est confirmé (≈ await dans une méthode async).
        switchMap((confirmed): Observable<CancelOutcome> => {
          if (!confirmed) {
            return of({ kind: 'dismissed' });
          }
          return this.api.cancel(reservation.id).pipe(
            map(
              (cancelled): CancelOutcome => ({
                kind: 'cancelled',
                reservation: cancelled,
                message: isRequest ? 'Votre demande est annulée.' : 'Votre réservation est annulée.',
              }),
            ),
            catchError((error: unknown) => of<CancelOutcome>({ kind: 'failed', error: toApiError(error) })),
          );
        }),
      );
  }
}
