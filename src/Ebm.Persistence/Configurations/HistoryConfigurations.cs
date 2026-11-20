using Ebm.Domain.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

/// <summary>History links are plain IDs (no foreign keys): records outlive players, staff or clubs removed in the editor (docs/HISTORY.md).</summary>
internal sealed class SeasonAwardConfiguration : IEntityTypeConfiguration<SeasonAward>
{
    public void Configure(EntityTypeBuilder<SeasonAward> builder)
    {
        builder.ToTable("SeasonAwards");
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
        builder.HasIndex(x => new { x.SeasonId, x.CompetitionId, x.Kind, x.Rank }).IsUnique();
        builder.HasIndex(x => x.PlayerId);
        builder.HasIndex(x => x.ClubId);
    }
}

internal sealed class HallOfFameEntryConfiguration : IEntityTypeConfiguration<HallOfFameEntry>
{
    public void Configure(EntityTypeBuilder<HallOfFameEntry> builder)
    {
        builder.ToTable("HallOfFame");
        builder.HasIndex(x => x.PlayerId).IsUnique();
    }
}

internal sealed class LeagueSeasonSnapshotConfiguration : IEntityTypeConfiguration<LeagueSeasonSnapshot>
{
    public void Configure(EntityTypeBuilder<LeagueSeasonSnapshot> builder)
    {
        builder.ToTable("LeagueSeasonSnapshots");
        builder.HasIndex(x => new { x.CompetitionId, x.SeasonId }).IsUnique();
    }
}
