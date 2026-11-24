import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from '../models/api-models';

/** Erreur de l'API sous une forme unique pour tous les écrans (ADR 0016 § 2). */
export interface ApiError {
  /** Statut HTTP ; 0 = serveur injoignable (réseau, CORS, API arrêtée). */
  status: number;
  /** Code stable de l'API (`validation_failed`, `concurrent_update`…) ou code local (`network`, `unavailable`, `unknown`). */
  code: string;
  /** Message français affichable tel quel. */
  message: string;
  /** Messages par champ (clés = noms des contrôles du formulaire). */
  fieldErrors: Record<string, string[]>;
}

// Textes : docs/fonctionnel/libelles-ecrans.md § 1 et § 7.
export const NETWORK_ERROR_MESSAGE = 'Impossible de joindre le serveur. Vérifiez votre connexion et réessayez.';
export const UNAVAILABLE_MESSAGE = 'Le service est momentanément indisponible. Réessayez dans un instant.';
export const GENERIC_ERROR_MESSAGE = 'Un problème est survenu. Veuillez réessayer plus tard.';
export const FORBIDDEN_MESSAGE = "Vous n'avez pas le droit d'effectuer cette action.";

/**
 * Transforme n'importe quelle erreur reçue (HttpErrorResponse, ou erreur d'un rxResource qui l'enveloppe
 * dans `cause`) en ApiError. Le front décide toujours sur `code`, jamais sur le texte.
 */
export function toApiError(error: unknown): ApiError {
  const http = findHttpError(error);
  if (!http) {
    return { status: -1, code: 'unknown', message: GENERIC_ERROR_MESSAGE, fieldErrors: {} };
  }

  if (http.status === 0) {
    return { status: 0, code: 'network', message: NETWORK_ERROR_MESSAGE, fieldErrors: {} };
  }

  // 502 / 503 / 504 : souvent nginx pendant le démarrage de l'API, ou App Service qui se réveille.
  if (http.status === 502 || http.status === 503 || http.status === 504) {
    return { status: http.status, code: 'unavailable', message: UNAVAILABLE_MESSAGE, fieldErrors: {} };
  }

  const problem = asProblemDetails(http.error);
  if (!problem?.code) {
    const code = http.status === 403 ? 'forbidden' : 'unknown';
    const message = http.status === 403 ? FORBIDDEN_MESSAGE : (problem?.title ?? GENERIC_ERROR_MESSAGE);
    return { status: http.status, code, message, fieldErrors: {} };
  }

  return {
    status: http.status,
    code: problem.code,
    message: problem.title ?? GENERIC_ERROR_MESSAGE,
    fieldErrors: normalizeFieldErrors(problem.errors),
  };
}

/** `$.capacity` (erreur de type JSON) → `capacity` ; `$` seul (corps illisible) reste tel quel. */
function normalizeFieldErrors(errors: Record<string, string[]> | undefined): Record<string, string[]> {
  const result: Record<string, string[]> = {};
  for (const [key, messages] of Object.entries(errors ?? {})) {
    const name = key.startsWith('$.') ? key.slice(2) : key;
    result[name] = [...(result[name] ?? []), ...messages];
  }
  return result;
}

function findHttpError(error: unknown): HttpErrorResponse | null {
  if (error instanceof HttpErrorResponse) {
    return error;
  }
  // rxResource enveloppe une valeur qui n'est pas une Error (HttpErrorResponse n'en est pas une) dans `cause`.
  if (error instanceof Error && error.cause instanceof HttpErrorResponse) {
    return error.cause;
  }
  return null;
}

function asProblemDetails(body: unknown): ProblemDetails | null {
  if (body && typeof body === 'object') {
    return body as ProblemDetails;
  }
  // Corps reçu en texte (Content-Type inattendu) : on tente le JSON, sans jamais lever d'exception.
  if (typeof body === 'string' && body.startsWith('{')) {
    try {
      return JSON.parse(body) as ProblemDetails;
    } catch {
      return null;
    }
  }
  return null;
}
