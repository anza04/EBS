using Ebm.Domain.Chemistry;
using Ebm.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class PlayerRelationshipConfiguration : IEntityTypeConfiguration<PlayerRelationship>
{
    public void Configure(EntityTypeBuilder<PlayerRelationship> builder)
    {
        builder.ToTable("PlayerRelationships");
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerAId);
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerBId);
        builder.HasIndex(x => new { x.PlayerAId, x.PlayerBId }).IsUnique();
        builder.HasIndex(x => x.PlayerBId);
    }
}
