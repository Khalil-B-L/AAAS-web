import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { Observable, Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { AdminNewsApi } from '../../../../core/api/admin-news.api';
import { ApiError, toApiError } from '../../../../core/errors/api-error';
import { applyServerErrors } from '../../../../core/errors/apply-server-errors';
import { AdminNewsDto } from '../../../../core/models/api-models';
import { NotificationService } from '../../../../core/notifications/notification.service';
import { DETAIL_DATE_FORMAT } from '../../../../core/time/date-formats';
import { FieldErrors } from '../../../../shared/forms/field-errors';
import { FORM_FIELD_DEFAULTS } from '../../../../shared/forms/form-field-defaults';
import { HasUnsavedChanges } from '../../../../shared/forms/unsaved-changes.guard';
import { trimmedText } from '../../../../shared/forms/validators';
import { isGuid } from '../../../../shared/ids';
import { NewsArticle } from '../../../../shared/news/news-article';
import {
  NEWS_CONTENT_MAX_LENGTH,
  NEWS_STATUS_LABELS,
  NEWS_TITLE_MAX_LENGTH,
  newsContentLength,
} from '../../../../shared/news/news-labels';
import { ConfirmDialogService } from '../../../../shared/ui/confirm-dialog';
import { Loading } from '../../../../shared/ui/loading';

type LoadState = 'loading' | 'ready' | 'not-found' | 'error';

type LoadResult =
  | { kind: 'new' }
  | { kind: 'loaded'; news: AdminNewsDto }
  | { kind: 'not-found' }
  | { kind: 'failed'; error: ApiError };

// Libellés : docs/fonctionnel/libelles-ecrans.md §§ 13 et 14.
export const NEWS_CONFLICT_TITLE = 'Actualité modifiée par un autre administrateur';
export const NEWS_CONFLICT_MESSAGE =
  "Cette actualité vient d'être modifiée. Rechargez la page pour voir la dernière version. Vos modifications seront perdues.";
export const ALREADY_DELETED_MESSAGE = 'Cette actualité a déjà été supprimée.';

/** Sous ce nombre de caractères restants, le compteur est annoncé aux lecteurs d'écran (pas à chaque frappe avant). */
const ANNOUNCE_REMAINING_BELOW = 200;

const EMPTY_NEWS_FORM = { title: '', content: '' };

/**
 * Création et modification d'une actualité (US-E1), pour /admin/actualites/nouvelle et /:id.
 * Même organisation que le formulaire des événements : la `version` vit dans un signal remplacé après chaque réponse
 * qui renvoie la fiche (ADR 0016 § 4) ; chargement par toObservable + switchMap (une fiche chasse la précédente).
 */
@Component({
  selector: 'app-admin-news-form',
  imports: [
    DatePipe,
    DecimalPipe,
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    FieldErrors,
    Loading,
    NewsArticle,
  ],
  providers: [FORM_FIELD_DEFAULTS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-news-form.html',
  styleUrl: './admin-news-form.scss',
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
})
export class AdminNewsForm implements HasUnsavedChanges {
  private readonly api = inject(AdminNewsApi);
  private readonly router = inject(Router);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  /** :id de la route ; absent sur /nouvelle. */
  readonly id = input<string>();

  protected readonly form = inject(NonNullableFormBuilder).group({
    title: [EMPTY_NEWS_FORM.title, [trimmedText(NEWS_TITLE_MAX_LENGTH)]],
    content: [EMPTY_NEWS_FORM.content, [trimmedText(NEWS_CONTENT_MAX_LENGTH)]],
  });

  protected readonly news = signal<AdminNewsDto | null>(null);
  protected readonly version = signal<string | null>(null);
  protected readonly loadState = signal<LoadState>('ready');
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Bascule « Modifier » / « Aperçu ». */
  protected readonly preview = signal(false);

  // toSignal(valueChanges) : le flux des valeurs du formulaire devient un signal. L'aperçu et les compteurs le
  // lisent, donc se redessinent à chaque frappe (OnPush + signals : rien d'autre à déclencher).
  protected readonly values = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), {
    initialValue: this.form.getRawValue(),
  });

  // form.dirty n'est pas un signal : dérivé du flux d'événements du formulaire (UX-7).
  private readonly formEvents = toSignal(this.form.events);
  protected readonly dirty = computed(() => {
    this.formEvents();
    return this.form.dirty;
  });

  protected readonly isNew = computed(() => !this.id());
  protected readonly isPublished = computed(() => this.news()?.status === 'Published');
  protected readonly statusLabel = computed(() => {
    const news = this.news();
    return news ? NEWS_STATUS_LABELS[news.status] : null;
  });

  /** Même décompte que l'API (après Trim et \r\n → \n) : le compteur ne contredit jamais le message d'erreur. */
  protected readonly titleLength = computed(() => newsContentLength(this.values().title));
  protected readonly contentLength = computed(() => newsContentLength(this.values().content));
  protected readonly contentRemaining = computed(() => NEWS_CONTENT_MAX_LENGTH - this.contentLength());
  protected readonly announceRemaining = computed(() => this.contentRemaining() < ANNOUNCE_REMAINING_BELOW);

  protected readonly titleMax = NEWS_TITLE_MAX_LENGTH;
  protected readonly contentMax = NEWS_CONTENT_MAX_LENGTH;
  protected readonly detailFormat = DETAIL_DATE_FORMAT;

  private readonly reloadRequests = new Subject<void>();

  constructor() {
    toObservable(this.id)
      .pipe(
        switchMap((id) => this.reloadRequests.pipe(startWith(undefined), map(() => id))),
        // switchMap désabonne la requête précédente : la réponse tardive d'une fiche ne peut pas écraser la suivante.
        switchMap((id) => this.fetch(id)),
        takeUntilDestroyed(),
      )
      .subscribe((result) => this.applyLoadResult(result));
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  protected save(): void {
    if (this.busy()) {
      return;
    }
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.preview.set(false); // les erreurs sont sous les champs : on y revient
      return;
    }
    const body = this.form.getRawValue();
    this.startRequest();

    const id = this.id();
    if (!id) {
      this.api.create(body).subscribe({
        next: (created) => {
          this.notifications.show('Enregistré.');
          if (this.isStale(id)) {
            this.busy.set(false);
            return;
          }
          this.form.markAsPristine();
          // replaceUrl : « Précédent » ne ramène pas sur un formulaire de création vide.
          void this.router.navigate(['/admin/actualites', created.id], { replaceUrl: true });
        },
        error: (error: unknown) => this.handleWriteError(error, id),
      });
      return;
    }

    this.api.update(id, { ...body, version: this.version() ?? '' }).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.notifications.show('Enregistré.');
        if (!this.isStale(id)) {
          this.applyNews(updated);
        }
      },
      error: (error: unknown) => this.handleWriteError(error, id),
    });
  }

  protected publish(): void {
    this.runAction((id) => this.api.publish(id), "L'actualité est publiée.");
  }

  protected unpublish(): void {
    this.runAction((id) => this.api.unpublish(id), "L'actualité n'est plus publiée.");
  }

  protected remove(): void {
    const id = this.id();
    if (!id || this.busy() || this.dirty()) {
      return;
    }
    const message = this.isPublished()
      ? 'Cette action est définitive. Elle n’est plus visible du public dès maintenant.'
      : 'Cette action est définitive.';
    this.confirmDialog
      .confirm({
        title: 'Supprimer définitivement cette actualité ?',
        message,
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
          next: () => this.leaveAfterDeletion(id, "L'actualité a été supprimée."),
          error: (error: unknown) => {
            if (toApiError(error).code === 'not_found') {
              this.leaveAfterDeletion(id, ALREADY_DELETED_MESSAGE);
            } else {
              this.handleWriteError(error, id);
            }
          },
        });
      });
  }

  protected reload(): void {
    if (this.id()) {
      this.reloadRequests.next();
    }
  }

  private runAction(call: (id: string) => Observable<AdminNewsDto>, successMessage: string): void {
    const id = this.id();
    if (!id || this.busy() || this.dirty()) {
      return;
    }
    this.startRequest();
    call(id).subscribe({
      next: (news) => {
        this.busy.set(false);
        this.notifications.show(successMessage);
        if (!this.isStale(id)) {
          this.applyNews(news);
        }
      },
      error: (error: unknown) => this.handleWriteError(error, id),
    });
  }

  private leaveAfterDeletion(id: string, message: string): void {
    this.notifications.show(message);
    this.busy.set(false);
    if (this.isStale(id)) {
      return;
    }
    this.form.markAsPristine(); // pas de « Quitter sans enregistrer ? » après une suppression
    void this.router.navigate(['/admin/actualites']);
  }

  private fetch(id: string | undefined): Observable<LoadResult> {
    if (!id) {
      return of({ kind: 'new' });
    }
    if (!isGuid(id)) {
      return of({ kind: 'not-found' });
    }
    this.loadState.set('loading');
    return this.api.get(id).pipe(
      map((news): LoadResult => ({ kind: 'loaded', news })),
      catchError((error: unknown) => of<LoadResult>({ kind: 'failed', error: toApiError(error) })),
    );
  }

  private applyLoadResult(result: LoadResult): void {
    switch (result.kind) {
      case 'new':
        this.applyNews(null);
        this.loadState.set('ready');
        break;
      case 'loaded':
        this.applyNews(result.news);
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

  private applyNews(news: AdminNewsDto | null): void {
    this.news.set(news);
    this.version.set(news?.version ?? null);
    this.form.reset(news ? { title: news.title, content: news.content } : EMPTY_NEWS_FORM);
  }

  private startRequest(): void {
    this.busy.set(true);
    this.errorMessage.set(null);
  }

  /** L'écran ne montre plus la fiche `id` (détruit, ou autre :id) : on ignore les effets locaux de la réponse. */
  private isStale(id: string | undefined): boolean {
    return this.destroyRef.destroyed || this.id() !== id;
  }

  /** Réaction selon le `code` de l'API, jamais selon le texte (ADR 0016 § 2, libellés § 14). */
  private handleWriteError(error: unknown, id: string | undefined): void {
    const apiError = toApiError(error);
    this.busy.set(false);

    if (this.isStale(id)) {
      this.notifications.show(apiError.message, 'warning');
      return;
    }

    if (apiError.code === 'validation_failed') {
      this.preview.set(false);
      const unmatched = applyServerErrors(this.form, apiError);
      this.errorMessage.set([apiError.message, ...unmatched].join(' '));
    } else if (apiError.code === 'concurrent_update') {
      // Même dialogue pour Enregistrer, Publier, Dépublier et Supprimer (libellés § 14).
      this.confirmDialog
        .confirm({
          title: NEWS_CONFLICT_TITLE,
          message: NEWS_CONFLICT_MESSAGE,
          confirmLabel: 'Recharger',
          cancelLabel: 'Fermer',
        })
        .subscribe((reload) => {
          if (this.isStale(id)) {
            return;
          }
          if (reload) {
            this.reload();
          } else {
            this.errorMessage.set(NEWS_CONFLICT_MESSAGE); // la saisie reste affichée pour être recopiée
          }
        });
    } else if (apiError.code === 'not_found') {
      this.loadState.set('not-found');
    } else if (apiError.code === 'news_state_changed') {
      this.errorMessage.set(apiError.message);
      this.reload();
    } else {
      this.errorMessage.set(apiError.message);
    }
  }
}
