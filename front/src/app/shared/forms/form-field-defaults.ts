import { Provider } from '@angular/core';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS, MatFormFieldDefaultOptions } from '@angular/material/form-field';

/**
 * Options des champs Material : contour, hauteur adaptée aux messages, pas d'astérisque automatique
 * (les libellés portent « (obligatoire) », libelles-ecrans.md).
 *
 * Fournies dans `providers` de chaque écran à formulaire, PAS dans app.config.ts : importer ce jeton là-bas
 * ferait entrer le module form-field de Material et @angular/forms (≈ 115 Ko) dans le bundle initial,
 * téléchargé par toutes les pages, alors que seuls des écrans chargés à la demande ont des formulaires.
 */
export const FORM_FIELD_DEFAULTS: Provider = {
  provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
  useValue: {
    appearance: 'outline',
    subscriptSizing: 'dynamic',
    hideRequiredMarker: true,
  } satisfies MatFormFieldDefaultOptions,
};
