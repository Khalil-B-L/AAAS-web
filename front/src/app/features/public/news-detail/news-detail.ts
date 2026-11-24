import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { NewsApi } from '../../../core/api/news.api';
import { pageTitle } from '../../../core/config/app-title.strategy';
import { toApiError } from '../../../core/errors/api-error';
import { NotFound } from '../../../layout/not-found';
import { isGuid } from '../../../shared/ids';
import { NewsArticle } from '../../../shared/news/news-article';
import { Loading } from '../../../shared/ui/loading';

/** Détail d'une actualité (US-B2). Brouillon, dépubliée, inconnue ou id mal formé : « Contenu introuvable » à la même URL. */
@Component({
  selector: 'app-news-detail',
  imports: [RouterLink, MatButtonModule, Loading, NotFound, NewsArticle],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (notFound()) {
      <app-not-found />
    } @else if (apiError(); as error) {
      <section class="page">
        <div class="banner banner--error" role="alert">
          <p>{{ error.message }}</p>
          <button mat-stroked-button type="button" (click)="news.reload()">Réessayer</button>
        </div>
      </section>
    } @else if (detail(); as n) {
      <div class="page">
        <a routerLink="/actualites">← Retour aux actualités</a>
        <app-news-article [title]="n.title" [publishedAt]="n.publishedAt" [content]="n.content" />
      </div>
    } @else {
      <app-loading />
    }
  `,
})
export class NewsDetail {
  private readonly api = inject(NewsApi);
  private readonly title = inject(Title);

  /** Paramètre de route :id (withComponentInputBinding). */
  readonly id = input.required<string>();

  protected readonly validId = computed(() => isGuid(this.id()));

  protected readonly news = rxResource({
    // undefined = ressource au repos : aucun appel pour /actualites/abc.
    params: () => (this.validId() ? { id: this.id() } : undefined),
    stream: ({ params }) => this.api.get(params.id),
  });

  protected readonly apiError = computed(() => {
    const error = this.news.error();
    return error ? toApiError(error) : null;
  });

  protected readonly notFound = computed(() => !this.validId() || this.apiError()?.code === 'not_found');
  protected readonly detail = computed(() => (this.news.hasValue() ? this.news.value() : null));

  constructor() {
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
