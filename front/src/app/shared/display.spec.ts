import { EventAvailability } from '../core/models/api-models';
import { adminStatusLabels, eventMention } from './events/event-labels';
import { formatTunisianPhone, whatsAppLink } from './phone';
import { formatTndPrice } from './pipes/tnd-price.pipe';
import { toPageNumber } from './ui/pager';

// L'espace des milliers de la locale « fr » est une espace fine insécable (U+202F) : on la normalise pour lire les tests.
const plain = (text: string) => text.replace(/[\u202f\u00a0]/g, ' ');

describe('formatTndPrice', () => {
  it.each([
    [null, 'Gratuit'],
    [0, 'Gratuit'],
    [15, '15 TND, à régler sur place'],
    [12.5, '12,500 TND, à régler sur place'],
    [0.75, '0,750 TND, à régler sur place'],
    [99999.999, '99 999,999 TND, à régler sur place'],
  ])('%s → %s', (price, expected) => {
    expect(plain(formatTndPrice(price))).toBe(expected);
  });
});

describe('eventMention (ordre fixé par api.md)', () => {
  const base: EventAvailability = { status: 'Published', remainingSeats: 8, hasStarted: false, hasEnded: false, isFull: false };

  it.each([
    [{ status: 'Cancelled', hasEnded: true, hasStarted: true, isFull: true }, 'Annulé'],
    [{ hasEnded: true, hasStarted: true, isFull: true }, 'Terminé'],
    [{ hasStarted: true, isFull: true }, 'Réservations closes'],
    [{ isFull: true, remainingSeats: 0 }, 'Complet'],
    [{}, '8 places restantes'],
    [{ remainingSeats: 1 }, '1 place restante'],
  ] as [Partial<EventAvailability>, string][])('%o → %s', (overrides, label) => {
    expect(eventMention({ ...base, ...overrides }).label).toBe(label);
  });
});

describe('adminStatusLabels', () => {
  it('complète le statut par En cours ou Terminé', () => {
    expect(adminStatusLabels({ status: 'Draft', hasStarted: false, hasEnded: false })).toEqual(['Brouillon']);
    expect(adminStatusLabels({ status: 'Published', hasStarted: true, hasEnded: false })).toEqual(['Publié', 'En cours']);
    expect(adminStatusLabels({ status: 'Published', hasStarted: true, hasEnded: true })).toEqual(['Publié', 'Terminé']);
  });
});

describe('téléphone', () => {
  it('formate le numéro normalisé et construit le lien WhatsApp', () => {
    expect(formatTunisianPhone('+21620123456')).toBe('+216 20 123 456');
    expect(whatsAppLink('+21620123456')).toBe('https://wa.me/21620123456');
  });
});

describe('toPageNumber', () => {
  it.each([
    [undefined, 1],
    ['abc', 1],
    ['0', 1],
    ['-2', 1],
    ['3', 3],
    ['99999', 1],
  ])('%s → %s', (value, page) => {
    expect(toPageNumber(value)).toBe(page);
  });
});
