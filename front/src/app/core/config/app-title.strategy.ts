import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { APP_INFO } from './app-info';

/** « Événements — A.A.A.S » : titre d'onglet construit à partir du `title` de la route. */
export function pageTitle(title: string | undefined | null): string {
  return title ? `${title} — ${APP_INFO.name}` : APP_INFO.name;
}

/**
 * Le routeur appelle cette stratégie après chaque navigation (fournie dans app.config.ts à la place de celle
 * par défaut). Utile aussi aux lecteurs d'écran, qui annoncent le titre de la nouvelle page.
 */
@Injectable()
export class AppTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.title.setTitle(pageTitle(this.buildTitle(snapshot)));
  }
}
