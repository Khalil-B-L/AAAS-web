using Aaas.Application.Events;
using Aaas.Application.News;
using Aaas.Application.Reservations;
using Aaas.Domain.News;
using static Aaas.Application.Tests.Common.ValidationTestHelper;

namespace Aaas.Application.Tests.News;

/// <summary>Validation déclarative des DTO d'actualités et pagination après le déplacement de PagingQuery (plan § 9.2).</summary>
public class NewsRequestValidationTests
{
    [Fact(DisplayName = "Création complète : aucune erreur")]
    public void Valid_request() => Assert.Empty(Validate(new CreateNewsRequest { Title = "AG", Content = "Texte" }));

    [Fact(DisplayName = "Titre et texte manquants ou blancs : messages français du Domain")]
    public void Required_fields()
    {
        var errors = Validate(new CreateNewsRequest { Title = "  ", Content = "" });

        Assert.Equal(["Content", "Title"], errors.Keys.Order());
        Assert.Equal(NewsRules.TitleRequiredMessage, errors["Title"]);
        Assert.Equal(NewsRules.ContentRequiredMessage, errors["Content"]);
    }

    [Fact(DisplayName = "Pas de limite de longueur dans le DTO : comptée par le Domain après normalisation des \\r\\n")]
    public void No_length_attribute_on_content()
    {
        var withCrLf = string.Concat(Enumerable.Repeat("abc\r\n", 1999)) + "abcd"; // 9 999 bruts, 8 000 normalisés

        Assert.Empty(Validate(new CreateNewsRequest { Title = "T", Content = withCrLf }));
        Assert.Empty(NewsRules.Validate("T", withCrLf));
    }

    [Theory(DisplayName = "Modification : version absente ou mal formée → erreur sur Version")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAA==")] // 10 octets
    public void Update_requires_valid_version(string version) =>
        Assert.Equal(["Version"], Validate(new UpdateNewsRequest { Title = "T", Content = "C", Version = version }).Keys);

    [Fact(DisplayName = "Modification avec une version valide (Base64 de 8 octets) : aucune erreur")]
    public void Update_valid() =>
        Assert.Empty(Validate(new UpdateNewsRequest { Title = "T", Content = "C", Version = "AAAAAAAAB9E=" }));

    [Fact(DisplayName = "Liste publique : 10 par page par défaut ; liste admin : 20")]
    public void Default_page_sizes()
    {
        Assert.Equal(10, new PublicNewsListQuery().PageSize);
        Assert.Equal(1, new PublicNewsListQuery().Page);
        Assert.Equal(20, new AdminNewsListQuery().PageSize);
        Assert.Null(new AdminNewsListQuery().Status);
    }

    [Fact(DisplayName = "Les listes existantes gardent 20 par défaut après le déplacement de PagingQuery")]
    public void Existing_lists_keep_20()
    {
        Assert.Equal(20, new PublicEventListQuery().PageSize);
        Assert.Equal(20, new AdminEventListQuery().PageSize);
        Assert.Equal(20, new PendingReservationListQuery().PageSize);
        Assert.Equal(20, new MyReservationListQuery().PageSize);
    }

    [Theory(DisplayName = "Pagination des actualités : page 0, pageSize 0 et 51 refusés ; 50 accepté")]
    [InlineData(0, 10, "Page")]
    [InlineData(10_001, 10, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 51, "PageSize")]
    public void Paging_bounds(int page, int pageSize, string field)
    {
        Assert.Equal([field], Validate(new PublicNewsListQuery { Page = page, PageSize = pageSize }).Keys);
        Assert.Equal([field], Validate(new AdminNewsListQuery { Page = page, PageSize = pageSize }).Keys);
        Assert.Empty(Validate(new PublicNewsListQuery { PageSize = 50 }));
    }
}
