using Aaas.Domain.Common;
using Aaas.Domain.News;

namespace Aaas.Domain.Tests.News;

/// <summary>Cycle de vie d'une actualité (US-E1, ADR 0022) : création, modification, publication, dépublication.</summary>
public class NewsArticleTests
{
    // 10 h à Tunis.
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 10, 0, 0, TimeSpan.FromHours(1));

    private static BusinessRuleException Rejected(Action action) => Assert.Throws<BusinessRuleException>(action);

    private static NewsArticle Draft() => NewsArticle.Create("Assemblée générale", "Premier paragraphe.", Now);

    [Fact(DisplayName = "Création : brouillon, PublishedAt nulle, CreatedAt = UpdatedAt = now en UTC, valeurs normalisées")]
    public void Create_makes_a_normalized_draft()
    {
        var news = NewsArticle.Create("  Assemblée générale  ", "\r\n  Ligne 1\r\nLigne 2\rLigne 3  \n", Now);

        Assert.Equal(NewsStatus.Draft, news.Status);
        Assert.Null(news.PublishedAt);
        Assert.Equal(Now, news.CreatedAt);
        Assert.Equal(TimeSpan.Zero, news.CreatedAt.Offset);
        Assert.Equal(news.CreatedAt, news.UpdatedAt);
        Assert.Equal("Assemblée générale", news.Title);
        Assert.Equal("Ligne 1\nLigne 2\nLigne 3", news.Content);
    }

    [Fact(DisplayName = "Création invalide : filet validation_failed (Application renvoie 400 avant)")]
    public void Create_invalid_throws() =>
        Assert.Equal(ErrorCodes.ValidationFailed, Rejected(() => NewsArticle.Create(" ", "Texte", Now)).Code);

    [Fact(DisplayName = "Le texte est stocké tel quel : aucun encodage HTML (<script> reste <script>)")]
    public void Content_is_not_html_encoded()
    {
        const string text = "<script>alert(1)</script><img src=x onerror=alert(1)> & \"guillemets\"";

        Assert.Equal(text, NewsArticle.Create("T", text, Now).Content);
    }

    [Fact(DisplayName = "Publier un brouillon : Publiée, PublishedAt = now (UTC)")]
    public void Publish_draft()
    {
        var news = Draft();
        var later = Now.AddHours(2);

        news.Publish(later);

        Assert.Equal(NewsStatus.Published, news.Status);
        Assert.Equal(later, news.PublishedAt);
        Assert.Equal(TimeSpan.Zero, news.PublishedAt!.Value.Offset);
        Assert.Equal(later, news.UpdatedAt);
    }

    [Fact(DisplayName = "Publier une publiée : news_state_changed « déjà publiée »")]
    public void Publish_twice_is_refused()
    {
        var news = Draft();
        news.Publish(Now);

        var error = Rejected(() => news.Publish(Now.AddMinutes(1)));

        Assert.Equal(ErrorCodes.NewsStateChanged, error.Code);
        Assert.Equal(NewsRules.AlreadyPublishedMessage, error.Message);
    }

    [Fact(DisplayName = "Dépublier : Brouillon, PublishedAt conservée")]
    public void Unpublish_keeps_published_at()
    {
        var news = Draft();
        news.Publish(Now);

        news.Unpublish(Now.AddDays(1));

        Assert.Equal(NewsStatus.Draft, news.Status);
        Assert.Equal(Now, news.PublishedAt);
        Assert.Equal(Now.AddDays(1), news.UpdatedAt);
    }

    [Fact(DisplayName = "Dépublier un brouillon : news_state_changed « pas publiée »")]
    public void Unpublish_draft_is_refused()
    {
        var error = Rejected(() => Draft().Unpublish(Now));

        Assert.Equal(ErrorCodes.NewsStateChanged, error.Code);
        Assert.Equal(NewsRules.NotPublishedMessage, error.Message);
    }

    [Fact(DisplayName = "Republication : la date d'origine est conservée (QN-2)")]
    public void Republish_keeps_original_date()
    {
        var news = Draft();
        news.Publish(Now);
        news.Unpublish(Now.AddDays(1));

        news.Publish(Now.AddDays(2));

        Assert.Equal(NewsStatus.Published, news.Status);
        Assert.Equal(Now, news.PublishedAt);
        Assert.Equal(Now.AddDays(2), news.UpdatedAt);
    }

    [Theory(DisplayName = "Modifier : permis en brouillon et publiée ; PublishedAt inchangée, UpdatedAt = now")]
    [InlineData(false)]
    [InlineData(true)]
    public void Update_in_both_statuses(bool published)
    {
        var news = Draft();
        if (published)
        {
            news.Publish(Now);
        }

        var publishedAt = news.PublishedAt;
        news.Update(" Nouveau titre ", "Nouveau\r\ntexte", Now.AddHours(3));

        Assert.Equal("Nouveau titre", news.Title);
        Assert.Equal("Nouveau\ntexte", news.Content);
        Assert.Equal(publishedAt, news.PublishedAt);
        Assert.Equal(Now.AddHours(3), news.UpdatedAt);
        Assert.Equal(published ? NewsStatus.Published : NewsStatus.Draft, news.Status);
    }

    [Fact(DisplayName = "Modifier avec un texte trop long : filet validation_failed, rien n'est changé")]
    public void Update_invalid_changes_nothing()
    {
        var news = Draft();

        Assert.Equal(ErrorCodes.ValidationFailed,
            Rejected(() => news.Update("T", new string('a', NewsRules.ContentMaxLength + 1), Now)).Code);
        Assert.Equal("Assemblée générale", news.Title);
    }
}
