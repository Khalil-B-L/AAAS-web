// Recopie des DTO de docs/technique/api.md (JSON camelCase). Toute modification d'api.md se reporte ici
// dans la même tâche (ADR 0014). Les dates restent des chaînes ISO avec décalage : le DatePipe sait les lire,
// et une conversion en Date au mauvais endroit ferait intervenir le fuseau du navigateur (ADR 0016 § 3).

/** Date ISO 8601 avec décalage, toujours UTC en sortie de l'API : "2026-12-05T09:00:00+00:00". */
export type IsoDateString = string;

// ---------- Erreurs ----------

/** Corps des erreurs de l'API (RFC 9457) complété par `code` et, pour les 400, `errors`. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
  traceId?: string;
}

// ---------- Listes ----------

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// ---------- Comptes ----------

export type Role = 'User' | 'Admin';

export interface AccountDto {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  /** Normalisé E.164 : "+21620123456". */
  phoneNumber: string;
  roles: string[];
  createdAt: IsoDateString;
  privacyConsentAt: IsoDateString;
  privacyPolicyVersion: string;
  lastLoginAt: IsoDateString | null;
}

export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  password: string;
  acceptPrivacyPolicy: boolean;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: IsoDateString;
  user: AccountDto;
}

// ---------- Événements (public) ----------

export type EventStatus = 'Draft' | 'Published' | 'Cancelled';

/** Champs utilisés pour choisir la mention affichée (event-mention.ts). */
export interface EventAvailability {
  status: EventStatus;
  remainingSeats: number;
  hasStarted: boolean;
  hasEnded: boolean;
  isFull: boolean;
}

export interface EventSummaryDto extends EventAvailability {
  id: string;
  title: string;
  location: string;
  startsAt: IsoDateString;
  endsAt: IsoDateString;
  /** Dinars ; null = gratuit. */
  price: number | null;
  capacity: number;
  isBookable: boolean;
}

export interface EventDetailDto extends EventSummaryDto {
  description: string;
}

// ---------- Événements (admin) ----------

export interface AdminEventSummaryDto {
  id: string;
  title: string;
  location: string;
  startsAt: IsoDateString;
  endsAt: IsoDateString;
  status: EventStatus;
  capacity: number;
  price: number | null;
  pendingCount: number;
  confirmedCount: number;
  remainingSeats: number;
  hasStarted: boolean;
  hasEnded: boolean;
  /** Brouillon jamais publié : seul cas où « Supprimer » est proposé. */
  canBeDeleted: boolean;
}

export interface AdminEventDto extends AdminEventSummaryDto {
  description: string;
  publishedAt: IsoDateString | null;
  firstPublishedAt: IsoDateString | null;
  createdAt: IsoDateString;
  updatedAt: IsoDateString;
  /** Jeton de concurrence (Base64 du rowversion) à renvoyer tel quel à la modification (ADR 0006). */
  version: string;
}

/** Corps de création ; la modification ajoute `version` et `confirmRegistrantsWarning`. */
export interface EventInput {
  title: string;
  description: string;
  location: string;
  /** Avec décalage obligatoire : "2026-12-05T10:00+01:00" (toTunisIso). */
  startsAt: string;
  endsAt: string;
  capacity: number;
  price: number | null;
}

export interface UpdateEventRequest extends EventInput {
  version: string;
  confirmRegistrantsWarning: boolean;
}

export interface ApiWarning {
  code: string;
  message: string;
}

export interface PublishEventResponse {
  event: AdminEventDto;
  warnings: ApiWarning[];
}

export type EventPeriod = 'upcoming' | 'past' | 'all';

export interface AdminEventListQuery {
  status: EventStatus | null;
  period: EventPeriod;
  page: number;
}

// ---------- Numéro de contact ----------

export interface ContactSettingsDto {
  phoneNumber: string | null;
  whatsApp: boolean;
  isConfigured: boolean;
  version: string;
  updatedAt: IsoDateString | null;
}

export interface UpdateContactSettingsRequest {
  phoneNumber: string;
  whatsApp: boolean;
  version: string;
}

// ---------- Réservations (membre) ----------

/** Statut EFFECTIF renvoyé par l'API : une demande échue est déjà « Expired » (ADR 0005). */
export type ReservationStatus =
  | 'Pending'
  | 'Confirmed'
  | 'Refused'
  | 'Expired'
  | 'CancelledByUser'
  | 'CancelledByAssociation';

export type MyReservationScope = 'current' | 'history';

export interface CreateReservationRequest {
  eventId: string;
}

/** Événement vu depuis une réservation ; `isVisible` faux = repassé en brouillon (pas de lien). */
export interface ReservationEventDto {
  id: string;
  title: string;
  location: string;
  startsAt: IsoDateString;
  endsAt: IsoDateString;
  price: number | null;
  status: EventStatus;
  hasStarted: boolean;
  hasEnded: boolean;
  isVisible: boolean;
}

export interface MyReservationDto {
  id: string;
  status: ReservationStatus;
  requestedAt: IsoDateString;
  /** Renseignée seulement pour une demande en attente : date limite pour la faire confirmer. */
  expiresAt: IsoDateString | null;
  decidedAt: IsoDateString | null;
  refusalReason: string | null;
  cancelledAt: IsoDateString | null;
  isPaid: boolean;
  paidAt: IsoDateString | null;
  /** Calculé par l'API (RG-05) : le front n'applique jamais la règle des 24 h avec l'horloge du poste. */
  canCancel: boolean;
  cancellableUntil: IsoDateString | null;
  event: ReservationEventDto;
}

// ---------- Réservations (administration, tâche B) ----------

/** Coordonnées du membre (RG-15) ; `null` dans AdminReservationDto = « Compte supprimé ». Jamais stockées hors mémoire. */
export interface ReservationMemberDto {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
}

export interface AdminReservationEventDto {
  id: string;
  title: string;
  startsAt: IsoDateString;
  endsAt: IsoDateString;
  price: number | null;
  status: EventStatus;
}

/** Boutons à proposer, calculés par l'API (le serveur revérifie à chaque action). */
export interface AdminReservationActionsDto {
  canConfirm: boolean;
  canRefuse: boolean;
  canCancel: boolean;
  canMarkPaid: boolean;
  canUnmarkPaid: boolean;
}

export interface AdminReservationDto {
  id: string;
  status: ReservationStatus;
  requestedAt: IsoDateString;
  expiresAt: IsoDateString | null;
  decidedAt: IsoDateString | null;
  refusalReason: string | null;
  cancelledAt: IsoDateString | null;
  isPaid: boolean;
  paidAt: IsoDateString | null;
  /** Admin qui a posé « Payée » ; `null` avec isPaid = vrai : administrateur supprimé. */
  paidBy: { firstName: string; lastName: string } | null;
  member: ReservationMemberDto | null;
  event: AdminReservationEventDto;
  actions: AdminReservationActionsDto;
}

export interface RefuseReservationRequest {
  reason: string | null;
}

export interface EventRegistrantsDto {
  event: {
    id: string;
    title: string;
    startsAt: IsoDateString;
    endsAt: IsoDateString;
    capacity: number;
    price: number | null;
    status: EventStatus;
    hasStarted: boolean;
    hasEnded: boolean;
  };
  occupancy: { pending: number; confirmed: number; active: number; remaining: number };
  /** De la plus ancienne à la plus récente (US-E4). */
  pending: AdminReservationDto[];
  /** Par nom puis prénom ; comptes supprimés à la fin. */
  confirmed: AdminReservationDto[];
  /** Refusées, expirées, annulées : la plus récente demande d'abord. */
  others: AdminReservationDto[];
}
