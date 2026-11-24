/** '+21620123456' → '+216 20 123 456' (même présentation que PhoneNumber.ToDisplayString côté .NET). */
export function formatTunisianPhone(phone: string | null | undefined): string {
  if (!phone) {
    return '';
  }
  return /^\+216\d{8}$/.test(phone)
    ? `${phone.slice(0, 4)} ${phone.slice(4, 6)} ${phone.slice(6, 9)} ${phone.slice(9)}`
    : phone;
}

/** Lien d'appel : tel:+21620123456. */
export function telLink(phone: string): string {
  return `tel:${phone}`;
}

/** Lien WhatsApp : https://wa.me/21620123456 (sans le « + », format exigé par wa.me). */
export function whatsAppLink(phone: string): string {
  return `https://wa.me/${phone.replace(/^\+/, '')}`;
}
