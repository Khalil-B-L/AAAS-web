import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { Footer } from './layout/footer';
import { Header } from './layout/header';
import { NotificationHost } from './layout/notification-host';

/** Coquille de l'application : en-tête, page courante (<router-outlet>), pied de page, messages. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Header, Footer, NotificationHost],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');
  private readonly injector = inject(Injector);

  constructor() {
    // Dans une SPA, changer de page ne recharge rien : sans aide, le focus reste sur le lien cliqué (souvent
    // dans le menu) et le lecteur d'écran n'annonce pas la nouvelle page. On le place sur son titre.
    let previousPath: string | null = null;
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((event) => {
        const path = event.urlAfterRedirects.split(/[?#]/)[0];
        const isFirstPage = previousPath === null;
        const pathChanged = path !== previousPath;
        previousPath = path;
        // Premier affichage : le focus reste en haut du document (le lien d'évitement est le premier Tab).
        // Seuls les paramètres changent (?page=2, filtres admin) : on ne vole pas le focus du champ ou du bouton.
        if (!isFirstPage && pathChanged) {
          // La nouvelle page n'est dessinée qu'au prochain rendu : son <h1> n'existe pas encore ici.
          afterNextRender(() => this.focusPageContent(), { injector: this.injector });
        }
      });
  }

  /** Lien « Aller au contenu ». */
  protected skipToContent(event: Event): void {
    event.preventDefault();
    this.main().nativeElement.focus();
  }

  private focusPageContent(): void {
    const main = this.main().nativeElement;
    const title = main.querySelector('h1');
    if (title) {
      // Un <h1> n'est pas focusable par défaut ; tabindex -1 le rend focusable par programme seulement.
      title.setAttribute('tabindex', '-1');
      // preventScroll : le défilement est déjà géré par withInMemoryScrolling (haut de page, ou position restaurée).
      title.focus({ preventScroll: true });
    } else {
      main.focus({ preventScroll: true });
    }
  }
}
