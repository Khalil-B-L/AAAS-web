using Aaas.Application.Common.Errors;
using Aaas.Application.Events;
using Aaas.Application.Options;
using Aaas.Application.Reservations;
using Microsoft.Extensions.Time.Testing;

namespace Aaas.Application.Tests.Reservations;

/// <summary>
/// MemberReservationService en unitaire : seulement ce qui se décide AVANT tout accès aux données (gardes d'entrée).
/// Le reste du service est de la requête LINQ-to-SQL (Join, Skip/Take, ExecuteUpdate), du verrou UPDLOCK et de la
/// rowversion : un fournisseur EF en mémoire n'exécute fidèlement ni l'un ni l'autre, et une doublure de IAppDbContext
/// ne testerait que la doublure. Cette partie est couverte sur un vrai SQL Server par
/// tests/Aaas.Api.IntegrationTests/Reservations (MemberReservationTests, ReservationConcurrencyTests, ReservationBoundaryApiTests).
/// Les dépendances sont donc nulles ici : si une garde laissait passer l'appel, le test échouerait en NullReferenceException.
/// </summary>
public class MemberReservationServiceGuardTests
{
    private static MemberReservationService Service() => new(
        db: null!,
        eventLock: null!,
        occupancyReader: null!,
        expiry: null!,
        Microsoft.Extensions.Options.Options.Create(new ReservationOptions()),
        new FakeTimeProvider());

    [Fact(DisplayName = "Demande : requête nulle → ArgumentNullException, sans accès aux données")]
    public async Task Request_null_is_rejected() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service().RequestAsync(Guid.NewGuid(), null!, TestContext.Current.CancellationToken));

    [Fact(DisplayName = "Demande : eventId absent → RequestValidationException sur « eventId », avant tout verrou")]
    public async Task Request_without_event_id_is_a_validation_error()
    {
        var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
            Service().RequestAsync(Guid.NewGuid(), new CreateReservationRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("L'événement est obligatoire.", Assert.Single(error.Errors["eventId"]));
    }

    [Fact(DisplayName = "Liste : requête de pagination nulle → ArgumentNullException")]
    public async Task List_null_query_is_rejected() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service().ListAsync(Guid.NewGuid(), null!, TestContext.Current.CancellationToken));

    [Fact(DisplayName = "Message « introuvable » : identique à celui du détail public (brouillon et inconnu indiscernables)")]
    public void Not_found_message_is_shared_with_public_detail() =>
        Assert.Equal(PublicEventService.NotFoundMessage, MemberReservationService.NotFoundMessage);
}
