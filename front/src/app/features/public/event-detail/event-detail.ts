import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { EventsApi } from '../../../core/api/events.api';
import { AuthService } from '../../../core/auth/auth.service';
import { pageTitle } from '../../../core/config/app-title.strategy';
import { toApiError } from '../../../core/errors/api-error';
import { DETAIL_DATE_FORMAT, TIME_FORMAT } from '../../../core/time/date-formats';
import { toTunisLocalInput } from '../../../core/time/tunis-time';
import { eventMention } from '../../../shared/events/event-labels';
import { isGuid } from '../../../shared/ids';
import { TndPricePipe } from '../../../shared/pipes/tnd-price.pipe';
import { Loading } from '../../../shared/ui/loading';
import { NotFound } from '../../../layout/not-found';

/** Détail d'un événement (US-C2, partie visiteur). Brouillon ou inconnu : « Contenu introuvable » à la même URL. */
@Component({
  selector: 'app-event-detail',
  imports: [DatePipe, RouterLink, MatButtonModule, TndPricePipe, Loading, NotFound],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './event-detail.html',
  styleUrl: './event-detail.scss',
})
export class EventDetail {
  private readonly api = inject(EventsApi);
  protected readonly auth = inject(AuthService);
  private readonly title = inject(Title);

  /** Paramètre de route :id (withComponentInputBinding). */
  readonly id = input.required<string>();

  protected readonly validId = computed(() => isGuid(this.id()));

  protected readonly event = rxResource({
    // params qui renvoie undefined = ressource au repos : aucun appel pour un identifiant mal formé.
    params: () => (this.validId() ? { id: this.id() } : undefined),
    stream: ({ params }) => this.api.get(params.id),
  });

  protected readonly apiError = computed(() => {
    const error = this.event.error();
    return error ? toApiError(error) : null;
  });

  protected readonly notFound = computed(() => !this.validId() || this.apiError()?.code === 'not_found');

  /** Valeur sûre à lire dans le template (value() lèverait une exception en cas d'erreur). */
  protected readonly detail = computed(() => (this.event.hasValue() ? this.event.value() : null));
  protected readonly mention = computed(() => {
    const detail = this.detail();
    return detail ? eventMention(detail) : null;
  });

  /** Retour après connexion : on revient sur cet événement (US-C2). */
  protected readonly loginQueryParams = computed(() => ({ returnUrl: `/evenements/${this.id()}` }));

  protected readonly detailFormat = DETAIL_DATE_FORMAT;
  protected readonly timeFormat = TIME_FORMAT;

  /** Même jour à Tunis (pas dans le fuseau du navigateur). */
  protected sameDay(startsAt: string, endsAt: string): boolean {
    return toTunisLocalInput(startsAt).slice(0, 10) === toTunisLocalInput(endsAt).slice(0, 10);
  }

  constructor() {
    // effect : code relancé chaque fois qu'un signal lu change (ici, le titre de l'onglet suit l'événement chargé).
    effect(() => {
      const detail = this.detail();
      if (detail) {
        this.title.setTitle(pageTitle(detail.title));
      } else if (this.notFound()) {
        this.title.setTitle(pageTitle('Contenu introuvable'));
      }
    });
  }
}
