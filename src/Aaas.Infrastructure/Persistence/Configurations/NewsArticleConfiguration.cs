using Aaas.Domain.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aaas.Infrastructure.Persistence.Configurations;

/// <summary>Table News (ADR 0022). La classe s'appelle NewsArticle (conflit avec l'espace de noms), pas la table.</summary>
internal sealed class NewsArticleConfiguration : IEntityTypeConfiguration<NewsArticle>
{
    public void Configure(EntityTypeBuilder<NewsArticle> builder)
    {
        // Filets en base : le Domain applique d'abord les règles.
        builder.ToTable("News", table =>
        {
            table.HasCheckConstraint("CK_News_Status", "[Status] IN (1, 2)");
            // Une actualité publiée a toujours une date ; un brouillon peut en avoir une (déjà publiée puis dépubliée).
            table.HasCheckConstraint("CK_News_PublishedAt", "[Status] = 1 OR [PublishedAt] IS NOT NULL");
        });

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedOnAdd();

        builder.Property(n => n.Title).HasMaxLength(NewsRules.TitleMaxLength).IsRequired();
        // Sans longueur → nvarchar(max) : nvarchar(n) s'arrête à 4 000. La limite métier (8 000) est dans le Domain.
        builder.Property(n => n.Content).IsRequired();

        builder.Property(n => n.Status).HasConversion<byte>();
        builder.Property(n => n.RowVersion).IsRowVersion();

        // Liste publique : filtre sur Status, tri PublishedAt décroissante puis Id (SQL Server lit l'index à l'envers).
        builder.HasIndex(n => new { n.Status, n.PublishedAt, n.Id }).HasDatabaseName("IX_News_Status_PublishedAt");
    }
}
