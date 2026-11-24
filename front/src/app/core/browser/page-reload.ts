import { DOCUMENT, InjectionToken, inject } from '@angular/core';

/**
 * Recharge complète de la page. Derrière un jeton d'injection (≈ une interface enregistrée dans le conteneur .NET)
 * pour être remplacée par une doublure en test : jsdom ne permet pas d'espionner location.reload.
 */
export const PAGE_RELOAD = new InjectionToken<() => void>('PAGE_RELOAD', {
  providedIn: 'root',
  factory: () => {
    const location = inject(DOCUMENT).location;
    return () => location.reload();
  },
});
