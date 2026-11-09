using Ebm.Domain.Clubs;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class RosterSpotConfiguration : IEntityTypeConfiguration<RosterSpot>
{
    public void Configure(EntityTypeBuilder<RosterSpot> builder)
    {
        builder.ToTable("RosterSpots", t =>
        {
            t.HasCheckConstraint("CK_RosterSpots_LineupPosition", "(Role = 'Starter') = (LineupPosition IS NOT NULL)");
            t.HasCheckConstraint("CK_RosterSpots_RotationOrder", "(Role = 'Rotation') = (RotationOrder IS NOT NULL) AND (RotationOrder IS NULL OR RotationOrder >= 1)");
        });
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.LineupPosition).HasConversion<string>().HasMaxLength(32);
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);

        // One spot per player: the squad of his current club.
        builder.HasIndex(x => x.PlayerId).IsUnique();
        builder.HasIndex(x => x.ClubId);
    }
}
