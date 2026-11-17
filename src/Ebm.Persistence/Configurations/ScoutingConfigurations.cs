using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Scouting;
using Ebm.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class ScoutingAssignmentConfiguration : IEntityTypeConfiguration<ScoutingAssignment>
{
    public void Configure(EntityTypeBuilder<ScoutingAssignment> builder)
    {
        builder.ToTable("ScoutingAssignments", t => t.HasCheckConstraint("CK_ScoutingAssignments_Dates", "DueDate > StartDate"));
        builder.Ignore(x => x.Days);
        builder.Property(x => x.Focus).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Position).HasConversion<string>().HasMaxLength(16);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.ScoutId);
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId);
        builder.HasOne<Country>().WithMany().HasForeignKey(x => x.CountryId);
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId);
        builder.HasIndex(x => new { x.Status, x.DueDate });
        builder.HasIndex(x => new { x.ClubId, x.Status });
    }
}

internal sealed class ScoutReportConfiguration : IEntityTypeConfiguration<ScoutReport>
{
    public void Configure(EntityTypeBuilder<ScoutReport> builder)
    {
        builder.ToTable("ScoutReports", t =>
        {
            t.HasCheckConstraint("CK_ScoutReports_Accuracy", $"Accuracy BETWEEN {ScoutingRules.MinAccuracy} AND {ScoutingRules.MaxAccuracy}");
            t.HasCheckConstraint("CK_ScoutReports_Money", "EstimatedValue >= 0 AND EstimatedSalary >= 0");
        });
        builder.Ignore(x => x.OverallRange);
        builder.Ignore(x => x.PotentialRange);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.ScoutId);
        builder.HasOne<ScoutingAssignment>().WithMany().HasForeignKey(x => x.AssignmentId);
        builder.HasIndex(x => new { x.ClubId, x.PlayerId, x.Date });

        // Estimated attributes in their own table (1:1), like the player's true attributes.
        builder.OwnsOne(x => x.Attributes, attributes =>
        {
            attributes.ToTable("ScoutReportAttributes");
            attributes.WithOwner().HasForeignKey("ScoutReportId");
            attributes.HasKey("ScoutReportId");
        });
        builder.Navigation(x => x.Attributes).IsRequired();
    }
}
