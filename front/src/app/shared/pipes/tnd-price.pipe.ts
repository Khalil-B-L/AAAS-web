import { formatNumber } from '@angular/common';
import { Pipe, PipeTransform } from '@angular/core';
import { APP_LOCALE } from '../../core/config/locale';

/**
 * Prix en dinars (ADR 0010, US-C1) : null → « Gratuit » ; 15 → « 15 TND, à régler sur place » ;
 * 12.5 → « 12,500 TND, à régler sur place » (3 décimales = millimes). Séparateurs de la locale « fr »
 * (virgule décimale, espace fine insécable pour les milliers : « 99 999,999 »).
 */
export function formatTndPrice(price: number | null | undefined): string {
  if (price === null || price === undefined || price === 0) {
    return 'Gratuit';
  }
  const digits = Number.isInteger(price) ? '1.0-0' : '1.3-3';
  return `${formatNumber(price, APP_LOCALE, digits)} TND, à régler sur place`;
}

/** Pipe pur (défaut) : recalculé seulement si la valeur d'entrée change. Usage : {{ event.price | tndPrice }}. */
@Pipe({ name: 'tndPrice' })
export class TndPricePipe implements PipeTransform {
  transform(price: number | null | undefined): string {
    return formatTndPrice(price);
  }
}
