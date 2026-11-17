using Ebm.Domain.Clubs;
using Ebm.Domain.Matches;
using Ebm.Domain.Medical;
using Ebm.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class InjuryConfiguration : IEntityTypeConfiguration<Injury>
{
    public void Configure(EntityTypeBuilder<Injury> builder)
    {
        builder.ToTable("Injuries");
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(x => x.Definition);
        builder.Ignore(x => x.BodyPart);
        builder.Ignore(x => x.IsActive);
        builder.Ignore(x => x.RemainingDays);
        builder.Ignore(x => x.Phase);
        builder.Ignore(x => x.Status);
        builder.Ignore(x => x.Restriction);
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId);
        builder.HasIndex(x => new { x.PlayerId, x.OccurredOn });
        builder.HasIndex(x => x.RecoveredOn);
    }
}
