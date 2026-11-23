using Aaas.Domain.Common;
using Aaas.Domain.Events;
using static Aaas.Domain.Tests.Events.EventTestData;

namespace Aaas.Domain.Tests.Events;

/// <summary>Validation des valeurs (RG-13, R28, R29 ; ADR 0009, 0010) et normalisation du prix.</summary>
public class EventRulesTests
{
    private static IReadOnlyList<FieldError> Validate(EventDetails details, DateTimeOffset? previousStartsAt = null) =>
        EventRules.Validate(details, previousStartsAt, Now);

    private static string? ErrorOn(IReadOnlyList<FieldError> errors, string field) =>
        errors.SingleOrDefault(e => e.Field == field)?.Message;

    [Fact(DisplayName = "Des données valides ne produisent aucune erreur")]
    public void Valid_details_have_no_error() => Assert.Empty(Validate(Details()));

    [Fact(DisplayName = "Début = maintenant : refusé ; maintenant + 1 min : accepté")]
    public void Start_must_be_strictly_in_the_future()
    {
        Assert.Equal(EventRules.StartsInPastMessage, ErrorOn(Validate(Details(startsAt: Now)), EventFields.StartsAt));
        Assert.Empty(Validate(Details(startsAt: Now.AddMinutes(1))));
    }

    [Fact(DisplayName = "Fin = début ou avant : refusé sur endsAt")]
    public void End_must_be_after_start()
    {
        Assert.Equal(EventRules.EndsBeforeStartMessage, ErrorOn(Validate(Details(endsAt: Start)), EventFields.EndsAt));
        Assert.Equal(EventRules.EndsBeforeStartMessage, ErrorOn(Validate(Details(endsAt: Start.AddHours(-1))), EventFields.EndsAt));
    }

    [Fact(DisplayName = "R28 : début inchangé dans le passé accepté (événement commencé)")]
    public void Unchanged_past_start_is_accepted()
    {
        var started = Now.AddHours(-1);
        Assert.Empty(Validate(Details(startsAt: started, endsAt: Now.AddHours(2)), previousStartsAt: started));
    }

    [Fact(DisplayName = "R28 : début déplacé dans le passé refusé, même sur un événement déjà commencé")]
    public void Changed_past_start_is_rejected()
    {
        var started = Now.AddHours(-1);
        var errors = Validate(Details(startsAt: Now.AddHours(-2), endsAt: Now.AddHours(2)), previousStartsAt: started);
        Assert.Equal(EventRules.StartsInPastMessage, ErrorOn(errors, EventFields.StartsAt));
    }

    [Fact(DisplayName = "R28 / Q28 : la fin d'un événement commencé ne peut pas être placée dans le passé (ni à maintenant)")]
    public void End_of_started_event_must_stay_in_the_future()
    {
        var started = Now.AddHours(-2);
        Assert.Equal(
            EventRules.EndsInPastMessage,
            ErrorOn(Validate(Details(startsAt: started, endsAt: Now.AddMinutes(-1)), previousStartsAt: started), EventFields.EndsAt));
        Assert.Equal(
            EventRules.EndsInPastMessage,
            ErrorOn(Validate(Details(startsAt: started, endsAt: Now), previousStartsAt: started), EventFields.EndsAt));
        Assert.Empty(Validate(Details(startsAt: started, endsAt: Now.AddMinutes(1)), previousStartsAt: started));
    }

    [Theory(DisplayName = "Capacité : bornes 1 et 10 000 (RG-13)")]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(1, true)]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void Capacity_bounds(int capacity, bool valid) =>
        Assert.Equal(valid, ErrorOn(Validate(Details(capacity: capacity)), EventFields.Capacity) is null);

    [Theory(DisplayName = "Prix : refus des négatifs, de plus de 3 décimales et au-delà de 99 999,999 TND")]
    [InlineData("-1", EventRules.NegativePriceMessage)]
    [InlineData("-0.001", EventRules.NegativePriceMessage)]
    [InlineData("12.5001", EventRules.PriceDecimalsMessage)]
    [InlineData("100000", EventRules.PriceTooHighMessage)]
    [InlineData("99999.9991", EventRules.PriceDecimalsMessage)]
    public void Invalid_prices_are_rejected(string price, string expected) =>
        Assert.Equal(expected, ErrorOn(Validate(Details(price: decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture))), EventFields.Price));

    [Theory(DisplayName = "Prix accepté : 0, 12.5, 12.500, 99 999,999")]
    [InlineData("0")]
    [InlineData("12.5")]
    [InlineData("12.500")]
    [InlineData("99999.999")]
    public void Valid_prices_are_accepted(string price) =>
        Assert.Null(ErrorOn(Validate(Details(price: decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture))), EventFields.Price));

    [Fact(DisplayName = "Prix : 0 devient null (gratuit), 12.500 reste égal à 12.5")]
    public void Price_normalization()
    {
        Assert.Null(EventRules.NormalizePrice(0m));
        Assert.Null(EventRules.NormalizePrice(0.000m));
        Assert.Null(EventRules.NormalizePrice(null));
        Assert.Equal(12.5m, EventRules.NormalizePrice(12.500m));
    }

    [Theory(DisplayName = "Textes : obligatoires et bornés (R29)")]
    [InlineData(EventFields.Title, 200)]
    [InlineData(EventFields.Location, 200)]
    [InlineData(EventFields.Description, 4000)]
    public void Text_fields_are_required_and_bounded(string field, int max)
    {
        EventDetails With(string value) => field switch
        {
            EventFields.Title => Details(title: value),
            EventFields.Location => Details(location: value),
            _ => Details(description: value),
        };

        Assert.NotNull(ErrorOn(Validate(With("   ")), field));
        Assert.NotNull(ErrorOn(Validate(With(new string('a', max + 1))), field));
        Assert.Null(ErrorOn(Validate(With(new string('a', max))), field));
        Assert.Null(ErrorOn(Validate(With("  " + new string('a', max) + "  ")), field)); // compté après Trim
    }

    [Fact(DisplayName = "Plusieurs champs invalides : toutes les erreurs sont listées, une par champ")]
    public void All_errors_are_listed()
    {
        var errors = Validate(Details(startsAt: Now.AddDays(-1), endsAt: Now.AddDays(-2), capacity: 0, price: -1m, title: ""));
        Assert.Equal(
            [EventFields.Title, EventFields.StartsAt, EventFields.EndsAt, EventFields.Capacity, EventFields.Price],
            errors.Select(e => e.Field));
    }
}
