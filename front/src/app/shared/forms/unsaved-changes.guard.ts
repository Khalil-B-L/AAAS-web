import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { ConfirmDialogService } from '../ui/confirm-dialog';

/** Implémenté par les écrans qui ont un formulaire modifiable (événement, numéro de contact). */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/** UX-8 : confirmation avant de quitter un formulaire modifié et non enregistré. */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) => {
  if (!component.hasUnsavedChanges()) {
    return true;
  }
  return inject(ConfirmDialogService).confirm({
    title: 'Quitter sans enregistrer ?',
    message: 'Vos modifications ne sont pas enregistrées.',
    confirmLabel: 'Quitter la page',
    cancelLabel: 'Rester',
  });
};
