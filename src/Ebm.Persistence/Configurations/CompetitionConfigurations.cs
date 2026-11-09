using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Countries;
using Ebm.Domain.Seasons;
using Ebm.Domain.Standings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class LeagueConfiguration : IEntityTypeConfiguration<League>
{
    public void Configure(EntityTypeBuilder<League> builder)
    {
        builder.ToTable("Leagues");
        builder.Property(x => x.Name).HasMaxLength(League.NameMaxLength).IsRequired();
        builder.Property(x => x.ShortName).HasMaxLength(League.ShortNameMaxLength).IsRequired();
        builder.HasOne<Country>().WithMany().HasForeignKey(x => x.CountryId);
    }
}

internal sealed class DivisionConfiguration : IEntityTypeConfiguration<Division>
{
    public void Configure(EntityTypeBuilder<Division> builder)
    {
        builder.ToTable("Divisions", t => t.HasCheckConstraint("CK_Divisions_Level", "Level >= 1"));
        builder.Property(x => x.Name).HasMaxLength(Division.NameMaxLength).IsRequired();
        builder.HasOne<League>().WithMany().HasForeignKey(x => x.LeagueId);
        builder.HasIndex(x => new { x.LeagueId, x.Level }).IsUnique();
    }
}

internal sealed class CompetitionConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        builder.ToTable("Competitions", t => t.HasCheckConstraint("CK_Competitions_RulesJson", "json_valid(RulesJson) AND json_type(RulesJson) = 'object'"));
        builder.Property(x => x.Name).HasMaxLength(Competition.NameMaxLength).IsRequired();
        builder.Property(x => x.ShortName).HasMaxLength(Competition.ShortNameMaxLength).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.RulesJson).IsRequired();
        builder.HasOne<Country>().WithMany().HasForeignKey(x => x.CountryId);
        builder.HasOne<Division>().WithMany().HasForeignKey(x => x.DivisionId);
    }
}

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("Seasons", t => t.HasCheckConstraint("CK_Seasons_Dates", "EndDate > StartDate"));
        builder.Property(x => x.Name).HasMaxLength(Season.NameMaxLength).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class CompetitionParticipantConfiguration : IEntityTypeConfiguration<CompetitionParticipant>
{
    public void Configure(EntityTypeBuilder<CompetitionParticipant> builder)
    {
        builder.ToTable("CompetitionParticipants");
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId);
        builder.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasIndex(x => new { x.CompetitionId, x.SeasonId, x.ClubId }).IsUnique();
        builder.HasIndex(x => new { x.ClubId, x.SeasonId });
    }
}

internal sealed class LeagueStandingConfiguration : IEntityTypeConfiguration<LeagueStanding>
{
    public void Configure(EntityTypeBuilder<LeagueStanding> builder)
    {
        builder.ToTable("LeagueStandings", t => t.HasCheckConstraint(
            "CK_LeagueStandings_NonNegative",
            "Won >= 0 AND Lost >= 0 AND PointsFor >= 0 AND PointsAgainst >= 0 AND StandingPoints >= 0"));
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId);
        builder.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasIndex(x => new { x.CompetitionId, x.SeasonId, x.ClubId }).IsUnique();
    }
}
