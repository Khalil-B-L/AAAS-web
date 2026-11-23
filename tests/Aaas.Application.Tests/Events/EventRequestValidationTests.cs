using Aaas.Application.Common.Errors;
using Aaas.Application.Events;
using Aaas.Domain.Events;
using static Aaas.Application.Tests.Common.ValidationTestHelper;

namespace Aaas.Application.Tests.Events;

/// <summary>Validation déclarative des DTO d'événements et des requêtes de liste (plan § 9.2).</summary>
public class EventRequestValidationTests
{
    private static CreateEventRequest Valid() => new()
    {
        Title = "Atelier couture",
        Description = "Initiation",
        Location = "Tunis",
        StartsAt = "2026-12-05T10:00+01:00",
        EndsAt = "2026-12-05T12:00+01:00",
        Capacity = 30,
        Price = 12.5m,
    };

    [Fact(DisplayName = "Création complète : aucune erreur")]
    public void Valid_request() => Assert.Empty(Validate(Valid()));

    [Fact(DisplayName = "Création vide : chaque champ obligatoire est signalé (prix facultatif)")]
    public void Empty_request_lists_required_fields()
    {
        var errors = Validate(new CreateEventRequest());

        Assert.Equal(
            ["Capacity", "Description", "EndsAt", "Location", "StartsAt", "Title"],
            errors.Keys.Order());
        Assert.Equal(EventRules.TitleRequiredMessage, errors["Title"]);
        Assert.Equal("La capacité est obligatoire.", errors["Capacity"]);
    }

    [Fact(DisplayName = "Titre de 201 caractères : refusé ; 200 : accepté")]
    public void Title_length()
    {
        Assert.Equal(EventRules.TitleTooLongMessage, Validate(Clone(Valid(), title: new string('a', 201)))["Title"]);
        Assert.Empty(Validate(Clone(Valid(), title: new string('a', 200))));
    }

    [Fact(DisplayName = "Description de 4 001 caractères : refusée")]
    public void Description_length() =>
        Assert.Equal(EventRules.DescriptionTooLongMessage, Validate(Clone(Valid(), description: new string('a', 4001)))["Description"]);

    [Fact(DisplayName = "Date sans décalage : erreur sur le champ, pas sur tout le corps")]
    public void Date_without_offset() =>
        Assert.Equal(["StartsAt"], Validate(Clone(Valid(), startsAt: "2026-12-05T10:00")).Keys);

    [Fact(DisplayName = "Modification : version obligatoire et au bon format")]
    public void Update_requires_version()
    {
        var missing = new UpdateEventRequest
        {
            Title = "T", Description = "D", Location = "L",
            StartsAt = "2026-12-05T10:00+01:00", EndsAt = "2026-12-05T12:00+01:00", Capacity = 1,
        };
        Assert.Equal(["Version"], Validate(missing).Keys);

        var invalid = new UpdateEventRequest
        {
            Title = "T", Description = "D", Location = "L",
            StartsAt = "2026-12-05T10:00+01:00", EndsAt = "2026-12-05T12:00+01:00", Capacity = 1, Version = "abc",
        };
        Assert.Equal(["Version"], Validate(invalid).Keys);
    }

    [Theory(DisplayName = "Pagination : page 0, page 10 001, pageSize 0 et 51 refusés")]
    [InlineData(0, 20, "Page")]
    [InlineData(10_001, 20, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 51, "PageSize")]
    public void Paging_bounds(int page, int pageSize, string field)
    {
        Assert.Equal([field], Validate(new PublicEventListQuery { Page = page, PageSize = pageSize }).Keys);
        Assert.Equal([field], Validate(new AdminEventListQuery { Page = page, PageSize = pageSize }).Keys);
    }

    [Fact(DisplayName = "Pagination par défaut : page 1, 20 éléments, période « à venir »")]
    public void Paging_defaults()
    {
        var query = new AdminEventListQuery();
        Assert.Equal((1, 20, EventPeriod.Upcoming), (query.Page, query.PageSize, query.Period));
        Assert.Empty(Validate(new PublicEventListQuery { Page = 1, PageSize = 50 }));
    }

    [Fact(DisplayName = "Le service convertit les dates en UTC et revérifie leur format (400 sur le champ)")]
    public void Service_conversion()
    {
        var details = AdminEventService.ToDetails(Valid());
        Assert.Equal(new DateTimeOffset(2026, 12, 5, 9, 0, 0, TimeSpan.Zero), details.StartsAt);
        Assert.Equal(TimeSpan.Zero, details.StartsAt.Offset);

        var ex = Assert.Throws<RequestValidationException>(() => AdminEventService.ToDetails(Clone(Valid(), startsAt: "demain")));
        Assert.Equal([EventFields.StartsAt], ex.Errors.Keys);
    }

    private static CreateEventRequest Clone(
        CreateEventRequest source, string? title = null, string? description = null, string? startsAt = null) => new()
    {
        Title = title ?? source.Title,
        Description = description ?? source.Description,
        Location = source.Location,
        StartsAt = startsAt ?? source.StartsAt,
        EndsAt = source.EndsAt,
        Capacity = source.Capacity,
        Price = source.Price,
    };
}
