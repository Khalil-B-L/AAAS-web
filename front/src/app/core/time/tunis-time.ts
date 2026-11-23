// Heure de Tunis (ADR 0003, 0016 § 3) : UTC+1 toute l'année, sans heure d'été.
// Ces fonctions n'utilisent JAMAIS le fuseau du navigateur (pas de getHours(), pas de new Date('2026-12-05T10:00')) :
// un admin en France l'été (UTC+2) saisirait sinon des heures fausses d'une heure.

/** Décalage envoyé à l'API. Si la Tunisie réintroduisait l'heure d'été : passer par Intl.DateTimeFormat('Africa/Tunis'). */
export const TUNIS_UTC_OFFSET = '+01:00';
export const TUNIS_OFFSET_MINUTES = 60;
/** Même décalage, au format attendu par le DatePipe (DATE_PIPE_DEFAULT_OPTIONS dans app.config.ts). */
export const TUNIS_DATE_PIPE_TIMEZONE = '+0100';

const LOCAL_INPUT_PATTERN = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$/;

/**
 * Valeur d'un champ `datetime-local` (heure de Tunis, sans fuseau) → chaîne envoyée à l'API.
 * '2026-12-05T10:00' → '2026-12-05T10:00+01:00'. Une valeur vide ou mal formée est renvoyée telle quelle :
 * l'API répondra 400 avec un message français sur le champ.
 */
export function toTunisIso(localValue: string): string {
  return LOCAL_INPUT_PATTERN.test(localValue) ? `${localValue}${TUNIS_UTC_OFFSET}` : localValue;
}

/**
 * Date ISO reçue de l'API (UTC) → valeur pour préremplir un champ `datetime-local` à l'heure de Tunis.
 * On part de l'instant (Date.parse), on ajoute 1 h, puis on lit les composants avec les getters UTC :
 * '2026-12-31T23:30:00+00:00' → '2027-01-01T00:30'. Les secondes sont perdues (champ à la minute).
 */
export function toTunisLocalInput(isoValue: string): string {
  const instant = Date.parse(isoValue);
  if (Number.isNaN(instant)) {
    return '';
  }
  const tunis = new Date(instant + TUNIS_OFFSET_MINUTES * 60_000);
  const pad = (n: number) => String(n).padStart(2, '0');
  return (
    `${tunis.getUTCFullYear()}-${pad(tunis.getUTCMonth() + 1)}-${pad(tunis.getUTCDate())}` +
    `T${pad(tunis.getUTCHours())}:${pad(tunis.getUTCMinutes())}`
  );
}
