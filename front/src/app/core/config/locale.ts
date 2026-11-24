import { registerLocaleData } from '@angular/common';
import localeFr from '@angular/common/locales/fr';

/** Interface en français uniquement (R27) : pas d'outil d'i18n, une seule locale. */
export const APP_LOCALE = 'fr';

// Données de la locale (noms des jours et des mois, séparateurs) utilisées par DatePipe et formatNumber.
// Enregistrées au chargement de ce module : tout fichier qui formate en « fr » importe APP_LOCALE d'ici.
registerLocaleData(localeFr);
