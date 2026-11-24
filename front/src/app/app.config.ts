import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import {
  TitleStrategy,
  provideRouter,
  withComponentInputBinding,
  withInMemoryScrolling,
} from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AppTitleStrategy } from './core/config/app-title.strategy';
import { FORMATTING_PROVIDERS } from './core/config/formatting.providers';

// Équivalent du Program.cs : la liste des services (« providers ») de l'application.
// Zoneless (pas de zone.js) est le défaut depuis Angular 21 : rien à déclarer.
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      // Paramètres de route (:id) et de requête (?page=2) reçus directement dans les input() des pages.
      withComponentInputBinding(),
      // Retour en haut de page à chaque navigation (y compris changement de ?page=), position restaurée sur « Précédent ».
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled' }),
    ),
    // HttpClient (moteur fetch par défaut en v22) + intercepteur qui ajoute le jeton (ADR 0015).
    provideHttpClient(withInterceptors([authInterceptor])),
    // Locale « fr » et dates à l'heure de Tunis pour tous les DatePipe.
    ...FORMATTING_PROVIDERS,
    // Titre d'onglet « Page — Association ».
    { provide: TitleStrategy, useClass: AppTitleStrategy },
    // Options des champs Material : voir shared/forms/form-field-defaults.ts (volontairement pas ici).
  ],
};
