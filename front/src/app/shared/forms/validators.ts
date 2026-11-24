import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// Validateurs = confort côté front : mêmes règles que l'API, qui reste la référence (ADR 0016 § 1).
// Un validateur est une fonction (contrôle) => null si valide, sinon { cléDErreur: détail } (≈ ValidationAttribute.IsValid).
// Valeur vide → null : c'est Validators.required qui signale l'absence.

const TUNISIAN_PHONE = /^\+216[0-9]{8}$/;

/** Retire tous les espaces (y compris insécables), comme PhoneNumber.TryParse côté .NET. */
export function compactPhone(value: string): string {
  return value.replace(/\s/g, '');
}

/** RG-11 : « +216 » suivi d'exactement 8 chiffres latins, mobile ou fixe, espaces tolérés. */
export const tunisianPhone: ValidatorFn = (control: AbstractControl<string>): ValidationErrors | null => {
  const value = control.value ?? '';
  if (value.trim() === '') {
    return null;
  }
  return TUNISIAN_PHONE.test(compactPhone(value)) ? null : { tunisianPhone: true };
};

/** R21 : au moins une lettre (accents compris) et au moins un chiffre. La longueur est vérifiée par min/maxLength. */
export const passwordStrength: ValidatorFn = (control: AbstractControl<string>): ValidationErrors | null => {
  const value = control.value ?? '';
  if (value === '') {
    return null;
  }
  const errors: ValidationErrors = {};
  if (!/\p{L}/u.test(value)) {
    errors['passwordLetter'] = true;
  }
  if (!/[0-9]/.test(value)) {
    errors['passwordDigit'] = true;
  }
  return Object.keys(errors).length ? errors : null;
};

/**
 * Prix saisi en texte (la virgule française est acceptée) → nombre, ou null si vide (gratuit).
 * Renvoie NaN si la saisie n'est pas un nombre décimal positif ou nul.
 */
export function parsePrice(value: string): number | null {
  const text = value.trim().replace(',', '.');
  if (text === '') {
    return null;
  }
  return /^-?\d+(\.\d+)?$/.test(text) ? Number(text) : Number.NaN;
}

/** ADR 0010 : ≥ 0, ≤ 99 999,999, au plus 3 décimales (refus, pas d'arrondi). */
export const priceValidator: ValidatorFn = (control: AbstractControl<string>): ValidationErrors | null => {
  const raw = control.value ?? '';
  const price = parsePrice(raw);
  if (price === null) {
    return null;
  }
  if (Number.isNaN(price)) {
    return { priceFormat: true };
  }
  if (price < 0) {
    return { priceNegative: true };
  }
  const decimals = raw.trim().replace(',', '.').split('.')[1] ?? '';
  if (decimals.length > 3) {
    return { priceDecimals: true };
  }
  if (price > 99_999.999) {
    return { priceTooHigh: true };
  }
  return null;
};

/** Capacité saisie en texte → entier, ou NaN si ce n'est pas un entier. */
export function parseCapacity(value: string): number {
  const text = value.trim();
  return /^\d+$/.test(text) ? Number(text) : Number.NaN;
}

/** RG-13 : entier de 1 à 10 000. */
export const capacityValidator: ValidatorFn = (control: AbstractControl<string>): ValidationErrors | null => {
  const raw = control.value ?? '';
  if (raw.trim() === '') {
    return null;
  }
  const capacity = parseCapacity(raw);
  return Number.isInteger(capacity) && capacity >= 1 && capacity <= 10_000 ? null : { capacityRange: true };
};

/**
 * Validateur de GROUPE (il lit deux champs) : la fin doit être après le début.
 * Les deux valeurs sont au même format 'AAAA-MM-JJTHH:mm' (heure de Tunis) : la comparaison de chaînes suffit.
 * « Dans le futur » est laissé au serveur (il dépend de l'heure, ADR 0016 § 3).
 */
export const endAfterStart: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const startsAt: unknown = group.get('startsAt')?.value;
  const endsAt: unknown = group.get('endsAt')?.value;
  if (typeof startsAt !== 'string' || typeof endsAt !== 'string' || !startsAt || !endsAt) {
    return null;
  }
  return endsAt > startsAt ? null : { endBeforeStart: true };
};
