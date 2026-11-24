import { DATE_PIPE_DEFAULT_OPTIONS } from '@angular/common';
import { LOCALE_ID, Provider } from '@angular/core';
import { TUNIS_DATE_PIPE_TIMEZONE } from '../time/tunis-time';
import { APP_LOCALE } from './locale';

/**
 * Locale française et fuseau de Tunis pour tous les DatePipe (ADR 0003, 0016 § 3).
 * Partagé par app.config.ts et par les tests de composants, qui affichent ainsi les mêmes dates que l'application.
 */
export const FORMATTING_PROVIDERS: Provider[] = [
  { provide: LOCALE_ID, useValue: APP_LOCALE },
  // Toutes les dates affichées le sont à l'heure de Tunis (+0100), quel que soit le fuseau du poste.
  { provide: DATE_PIPE_DEFAULT_OPTIONS, useValue: { timezone: TUNIS_DATE_PIPE_TIMEZONE } },
];
