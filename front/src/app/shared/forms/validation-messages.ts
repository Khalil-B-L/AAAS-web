import { ValidationErrors } from '@angular/forms';

// Messages IDENTIQUES à ceux de l'API (docs/technique/api.md, regles-metier.md, constantes C#) : l'utilisateur lit
// la même phrase que l'erreur vienne du front ou du serveur. Toute modification se reporte des deux côtés (ADR 0016).

export const PHONE_FORMAT_MESSAGE =
  'Le numéro de téléphone doit être un numéro tunisien au format +216 suivi de 8 chiffres, par exemple +216 20 123 456.';
export const END_BEFORE_START_MESSAGE = 'La fin doit être après le début.';

type FieldMessages = Record<string, string>;

/** Messages par champ (clé = nom du contrôle = clé JSON de l'API), puis par clé d'erreur du validateur. */
export const VALIDATION_MESSAGES: Record<string, FieldMessages> = {
  firstName: {
    required: 'Le prénom est obligatoire.',
    maxlength: 'Le prénom ne doit pas dépasser 100 caractères.',
  },
  lastName: {
    required: 'Le nom est obligatoire.',
    maxlength: 'Le nom ne doit pas dépasser 100 caractères.',
  },
  email: {
    required: "L'e-mail est obligatoire.",
    email: "L'e-mail n'est pas valide.",
    maxlength: "L'e-mail ne doit pas dépasser 256 caractères.",
  },
  phoneNumber: {
    required: 'Le numéro de téléphone est obligatoire.',
    tunisianPhone: PHONE_FORMAT_MESSAGE,
  },
  password: {
    required: 'Le mot de passe est obligatoire.',
    minlength: 'Le mot de passe doit contenir au moins 8 caractères.',
    maxlength: 'Le mot de passe ne doit pas dépasser 128 caractères.',
    passwordLetter: 'Le mot de passe doit contenir au moins une lettre.',
    passwordDigit: 'Le mot de passe doit contenir au moins un chiffre.',
  },
  acceptPrivacyPolicy: {
    required: 'Vous devez accepter la politique de confidentialité pour créer un compte.',
  },
  title: {
    required: 'Le titre est obligatoire.',
    maxlength: 'Le titre ne doit pas dépasser 200 caractères.',
  },
  // Actualités (NewsRules) : texte compté après Trim et \r\n → \n.
  content: {
    required: 'Le texte est obligatoire.',
    maxlength: 'Le texte ne doit pas dépasser 8 000 caractères.',
  },
  location: {
    required: 'Le lieu est obligatoire.',
    maxlength: 'Le lieu ne doit pas dépasser 200 caractères.',
  },
  description: {
    required: 'La description est obligatoire.',
    maxlength: 'La description ne doit pas dépasser 4 000 caractères.',
  },
  startsAt: {
    required: 'La date de début est obligatoire.',
  },
  endsAt: {
    required: 'La date de fin est obligatoire.',
    endBeforeStart: END_BEFORE_START_MESSAGE,
  },
  capacity: {
    required: 'La capacité est obligatoire.',
    capacityRange: 'La capacité doit être comprise entre 1 et 10 000 places.',
  },
  price: {
    // Seul message propre au front : l'API reçoit toujours un nombre JSON, elle n'a pas ce cas.
    priceFormat: 'Le prix doit être un nombre, par exemple 15 ou 12,500.',
    priceNegative: 'Le prix ne peut pas être négatif.',
    priceDecimals: 'Le prix est en dinars, avec au plus 3 décimales (millimes).',
    priceTooHigh: 'Le prix ne doit pas dépasser 99 999,999 TND.',
  },
};

const FALLBACK_MESSAGE = 'Valeur invalide.';

/**
 * Messages à afficher pour les erreurs d'un champ. L'erreur `server` (recopiée d'une réponse 400 par
 * applyServerErrors) contient déjà les textes de l'API ; les autres clés sont traduites par la table ci-dessus.
 */
export function messagesFor(field: string, errors: ValidationErrors | null): string[] {
  if (!errors) {
    return [];
  }
  const messages: string[] = [];
  for (const [key, detail] of Object.entries(errors)) {
    if (key === 'server' && Array.isArray(detail)) {
      messages.push(...(detail as string[]));
    } else {
      messages.push(VALIDATION_MESSAGES[field]?.[key] ?? FALLBACK_MESSAGE);
    }
  }
  return [...new Set(messages)];
}
