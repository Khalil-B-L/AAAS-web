import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { IsoDateString } from '../../core/models/api-models';
import { DAY_FORMAT } from '../../core/time/date-formats';
import { toParagraphs } from './paragraphs';

/**
 * Rendu d'une actualité : titre, date de publication, texte en paragraphes. Composant « de présentation » (il ne
 * fait aucun appel) partagé par le détail public ET l'aperçu du formulaire admin : même rendu garanti (ADR 0023).
 *
 * Sécurité : le titre et le texte sont affichés UNIQUEMENT par interpolation {{ }}. Angular crée alors des nœuds
 * texte : « <script> » s'affiche à l'écran au lieu d'être interprété. Jamais de [innerHTML] ici (règle ESLint).
 */
@Component({
  selector: 'app-news-article',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="news-article">
      @if (headingLevel() === 1) {
        <h1>{{ title() }}</h1>
      } @else {
        <h2>{{ title() }}</h2>
      }
      <p class="news-article__date muted">
        @if (publishedAt(); as date) {
          <!-- datetime : la date machine (UTC) ; le texte : le jour à Tunis (DatePipe réglé sur +0100). -->
          Publiée le <time [attr.datetime]="date">{{ date | date: dayFormat }}</time>
        } @else {
          Brouillon, non publié
        }
      </p>
      <!-- track $index : les paragraphes n'ont pas d'identifiant ; l'ordre suffit (liste recalculée à chaque saisie). -->
      @for (paragraph of paragraphs(); track $index) {
        <p class="news-article__paragraph">{{ paragraph }}</p>
      }
    </article>
  `,
  styles: `
    .news-article__paragraph {
      // Garde les retours à la ligne simples à l'intérieur d'un paragraphe, sans HTML.
      white-space: pre-line;
      overflow-wrap: anywhere;
    }
  `,
})
export class NewsArticle {
  readonly title = input.required<string>();
  readonly content = input.required<string>();
  /** null : pas encore publiée (aperçu d'un brouillon). */
  readonly publishedAt = input<IsoDateString | null>(null);
  /** 1 sur la page de détail ; 2 dans l'aperçu admin (la page a déjà son h1). */
  readonly headingLevel = input<1 | 2>(1);

  protected readonly paragraphs = computed(() => toParagraphs(this.content()));
  protected readonly dayFormat = DAY_FORMAT;
}
