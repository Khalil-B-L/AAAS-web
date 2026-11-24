using Aaas.Infrastructure.Jobs;

namespace Aaas.Infrastructure.Tests.Jobs;

public sealed class ApiActivityTests
{
    [Fact]
    public void Sans_requete_rien_a_consommer()
    {
        var activity = new ApiActivity();

        Assert.False(activity.HasPendingActivity);
        Assert.False(activity.TryConsume());
    }

    [Fact]
    public void Plusieurs_requetes_une_seule_consommation_puis_une_nouvelle_requete_se_voit()
    {
        var activity = new ApiActivity();
        activity.RecordRequest();
        activity.RecordRequest();

        Assert.True(activity.TryConsume());
        Assert.False(activity.TryConsume());

        activity.RecordRequest();
        Assert.True(activity.HasPendingActivity);
        Assert.True(activity.TryConsume());
    }
}
