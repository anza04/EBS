using Ebm.Domain.Careers;
using Ebm.Domain.Clubs;
using Ebm.Domain.Seasons;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class CareerConfiguration : IEntityTypeConfiguration<Career>
{
    public void Configure(EntityTypeBuilder<Career> builder)
    {
        builder.ToTable("Careers");
        builder.Property(x => x.ManagerName).HasMaxLength(Career.ManagerNameMaxLength);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId);
    }
}
