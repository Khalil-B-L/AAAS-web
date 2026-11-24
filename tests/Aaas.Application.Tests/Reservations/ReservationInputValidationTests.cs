using Aaas.Application.Options;
using Aaas.Application.Reservations;
using static Aaas.Application.Tests.Common.ValidationTestHelper;

namespace Aaas.Application.Tests.Reservations;

/// <summary>Entrées des réservations (plan-reservations § 9.2) : DTO de demande, paramètres de liste, options de délais.</summary>
public class ReservationInputValidationTests
{
    [Fact(DisplayName = "Demande : eventId absent → message français sur EventId ; présent → valide")]
    public void Create_request_requires_event_id()
    {
        Assert.Equal("L'événement est obligatoire.", Validate(new CreateReservationRequest())["EventId"]);
        Assert.Empty(Validate(new CreateReservationRequest { EventId = Guid.NewGuid() }));
    }

    [Fact(DisplayName = "Liste : scope « À venir » par défaut, page 0 et pageSize 51 refusés")]
    public void List_query_defaults_and_bounds()
    {
        Assert.Equal(MyReservationScope.Current, new MyReservationListQuery().Scope);
        var errors = Validate(new MyReservationListQuery { Page = 0, PageSize = 51 });
        Assert.True(errors.ContainsKey("Page"));
        Assert.True(errors.ContainsKey("PageSize"));
    }

    [Fact(DisplayName = "Options par défaut : 48 h et 24 h, valides")]
    public void Default_options_are_valid()
    {
        var options = new ReservationOptions();

        Assert.Equal(TimeSpan.FromHours(48), options.PendingLifetime);
        Assert.Equal(TimeSpan.FromHours(24), options.ConfirmedCancellationDeadline);
        Assert.True(new ReservationOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory(DisplayName = "Durée de vie hors bornes (0, 59 s, 31 jours) → refusée avec le nom de la clé")]
    [InlineData("00:00:00")]
    [InlineData("00:00:59")]
    [InlineData("31.00:00:00")]
    public void Invalid_pending_lifetime_is_rejected(string value)
    {
        var result = new ReservationOptionsValidator().Validate(null, new ReservationOptions { PendingLifetime = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture) });

        Assert.True(result.Failed);
        Assert.Contains("Reservations:PendingLifetime", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Délai d'annulation négatif → refusé ; 0 accepté")]
    public void Cancellation_deadline_bounds()
    {
        var validator = new ReservationOptionsValidator();
        Assert.True(validator.Validate(null, new ReservationOptions { ConfirmedCancellationDeadline = TimeSpan.FromHours(-1) }).Failed);
        Assert.True(validator.Validate(null, new ReservationOptions { ConfirmedCancellationDeadline = TimeSpan.Zero }).Succeeded);
    }

    [Fact(DisplayName = "Durée de vie courte de démonstration (1 min) : acceptée")]
    public void Short_demo_lifetime_is_accepted() =>
        Assert.True(new ReservationOptionsValidator().Validate(null, new ReservationOptions { PendingLifetime = TimeSpan.FromMinutes(1) }).Succeeded);
}
