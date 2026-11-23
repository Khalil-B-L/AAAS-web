using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Aaas.Api.Errors;

/// <summary>
/// Messages de la liaison de modèle (paramètres de requête, route, corps vide) en français (plan étape 2, § 6.3).
/// Par défaut : « The value 'abc' is not valid for Page. » pour « ?page=abc ».
/// Ne couvre pas les erreurs de TYPE dans le corps JSON (« "capacity": "trente" ») : elles viennent de
/// System.Text.Json, sous la clé « $.capacity », avec un message technique (limite connue, api.md).
/// Le champ en cause est donné par la clé de l'erreur : les messages n'ont pas à le répéter.
/// </summary>
internal static class FrenchModelBindingMessages
{
    public static void Apply(DefaultModelBindingMessageProvider messages)
    {
        const string Required = "Une valeur est obligatoire.";
        const string Invalid = "La valeur fournie n'est pas valide.";
        const string NotANumber = "La valeur doit être un nombre.";

        messages.SetMissingBindRequiredValueAccessor(_ => Required);
        messages.SetMissingKeyOrValueAccessor(() => Required);
        messages.SetMissingRequestBodyRequiredValueAccessor(() => "Le corps de la requête est obligatoire.");
        messages.SetValueMustNotBeNullAccessor(_ => Required);
        messages.SetAttemptedValueIsInvalidAccessor((value, _) => $"La valeur « {value} » n'est pas valide.");
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"La valeur « {value} » n'est pas valide.");
        messages.SetUnknownValueIsInvalidAccessor(_ => Invalid);
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => Invalid);
        messages.SetValueIsInvalidAccessor(value => $"La valeur « {value} » n'est pas valide.");
        messages.SetValueMustBeANumberAccessor(_ => NotANumber);
        messages.SetNonPropertyValueMustBeANumberAccessor(() => NotANumber);
    }
}
