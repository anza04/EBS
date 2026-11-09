using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class FixtureConfiguration : IEntityTypeConfiguration<Fixture>
{
    public void Configure(EntityTypeBuilder<Fixture> builder)
    {
        builder.ToTable("Fixtures", t =>
        {
            t.HasCheckConstraint("CK_Fixtures_DifferentClubs", "HomeClubId <> AwayClubId");
            t.HasCheckConstraint("CK_Fixtures_Round", "Round >= 1");
        });
        builder.Property(x => x.Stage).HasConversion<string>().HasMaxLength(32).HasDefaultValue(FixtureStage.RegularSeason).HasSentinel((FixtureStage)(-1));
        // No check constraint: on SQLite it would force a non-transactional table rebuild of existing saves.
        builder.Property(x => x.GameNumber).HasDefaultValue(1).HasSentinel(0);
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId);
        builder.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.HomeClubId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.AwayClubId);
        builder.HasIndex(x => new { x.CompetitionId, x.SeasonId, x.Round });
        builder.HasIndex(x => x.Date);
    }
}

internal sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("Matches", t =>
        {
            t.HasCheckConstraint("CK_Matches_NoDraw", "HomeScore <> AwayScore");
            t.HasCheckConstraint("CK_Matches_Scores", "HomeScore >= 0 AND AwayScore >= 0 AND Overtimes >= 0");
        });
        builder.HasOne<Fixture>().WithOne().HasForeignKey<Match>(x => x.FixtureId);
        builder.HasIndex(x => x.FixtureId).IsUnique();
    }
}

internal sealed class PlayerMatchStatisticsConfiguration : IEntityTypeConfiguration<PlayerMatchStatistics>
{
    public void Configure(EntityTypeBuilder<PlayerMatchStatistics> builder)
    {
        builder.ToTable("PlayerMatchStatistics", t => t.HasCheckConstraint(
            "CK_PlayerMatchStatistics_Shots",
            "TwoPointMade BETWEEN 0 AND TwoPointAttempted AND ThreePointMade BETWEEN 0 AND ThreePointAttempted AND FreeThrowMade BETWEEN 0 AND FreeThrowAttempted"));
        builder.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId);
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasIndex(x => new { x.MatchId, x.PlayerId }).IsUnique();
        builder.HasIndex(x => x.PlayerId);

        // Box score columns sit in the same table, without a prefix.
        builder.OwnsOne(x => x.BoxScore, box =>
        {
            foreach (var property in typeof(BoxScore).GetProperties().Where(p => p.CanWrite))
            {
                box.Property(property.Name).HasColumnName(property.Name);
            }
        });
        builder.Navigation(x => x.BoxScore).IsRequired();
    }
}
