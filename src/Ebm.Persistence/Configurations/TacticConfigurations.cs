using Ebm.Domain.Clubs;
using Ebm.Domain.Tactics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class TacticConfiguration : IEntityTypeConfiguration<Tactic>
{
    public void Configure(EntityTypeBuilder<Tactic> builder)
    {
        builder.ToTable("Tactics");
        builder.Property(x => x.Name).HasMaxLength(Tactic.NameMaxLength);
        builder.Property(x => x.OffensiveSystem).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DefensiveSystem).HasConversion<string>().HasMaxLength(32);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasIndex(x => new { x.ClubId, x.Name }).IsUnique();

        // At most one selected tactic per club.
        builder.HasIndex(x => x.ClubId).IsUnique().HasFilter("IsSelected = 1").HasDatabaseName("IX_Tactics_SelectedPerClub");

        // Settings columns sit in the same table, prefixed Offense / Defense.
        builder.OwnsOne(x => x.Offense, o =>
        {
            foreach (var p in typeof(OffensiveSettings).GetProperties().Where(p => p.CanWrite && p.Name != "EqualityContract"))
            {
                o.Property(p.Name).HasColumnName("Offense" + p.Name);
            }

            o.Property(x => x.Focus).HasConversion<string>().HasMaxLength(16);
        });
        builder.OwnsOne(x => x.Defense, d =>
        {
            foreach (var p in typeof(DefensiveSettings).GetProperties().Where(p => p.CanWrite && p.Name != "EqualityContract"))
            {
                d.Property(p.Name).HasColumnName("Defense" + p.Name);
            }

            d.Property(x => x.Switching).HasConversion<string>().HasMaxLength(16);
            d.Property(x => x.PickAndRollCoverage).HasConversion<string>().HasMaxLength(16);
            d.Property(x => x.Rebounding).HasConversion<string>().HasMaxLength(16);
        });
        builder.Navigation(x => x.Offense).IsRequired();
        builder.Navigation(x => x.Defense).IsRequired();

        // Player assignments: a JSON array in one column (current state, replaced as a whole on save).
        builder.OwnsMany(x => x.Assignments, a =>
        {
            a.ToJson("Assignments");
            a.Property(x => x.Role).HasConversion<string>();
        });
        builder.Navigation(x => x.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_assignments");
    }
}
