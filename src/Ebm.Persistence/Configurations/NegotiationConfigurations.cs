using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("Agents");
        builder.Property(x => x.Name).HasMaxLength(Agent.NameMaxLength);
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class AgentRelationshipConfiguration : IEntityTypeConfiguration<AgentRelationship>
{
    public void Configure(EntityTypeBuilder<AgentRelationship> builder)
    {
        builder.ToTable("AgentRelationships");
        builder.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasIndex(x => new { x.AgentId, x.ClubId }).IsUnique();
    }
}

internal sealed class ContractNegotiationConfiguration : IEntityTypeConfiguration<ContractNegotiation>
{
    public void Configure(EntityTypeBuilder<ContractNegotiation> builder)
    {
        builder.ToTable("ContractNegotiations");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(x => x.BrokenOffUntil);
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasOne<Contract>().WithMany().HasForeignKey(x => x.RenewalOfId);
        builder.HasIndex(x => new { x.PlayerId, x.ClubId, x.StartedOn });
    }
}
