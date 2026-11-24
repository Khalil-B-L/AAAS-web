using System.Globalization;
using Aaas.Application.Options;

namespace Aaas.Application.Tests.Options;

/// <summary>Reservations:ExpirationJob (ADR 0020 § 6) : intervalle borné de 1 min à 1 h, valeurs par défaut sûres.</summary>
public sealed class ExpirationJobOptionsTests
{
    [Fact]
    public void Valeurs_par_defaut_active_toutes_les_5_minutes_seulement_apres_activite()
    {
        var job = new ReservationOptions().ExpirationJob;

        Assert.True(job.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(5), job.Interval);
        Assert.True(job.OnlyWhenActive);
    }

    [Theory]
    [InlineData("00:00:10")]
    [InlineData("00:00:59")]
    [InlineData("01:00:01")]
    [InlineData("02:00:00")]
    [InlineData("-00:05:00")]
    public void Intervalle_hors_bornes_refuse_avec_le_nom_de_la_cle(string value)
    {
        var options = new ReservationOptions
        {
            ExpirationJob = new ExpirationJobOptions { Interval = TimeSpan.Parse(value, CultureInfo.InvariantCulture) },
        };

        var result = new ReservationOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Reservations:ExpirationJob:Interval", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:01:00")]
    [InlineData("00:05:00")]
    [InlineData("01:00:00")]
    public void Intervalle_dans_les_bornes_accepte(string value)
    {
        var options = new ReservationOptions
        {
            ExpirationJob = new ExpirationJobOptions { Interval = TimeSpan.Parse(value, CultureInfo.InvariantCulture) },
        };

        Assert.True(new ReservationOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Tache_desactivee_l_intervalle_reste_valide_quand_meme()
    {
        // Pas de cas particulier : une valeur absurde est une erreur de configuration, même si la tâche est coupée.
        var options = new ReservationOptions
        {
            ExpirationJob = new ExpirationJobOptions { Enabled = false, Interval = TimeSpan.Zero },
        };

        Assert.True(new ReservationOptionsValidator().Validate(null, options).Failed);
    }
}
