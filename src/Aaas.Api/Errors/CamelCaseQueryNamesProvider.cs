using System.Text.Json;
using Aaas.Application.Events;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Aaas.Api.Errors;

/// <summary>
/// Les objets de requête de liste (PagingQuery et dérivés) sont liés depuis la query string : sans ce réglage, la clé
/// d'erreur serait le nom C# (« Page ») alors que le corps JSON utilise le camelCase (« pageSize ») grâce à
/// SystemTextJsonValidationMetadataProvider, qui ne vaut que pour le JSON. On fixe le nom de liaison ici plutôt
/// qu'avec [FromQuery(Name = ...)] dans Application, qui ne dépend pas d'ASP.NET Core (ADR 0001).
/// </summary>
internal sealed class CamelCaseQueryNamesProvider : IBindingMetadataProvider
{
    public void CreateBindingMetadata(BindingMetadataProviderContext context)
    {
        var key = context.Key;
        if (key.MetadataKind == ModelMetadataKind.Property
            && key.ContainerType is { } container
            && typeof(PagingQuery).IsAssignableFrom(container)
            && key.Name is { } name)
        {
            // Sert au nom du paramètre lu (?pageSize=) ET à la clé de l'erreur (errors.pageSize).
            context.BindingMetadata.BinderModelName = JsonNamingPolicy.CamelCase.ConvertName(name);
        }
    }
}
