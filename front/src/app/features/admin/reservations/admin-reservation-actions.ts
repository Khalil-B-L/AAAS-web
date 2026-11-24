import { Injectable, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable, Subscription, catchError, map, of, switchMap } from 'rxjs';
import { AdminReservationAction, AdminReservationsApi } from '../../../core/api/admin-reservations.api';
import { ApiError, toApiError } from '../../../core/errors/api-error';
import { AdminReservationDto } from '../../../core/models/api-models';
import { ConfirmDialogService } from '../../../shared/ui/confirm-dialog';
import { openRefuseDialog } from './refuse-dialog';

export type AdminActionOutcome =
  | { kind: 'dismissed' }
  | { kind: 'done'; reservation: AdminReservationDto; message: string }
  | { kind: 'failed'; error: ApiError };

// Libellés : docs/fonctionnel/libelles-ecrans.md § 10.
const SUCCESS: Record<AdminReservationAction, string> = {
  confirm: 'Demande confirmée.',
  refuse: 'Demande refusée.',
  cancel: 'Réservation annulée.',
  'mark-paid': 'Mention « Payée » enregistrée.',
  'unmark-paid': 'Mention « Payée » retirée.',
};

const CANCEL_MESSAGE =
  "La place sera libérée. L'association n'envoie pas de message : pensez à prévenir la personne par téléphone.";
const CANCEL_PAID_NOTE = "Cette réservation est marquée payée : le remboursement éventuel se règle hors de l'application.";

/**
 * Actions de l'admin sur une réservation, communes à « Demandes à traiter » et « Inscrits » : dialogue éventuel
 * (refus, annulation), appel HTTP, puis un résultat typé. Les erreurs ne sont jamais levées : elles deviennent
 * { kind: 'failed' } (même schéma que ReservationCancellation côté membre).
 */
@Injectable({ providedIn: 'root' })
export class AdminReservationActions {
  private readonly api = inject(AdminReservationsApi);
  private readonly dialog = inject(MatDialog);
  private readonly confirmDialog = inject(ConfirmDialogService);

  run(reservation: AdminReservationDto, action: AdminReservationAction): Observable<AdminActionOutcome> {
    // of(...) puis switchMap : « si l'admin valide le dialogue, alors appeler l'API » (≈ await dialog; await http).
    return this.askFirst(reservation, action).pipe(
      switchMap((answer): Observable<AdminActionOutcome> => {
        if (answer === null) {
          return of({ kind: 'dismissed' });
        }
        return this.api.act(reservation.id, action, answer.reason).pipe(
          map((updated): AdminActionOutcome => ({ kind: 'done', reservation: updated, message: SUCCESS[action] })),
          catchError((error: unknown) => of<AdminActionOutcome>({ kind: 'failed', error: toApiError(error) })),
        );
      }),
    );
  }

  /** null = l'admin a fermé le dialogue ; sinon le motif (refus seulement). */
  private askFirst(reservation: AdminReservationDto, action: AdminReservationAction): Observable<{ reason: string | null } | null> {
    if (action === 'refuse') {
      return openRefuseDialog(this.dialog).pipe(map((result) => result ?? null));
    }
    if (action === 'cancel') {
      return this.confirmDialog
        .confirm({
          title: 'Annuler cette réservation ?',
          message: reservation.isPaid ? `${CANCEL_MESSAGE} ${CANCEL_PAID_NOTE}` : CANCEL_MESSAGE,
          confirmLabel: 'Oui, annuler',
          cancelLabel: 'Fermer',
          danger: true,
        })
        .pipe(map((confirmed) => (confirmed ? { reason: null } : null)));
    }
    return of({ reason: null });
  }
}

/**
 * Nombre de demandes en attente, pour « Demandes (N) » du sous-menu admin. Service singleton (providedIn: 'root')
 * qui garde un signal : le menu et les pages lisent la même valeur ; refresh() après chaque action.
 * pageSize = 1 : l'API renvoie totalCount sans qu'on télécharge la liste.
 */
@Injectable({ providedIn: 'root' })
export class PendingCountStore {
  private readonly api = inject(AdminReservationsApi);
  private readonly state = signal<number | null>(null);
  private request?: Subscription;

  readonly count = this.state.asReadonly();

  refresh(): void {
    this.request?.unsubscribe(); // une seule requête à la fois : la plus récente gagne
    this.request = this.api.pending(1, 1).subscribe({
      next: (page) => this.state.set(page.totalCount),
      // Compteur indicatif : en cas d'erreur, le menu affiche « Demandes » sans nombre.
      error: () => this.state.set(null),
    });
  }
}
