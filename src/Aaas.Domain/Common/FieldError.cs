namespace Aaas.Domain.Common;

/// <summary>
/// Erreur de saisie sur un champ précis. <see cref="Field"/> est le nom JSON (camelCase) :
/// Application la convertit en 400 avec <c>errors[Field]</c>, que le formulaire du front affiche sous le bon champ.
/// </summary>
public sealed record FieldError(string Field, string Message);
