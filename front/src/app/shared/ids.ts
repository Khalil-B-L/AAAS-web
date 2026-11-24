const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Identifiant au format GUID ? Une adresse comme /evenements/abc n'appelle pas l'API : le front affiche
 * directement « Contenu introuvable ». L'API répondrait 404 elle aussi (correctif D1) ; ce contrôle évite
 * seulement un aller-retour réseau inutile.
 */
export function isGuid(value: string | null | undefined): value is string {
  return !!value && GUID_PATTERN.test(value);
}
