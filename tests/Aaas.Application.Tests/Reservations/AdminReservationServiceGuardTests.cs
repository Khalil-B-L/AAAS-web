using Aaas.Application.Common.Errors;
using Aaas.Application.Events;
using Aaas.Application.Reservations;
using Aaas.Domain.Reservations;
using Microsoft.Extensions.Time.Testing;
using static Aaas.Application.Tests.Common.ValidationTestHelper;

namespace Aaas.Application.Tests.Reservations;

/// <summary>
/// AdminReservationService en unitaire : gardes d'entrée seulement (même raison que MemberReservationServiceGuardTests :
/// le reste est du LINQ-to-SQL et de la rowversion, couverts sur un vrai SQL Server par AdminReservationTests et
/// AdminReservationConcurrencyTests). Dépendances nulles : une garde qui laisserait passer échouerait en NullReferenceException.
/// </summary>
public class AdminReservationServiceGuardTests
{
    private static AdminReservationService Service() => new(db: null!, identity: null!, new FakeTimeProvider());

    [Fact(DisplayName = "Refus : motif de 501 caractères → 400 sur « reason », avant tout accès aux données")]
    public async Task Refuse_reason_too_long()
    {
        var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
            Service().RefuseAsync(Guid.NewGuid(), new RefuseReservationRequest { Reason = new string('a', 501) }, TestContext.Current.CancellationToken));

        Assert.Equal(ReservationRules.RefusalReasonTooLongMessage, Assert.Single(error.Errors["reason"]));
    }

    [Fact(DisplayName = "Refus : 500 caractères entourés d'espaces → pas d'erreur de validation (longueur comptée après Trim)")]
    public async Task Refuse_reason_is_measured_after_trim() =>
        // Passe la garde : échoue ensuite sur la base nulle, preuve que la validation n'a pas rejeté le motif.
        await Assert.ThrowsAsync<NullReferenceException>(() =>
            Service().RefuseAsync(Guid.NewGuid(), new RefuseReservationRequest { Reason = "  " + new string('a', 500) + "  " }, TestContext.Current.CancellationToken));

    [Fact(DisplayName = "Liste des demandes : requête nulle → ArgumentNullException")]
    public async Task List_pending_null_query() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service().ListPendingAsync(null!, TestContext.Current.CancellationToken));

    [Fact(DisplayName = "Liste des demandes : 20 par défaut ; page 0 et pageSize 51 refusés")]
    public void Pending_query_bounds()
    {
        Assert.Equal(20, new PendingReservationListQuery().PageSize);
        var errors = Validate(new PendingReservationListQuery { Page = 0, PageSize = 51 });
        Assert.True(errors.ContainsKey("Page"));
        Assert.True(errors.ContainsKey("PageSize"));
    }

    [Theory(DisplayName = "Motif : vide ou blanc → null ; espaces retirés")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t ", null)]
    [InlineData(" Réservé aux adhérents ", "Réservé aux adhérents")]
    public void Reason_normalization(string? input, string? expected) =>
        Assert.Equal(expected, ReservationRules.NormalizeRefusalReason(input));

    [Fact(DisplayName = "Message « introuvable » : identique à celui du détail public")]
    public void Not_found_message() => Assert.Equal(PublicEventService.NotFoundMessage, AdminReservationService.NotFoundMessage);
}
