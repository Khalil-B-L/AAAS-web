import { AdminEventDto, EventInput } from '../../../../core/models/api-models';
import { toTunisIso, toTunisLocalInput } from '../../../../core/time/tunis-time';
import { parseCapacity, parsePrice } from '../../../../shared/forms/validators';

/** Valeurs du formulaire : tout en texte (champs de saisie), dates à l'heure de Tunis sans fuseau. */
export interface EventFormValue {
  title: string;
  description: string;
  location: string;
  startsAt: string;
  endsAt: string;
  capacity: string;
  price: string;
}

export const EMPTY_EVENT_FORM: EventFormValue = {
  title: '',
  description: '',
  location: '',
  startsAt: '',
  endsAt: '',
  capacity: '',
  price: '',
};

/** Fiche reçue de l'API → valeurs du formulaire (UTC → heure de Tunis, prix avec virgule). */
export function toEventFormValue(event: AdminEventDto): EventFormValue {
  return {
    title: event.title,
    description: event.description,
    location: event.location,
    startsAt: toTunisLocalInput(event.startsAt),
    endsAt: toTunisLocalInput(event.endsAt),
    capacity: String(event.capacity),
    price: event.price === null ? '' : String(event.price).replace('.', ','),
  };
}

/**
 * Formulaire (déjà validé) → corps JSON de l'API : dates avec « +01:00 », capacité et prix en nombres JSON
 * (une chaîne « 30 » provoquerait l'erreur de type non traduite de l'API, api.md « Limite connue »).
 */
export function toEventInput(value: EventFormValue): EventInput {
  const price = parsePrice(value.price);
  return {
    title: value.title,
    description: value.description,
    location: value.location,
    startsAt: toTunisIso(value.startsAt),
    endsAt: toTunisIso(value.endsAt),
    capacity: parseCapacity(value.capacity),
    price: price === null || Number.isNaN(price) ? null : price,
  };
}
