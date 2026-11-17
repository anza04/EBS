using Ebm.Domain.Clubs;
using Ebm.Domain.Development;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class TrainingPlanConfiguration : IEntityTypeConfiguration<TrainingPlan>
{
    public void Configure(EntityTypeBuilder<TrainingPlan> builder)
    {
        builder.ToTable("TrainingPlans");
        builder.Property(x => x.TeamFocus).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Intensity).HasConversion<string>().HasMaxLength(16);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasIndex(x => x.ClubId).IsUnique();

        // Individual programmes: a JSON array in one column (current state, replaced as a whole on save).
        builder.OwnsMany(x => x.Individuals, i =>
        {
            i.ToJson("Individuals");
            i.Property(x => x.Focus).HasConversion<string>();
        });
        builder.Navigation(x => x.Individuals).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_individuals");
    }
}
