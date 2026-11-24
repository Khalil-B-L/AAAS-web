import { AdminReservationDto, ReservationMemberDto } from '../../../core/models/api-models';

/*
 * Noms accessibles des liens et boutons répétés à chaque ligne : un lecteur d'écran qui liste les boutons n'entend
 * sinon que « Confirmer, Confirmer, Confirmer… ». Le texte visible est repris EN TÊTE du nom (WCAG 2.5.3 « Label in
 * Name ») pour que la commande vocale « clique sur Confirmer » fonctionne toujours. Tiret plutôt que « de » : pas
 * d'élision à gérer (« d'Amel »).
 */

function who(r: AdminReservationDto): string {
  return r.member ? `${r.member.firstName} ${r.member.lastName}` : 'compte supprimé';
}

/** « Confirmer — Amel Ben Ali » ; avec l'événement quand la liste en mélange plusieurs (Demandes à traiter). */
export function rowActionLabel(action: string, r: AdminReservationDto, withEvent = false): string {
  return withEvent ? `${action} — ${who(r)}, ${r.event.title}` : `${action} — ${who(r)}`;
}

/** target="_blank" : on l'annonce, l'utilisateur ne doit pas perdre son onglet sans le savoir (WCAG 3.2.5). */
export function whatsAppLabel(m: ReservationMemberDto): string {
  return `WhatsApp — ${m.firstName} ${m.lastName} (nouvel onglet)`;
}
