using Aaas.Domain.News;

namespace Aaas.Domain.Tests.News;

/// <summary>Limites, normalisation et extrait d'une actualité (R18, ADR 0023).</summary>
public class NewsRulesTests
{
    private static Dictionary<string, string> Errors(string? title, string? content) =>
        NewsRules.Validate(title, content).ToDictionary(e => e.Field, e => e.Message);

    // ---- Validation ----

    [Theory(DisplayName = "Titre et texte absents, vides ou blancs : obligatoires")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void Required(string? value)
    {
        var errors = Errors(value, value);

        Assert.Equal(NewsRules.TitleRequiredMessage, errors[NewsFields.Title]);
        Assert.Equal(NewsRules.ContentRequiredMessage, errors[NewsFields.Content]);
    }

    [Fact(DisplayName = "Titre : 200 accepté, 201 refusé, espaces autour non comptés")]
    public void Title_length()
    {
        Assert.Empty(Errors(new string('a', 200), "x"));
        Assert.Empty(Errors("  " + new string('a', 200) + "  ", "x"));
        Assert.Equal(NewsRules.TitleTooLongMessage, Errors(new string('a', 201), "x")[NewsFields.Title]);
    }

    [Fact(DisplayName = "Texte : 8 000 accepté, 8 001 refusé, espaces autour non comptés")]
    public void Content_length()
    {
        Assert.Empty(Errors("T", new string('a', 8000)));
        Assert.Empty(Errors("T", "\n  " + new string('a', 8000) + "  \n"));
        Assert.Equal(NewsRules.ContentTooLongMessage, Errors("T", new string('a', 8001))[NewsFields.Content]);
    }

    [Fact(DisplayName = "Texte de 8 000 caractères écrit avec des \\r\\n : accepté (compté après normalisation)")]
    public void Content_length_counted_after_normalization()
    {
        // 1 999 blocs « abc\r\n » + « abcd » : 9 999 caractères bruts, exactement 8 000 une fois \r\n → \n.
        var withCrLf = string.Concat(Enumerable.Repeat("abc\r\n", 1999)) + "abcd";
        Assert.Equal(9999, withCrLf.Length);

        Assert.Empty(Errors("T", withCrLf));
        Assert.Equal(8000, NewsRules.NormalizeContent(withCrLf).Length);
        Assert.Equal(NewsRules.ContentTooLongMessage, Errors("T", withCrLf + "e")[NewsFields.Content]);
    }

    [Fact(DisplayName = "Émojis : la limite compte des unités UTF-16 (un émoji = 2) : 4 000 émojis acceptés, 4 001 refusés")]
    public void Content_length_counts_utf16_units()
    {
        const string rocket = "\U0001F680"; // 2 unités UTF-16
        Assert.Equal(2, rocket.Length);

        Assert.Empty(Errors("T", string.Concat(Enumerable.Repeat(rocket, 4000))));
        Assert.Equal(NewsRules.ContentTooLongMessage,
            Errors("T", string.Concat(Enumerable.Repeat(rocket, 4001)))[NewsFields.Content]);
        Assert.Empty(Errors("T", string.Concat(Enumerable.Repeat(rocket, 3999)) + "ab"));
        Assert.Contains(NewsFields.Content, Errors("T", string.Concat(Enumerable.Repeat(rocket, 3999)) + "abc"));
    }

    [Fact(DisplayName = "Émojis et \\r\\n mêlés, espaces autour : compté après normalisation en unités UTF-16")]
    public void Content_length_mixed_emoji_crlf_and_padding()
    {
        // " \r\n" + 2 000 × « 🚀\r\n » (2 + 1 unités après normalisation) = 6 000, + 2 000 « a » = 8 000, + padding.
        var body = string.Concat(Enumerable.Repeat("\U0001F680\r\n", 2000)) + new string('a', 1999) + "b";
        var padded = " \r\n\t" + body + "\r\n ";

        Assert.Equal(8000, NewsRules.NormalizeContent(padded).Length);
        Assert.Empty(Errors("T", padded));
        Assert.Contains(NewsFields.Content, Errors("T", padded + "c"));
    }

    [Fact(DisplayName = "Normalisation : \\r\\n et \\r deviennent \\n, Trim, null devient vide")]
    public void Normalize()
    {
        Assert.Equal("a\nb\nc", NewsRules.NormalizeContent("  a\r\nb\rc \r\n"));
        Assert.Equal(string.Empty, NewsRules.NormalizeContent(null));
    }

    // ---- Extrait ----

    [Fact(DisplayName = "Extrait : texte court inchangé")]
    public void Excerpt_short() => Assert.Equal("Bonjour à tous.", NewsRules.Excerpt("Bonjour à tous."));

    [Fact(DisplayName = "Extrait : retours à la ligne et espaces multiples réduits à une espace")]
    public void Excerpt_collapses_whitespace() =>
        Assert.Equal("Premier paragraphe. Second paragraphe.", NewsRules.Excerpt("  Premier   paragraphe.\n\n\tSecond paragraphe.\n"));

    [Fact(DisplayName = "Extrait : exactement 200 caractères, sans « … »")]
    public void Excerpt_exactly_200()
    {
        var text = new string('a', 200);

        Assert.Equal(text, NewsRules.Excerpt(text));
    }

    [Fact(DisplayName = "Extrait : 201 caractères, coupé sur la fin du dernier mot entier + « … »")]
    public void Excerpt_cut_on_word()
    {
        // 39 mots de 4 lettres séparés par une espace = 194 caractères, puis « motlong » (7) = 202.
        var text = string.Join(' ', Enumerable.Repeat("mots", 39)) + " motlong";
        Assert.True(text.Length > 200);

        var excerpt = NewsRules.Excerpt(text);

        Assert.Equal(string.Join(' ', Enumerable.Repeat("mots", 39)) + "…", excerpt);
    }

    [Fact(DisplayName = "Extrait : coupe à 200 juste avant une espace : le dernier mot est gardé")]
    public void Excerpt_word_ends_at_limit()
    {
        var text = new string('a', 195) + " bcd " + "suite";
        // Caractère 200 (index 199) = « d », index 200 = espace : le mot « bcd » est entier.
        Assert.Equal(new string('a', 195) + " bcd…", NewsRules.Excerpt(text));
    }

    [Fact(DisplayName = "Extrait : mot unique de 300 caractères → coupe nette à 200 + « … »")]
    public void Excerpt_single_long_word()
    {
        var excerpt = NewsRules.Excerpt(new string('x', 300));

        Assert.Equal(new string('x', 200) + "…", excerpt);
    }

    [Fact(DisplayName = "Extrait : un émoji à cheval sur la position 200 n'est pas coupé en deux")]
    public void Excerpt_does_not_split_surrogate_pair()
    {
        // 199 « a » puis un émoji (2 unités UTF-16 : index 199 et 200), puis du texte.
        var text = new string('a', 199) + "😀" + new string('b', 50);

        var excerpt = NewsRules.Excerpt(text);

        Assert.Equal(new string('a', 199) + "…", excerpt);
        Assert.DoesNotContain(excerpt, char.IsSurrogate);
    }

    [Fact(DisplayName = "Extrait : début seulement (moreFollows) et court après réduction → coupé sur un mot + « … »")]
    public void Excerpt_more_follows()
    {
        Assert.Equal("Un deux…", NewsRules.Excerpt("Un deux tro", moreFollows: true));
        Assert.Equal("Un deux trois…", NewsRules.Excerpt("Un deux trois ", moreFollows: true));
    }
}
