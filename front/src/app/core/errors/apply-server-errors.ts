import { FormGroup } from '@angular/forms';
import { ApiError } from './api-error';

/**
 * Recopie les erreurs par champ d'une réponse 400 sur les contrôles du formulaire (mêmes noms que les clés JSON).
 * L'erreur `server` disparaît d'elle-même dès que l'utilisateur modifie le champ : à chaque changement de valeur,
 * Angular recalcule les erreurs à partir des seuls validateurs du contrôle.
 *
 * @returns les messages qui ne correspondent à aucun contrôle, à afficher dans le bandeau du formulaire.
 */
export function applyServerErrors(form: FormGroup, error: ApiError): string[] {
  const unmatched: string[] = [];
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const control = form.get(field);
    if (control) {
      control.setErrors({ ...control.errors, server: messages });
      control.markAsTouched();
    } else {
      unmatched.push(...messages);
    }
  }
  return unmatched;
}
