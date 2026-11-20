using Ebm.Domain.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

/// <summary>News links are plain IDs (no foreign keys): a story outlives records removed in the editor.</summary>
internal sealed class NewsItemConfiguration : IEntityTypeConfiguration<NewsItem>
{
    public void Configure(EntityTypeBuilder<NewsItem> builder)
    {
        builder.ToTable("NewsItems");
        builder.Property(x => x.Key).HasMaxLength(NewsItem.KeyMaxLength);
        builder.Property(x => x.Headline).HasMaxLength(NewsItem.HeadlineMaxLength);
        builder.Property(x => x.Body).HasMaxLength(NewsItem.BodyMaxLength);
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(24);
        builder.HasIndex(x => x.Key).IsUnique();
        builder.HasIndex(x => x.Date);
        builder.HasIndex(x => x.ClubId);
        builder.HasIndex(x => x.OtherClubId);
        builder.HasIndex(x => x.PlayerId);
    }
}

internal sealed class ReputationChangeConfiguration : IEntityTypeConfiguration<ReputationChange>
{
    public void Configure(EntityTypeBuilder<ReputationChange> builder)
    {
        builder.ToTable("ReputationChanges");
        builder.Property(x => x.Key).HasMaxLength(NewsItem.KeyMaxLength);
        builder.Property(x => x.Cause).HasConversion<string>().HasMaxLength(24);
        builder.Ignore(x => x.Points);
        builder.HasIndex(x => x.Key).IsUnique();
        builder.HasIndex(x => new { x.ClubId, x.Date });
    }
}
