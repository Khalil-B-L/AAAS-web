import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { Observable, Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { AdminEventsApi } from '../../../../core/api/admin-events.api';
import { ApiError, toApiError } from '../../../../core/errors/api-error';
import { applyServerErrors } from '../../../../core/errors/apply-server-errors';
import { AdminEventDto, ApiWarning } from '../../../../core/models/api-models';
import { NotificationService } from '../../../../core/notifications/notification.service';
import { DETAIL_DATE_FORMAT } from '../../../../core/time/date-formats';
import { NotFound } from '../../../../layout/not-found';
import { adminStatusLabels } from '../../../../shared/events/event-labels';
import { FORM_FIELD_DEFAULTS } from '../../../../shared/forms/form-field-defaults';
import { isGuid } from '../../../../shared/ids';
import { FieldErrors, GroupErrorStateMatcher } from '../../../../shared/forms/field-errors';
import { HasUnsavedChanges } from '../../../../shared/forms/unsaved-changes.guard';
import { END_BEFORE_START_MESSAGE } from '../../../../shared/forms/validation-messages';
import { capacityValidator, endAfterStart, priceValidator } from '../../../../shared/forms/validators';
import { ConfirmDialogService } from '../../../../shared/ui/confirm-dialog';
import { Loading } from '../../../../shared/ui/loading';
import { EMPTY_EVENT_FORM, toEventFormValue, toEventInput } from './event-form-mapping';

type LoadState = 'loading' | 'ready' | 'not-found' | 'error';

type LoadResult =
  | { kind: 'new' }
  | { kind: 'loaded'; event: AdminEventDto }
  | { kind: 'not-found' }
  | { kind: 'failed'; error: ApiError };

/** Codes 409 qui signifient « l'état a changé côté serveur » : message, puis on recharge la fiche (ADR 0016 § 2). */
const STATE_CHANGED_CODES = new Set([
  'event_cancelled',
  'event_ended',
  'event_started',
  'event_state_changed',
  'event_already_published',
  'event_has_active_reservations',
]);

export const CONFLICT_TITLE = 'Événement modifié par un autre administrateur';
export const CONFLICT_MESSAGE =
  "Cet événement vient d'être modifié. Rechargez la page pour voir la dernière version. Vos modifications seront perdues.";

/**
 * Création et modification d'un événement (US-E2). Une seule page pour /admin/evenements/nouveau et /:id.
 * La `version` de la fiche vit dans un signal, remplacée après CHAQUE réponse qui renvoie la fiche
 * (enregistrer, publier, dépublier) : l'oublier provoquerait un faux 409 concurrent_update (ADR 0016 § 4).
 */
@Component({
  selector: 'app-admin-event-form',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    FieldErrors,
    Loading,
    NotFound,
  ],
  providers: [FORM_FIELD_DEFAULTS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-event-form.html',
  styleUrl: './admin-event-form.scss',
  // Fermeture ou rechargement de l'onglet avec des modifications : le navigateur demande confirmation.
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
})
export class AdminEventForm implements HasUnsavedChanges {
  private readonly api = inject(AdminEventsApi);
  private readonly router = inject(Router);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  /** :id de la route ; absent sur /nouveau. */
  readonly id = input<string>();

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      title: [EMPTY_EVENT_FORM.title, [Validators.required, Validators.maxLength(200)]],
      description: [EMPTY_EVENT_FORM.description, [Validators.required, Validators.maxLength(4000)]],
      location: [EMPTY_EVENT_FORM.location, [Validators.required, Validators.maxLength(200)]],
      startsAt: [EMPTY_EVENT_FORM.startsAt, [Validators.required]],
      endsAt: [EMPTY_EVENT_FORM.endsAt, [Validators.required]],
      capacity: [EMPTY_EVENT_FORM.capacity, [Validators.required, capacityValidator]],
      price: [EMPTY_EVENT_FORM.price, [priceValidator]],
    },
    // Validateur de groupe : il compare deux champs.
    { validators: [endAfterStart] },
  );

  protected readonly event = signal<AdminEventDto | null>(null);
  protected readonly version = signal<string | null>(null);
  protected readonly loadState = signal<LoadState>('ready');
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly warnings = signal<ApiWarning[]>([]);

  // form.dirty n'est pas un signal : on dérive un signal du flux d'événements du formulaire pour que
  // les boutons Publier / Dépublier / Supprimer se (dés)activent en zoneless (UX-7).
  private readonly formEvents = toSignal(this.form.events);
  protected readonly dirty = computed(() => {
    this.formEvents();
    return this.form.dirty;
  });

  protected readonly isNew = computed(() => !this.id());
  protected readonly readOnly = computed(() => {
    const event = this.event();
    return !!event && (event.status === 'Cancelled' || event.hasEnded);
  });
  protected readonly statusLabels = computed(() => {
    const event = this.event();
    return event ? adminStatusLabels(event) : [];
  });
  protected readonly canPublish = computed(() => {
    const event = this.event();
    return !!event && event.status === 'Draft' && !event.hasStarted;
  });
  protected readonly canUnpublish = computed(() => this.event()?.status === 'Published');
  protected readonly canDelete = computed(() => this.event()?.canBeDeleted ?? false);

  protected readonly endMatcher = new GroupErrorStateMatcher('endBeforeStart');
  protected readonly endBeforeStartMessage = END_BEFORE_START_MESSAGE;
  protected readonly detailFormat = DETAIL_DATE_FORMAT;

  /** « Recharger » (409, Réessayer) : relance le chargement de la fiche courante. */
  private readonly reloadRequests = new Subject<void>();

  constructor() {
    // Le composant est réutilisé si l'on passe d'une fiche à une autre (même route) : on suit le signal id.
    // toObservable : signal → flux RxJS, pour profiter de switchMap.
    toObservable(this.id)
      .pipe(
        // Chaque fiche : un chargement tout de suite, puis un à chaque demande de rechargement.
        switchMap((id) => this.reloadRequests.pipe(startWith(undefined), map(() => id))),
        // switchMap DÉSABONNE la requête précédente (HttpClient l'annule) : la réponse tardive de la fiche A
        // ne peut plus écraser la fiche B, ni un ancien rechargement le plus récent.
        switchMap((id) => this.fetch(id)),
        // Composant détruit (navigation ailleurs) : requête en cours annulée, plus aucun effet sur le formulaire.
        takeUntilDestroyed(),
      )
      .subscribe((result) => this.applyLoadResult(result));
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty && !this.readOnly();
  }

  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  protected save(confirmRegistrantsWarning = false): void {
    if (this.busy() || this.readOnly()) {
      return;
    }
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }
    const body = toEventInput(this.form.getRawValue());
    this.startRequest();

    const id = this.id();
    if (!id) {
      this.api.create(body).subscribe({
        next: (created) => {
          this.notifications.show('Enregistré.');
          if (this.isStale(id)) {
            this.busy.set(false);
            return; // l'admin est parti ailleurs : ne pas le ramener de force sur la nouvelle fiche
          }
          this.form.markAsPristine(); // sinon la garde de sortie demanderait confirmation
          void this.router.navigate(['/admin/evenements', created.id]);
        },
        error: (error: unknown) => this.handleWriteError(error, id),
      });
      return;
    }

    this.api
      .update(id, { ...body, version: this.version() ?? '', confirmRegistrantsWarning })
      .subscribe({
        next: (updated) => {
          this.busy.set(false);
          this.notifications.show('Enregistré.');
          if (!this.isStale(id)) {
            this.applyEvent(updated);
          }
        },
        // R12 : après confirmation, renvoi de la MÊME requête (même version) avec le drapeau.
        error: (error: unknown) => this.handleWriteError(error, id, () => this.save(true)),
      });
  }

  protected publish(): void {
    const id = this.id();
    if (!id || this.busy() || this.dirty()) {
      return;
    }
    this.startRequest();
    this.api.publish(id).subscribe({
      next: (response) => {
        this.busy.set(false);
        this.notifications.show("L'événement est publié.");
        if (!this.isStale(id)) {
          this.applyEvent(response.event);
          // Avertissement non bloquant (contact_not_configured) : message persistant sous le titre.
          this.warnings.set(response.warnings);
        }
      },
      error: (error: unknown) => this.handleWriteError(error, id),
    });
  }

  protected unpublish(): void {
    const id = this.id();
    if (!id || this.busy() || this.dirty()) {
      return;
    }
    this.startRequest();
    this.api.unpublish(id).subscribe({
      next: (event) => {
        this.busy.set(false);
        this.notifications.show("L'événement n'est plus publié.");
        if (!this.isStale(id)) {
          this.applyEvent(event);
        }
      },
      error: (error: unknown) => this.handleWriteError(error, id),
    });
  }

  protected remove(): void {
    const id = this.id();
    if (!id || this.busy() || this.dirty()) {
      return;
    }
    this.confirmDialog
      .confirm({
        title: 'Supprimer ce brouillon ?',
        message: 'Cette action est définitive.',
        confirmLabel: 'Supprimer',
        cancelLabel: 'Annuler',
        danger: true,
      })
      .subscribe((confirmed) => {
        if (!confirmed || this.isStale(id)) {
          return;
        }
        this.startRequest();
        this.api.delete(id).subscribe({
          next: () => {
            this.notifications.show('Le brouillon a été supprimé.');
            if (this.isStale(id)) {
              this.busy.set(false);
              return;
            }
            void this.router.navigate(['/admin/evenements']);
          },
          error: (error: unknown) => this.handleWriteError(error, id),
        });
      });
  }

  protected reload(): void {
    if (this.id()) {
      this.reloadRequests.next();
    }
  }

  private fetch(id: string | undefined): Observable<LoadResult> {
    if (!id) {
      return of({ kind: 'new' });
    }
    // Comme la page publique (event-detail.ts) : /admin/evenements/abc n'appelle pas l'API.
    if (!isGuid(id)) {
      return of({ kind: 'not-found' });
    }
    this.loadState.set('loading');
    return this.api.get(id).pipe(
      map((event): LoadResult => ({ kind: 'loaded', event })),
      // L'erreur devient une valeur : une erreur dans le flux principal le terminerait (plus de rechargement possible).
      catchError((error: unknown) => of<LoadResult>({ kind: 'failed', error: toApiError(error) })),
    );
  }

  private applyLoadResult(result: LoadResult): void {
    switch (result.kind) {
      case 'new':
        this.applyEvent(null);
        this.loadState.set('ready');
        break;
      case 'loaded':
        this.applyEvent(result.event);
        this.loadState.set('ready');
        break;
      case 'not-found':
        this.loadState.set('not-found');
        break;
      case 'failed':
        this.loadError.set(result.error.message);
        this.loadState.set(result.error.code === 'not_found' ? 'not-found' : 'error');
        break;
    }
  }

  /** Remplace la fiche, la version et le formulaire par la réponse du serveur (formulaire « propre »). */
  private applyEvent(event: AdminEventDto | null): void {
    this.event.set(event);
    this.version.set(event?.version ?? null);
    // reset() : nouvelles valeurs, et le formulaire redevient pristine et untouched.
    this.form.reset(event ? toEventFormValue(event) : EMPTY_EVENT_FORM);
    if (event && (event.status === 'Cancelled' || event.hasEnded)) {
      this.form.disable();
    } else {
      this.form.enable();
    }
  }

  private startRequest(): void {
    this.busy.set(true);
    this.errorMessage.set(null);
    this.warnings.set([]);
  }

  /**
   * L'écran ne montre plus la fiche `id` (composant détruit, ou autre :id sur la même route) ?
   * Les écritures ne sont volontairement PAS annulées (pas de takeUntilDestroyed) : le serveur a peut-être déjà
   * appliqué la modification, et la couper côté client ne la déferait pas. On ignore seulement ses effets locaux
   * (formulaire, navigation, dialogue) ; la notification globale, vraie quel que soit l'écran, est conservée.
   */
  private isStale(id: string | undefined): boolean {
    return this.destroyRef.destroyed || this.id() !== id;
  }

  /** Réaction selon le `code` de l'API, jamais selon le texte (tableau de l'ADR 0016 § 2). */
  private handleWriteError(error: unknown, id: string | undefined, retryWithConfirmation?: () => void): void {
    const apiError = toApiError(error);
    this.busy.set(false);

    if (this.isStale(id)) {
      // Pas de dialogue ni de message sur une autre fiche ; l'échec reste signalé par la notification globale.
      this.notifications.show(apiError.message, 'warning');
      return;
    }

    if (apiError.code === 'validation_failed') {
      const unmatched = applyServerErrors(this.form, apiError);
      this.errorMessage.set([apiError.message, ...unmatched].join(' '));
    } else if (apiError.code === 'registrants_warning' && retryWithConfirmation) {
      this.confirmDialog
        .confirm({
          title: 'Des personnes sont inscrites',
          message: apiError.message,
          confirmLabel: 'Confirmer',
          cancelLabel: 'Annuler',
        })
        .subscribe((confirmed) => confirmed && !this.isStale(id) && retryWithConfirmation());
    } else if (apiError.code === 'concurrent_update') {
      this.confirmDialog
        .confirm({ title: CONFLICT_TITLE, message: CONFLICT_MESSAGE, confirmLabel: 'Recharger', cancelLabel: 'Fermer' })
        .subscribe((reload) => {
          if (this.isStale(id)) {
            return;
          }
          if (reload) {
            this.reload();
          } else {
            // « Fermer » : la saisie reste affichée pour être recopiée (UX-6).
            this.errorMessage.set(CONFLICT_MESSAGE);
          }
        });
    } else if (apiError.code === 'not_found') {
      this.loadState.set('not-found');
    } else if (STATE_CHANGED_CODES.has(apiError.code)) {
      this.errorMessage.set(apiError.message);
      this.reload();
    } else {
      this.errorMessage.set(apiError.message);
    }
  }
}
