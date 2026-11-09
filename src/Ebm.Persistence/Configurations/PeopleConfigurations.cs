using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players", t =>
        {
            t.HasCheckConstraint("CK_Players_Height", $"HeightCm BETWEEN {Player.MinHeightCm} AND {Player.MaxHeightCm}");
            t.HasCheckConstraint("CK_Players_Weight", $"WeightKg BETWEEN {Player.MinWeightKg} AND {Player.MaxWeightKg}");
        });
        builder.Property(x => x.FirstName).HasMaxLength(Player.NameMaxLength).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(Player.NameMaxLength).IsRequired();
        builder.Property(x => x.Position).HasConversion<string>().HasMaxLength(32);
        builder.HasOne<Country>().WithMany().HasForeignKey(x => x.NationalityId);
        builder.HasIndex(x => new { x.LastName, x.FirstName });

        // Attributes live in their own table (1:1, keyed by PlayerId) and always load with the player.
        builder.OwnsOne(x => x.Attributes, attributes =>
        {
            attributes.ToTable("PlayerAttributes", t => t.HasCheckConstraint(
                "CK_PlayerAttributes_Range",
                string.Join(" AND ", RatingColumns.Select(c => $"{c} BETWEEN {PlayerAttributes.MinRating} AND {PlayerAttributes.MaxRating}"))));
            attributes.WithOwner().HasForeignKey("PlayerId");
            attributes.HasKey("PlayerId");
        });
        builder.Navigation(x => x.Attributes).IsRequired();
    }

    private static readonly string[] RatingColumns =
    [
        nameof(PlayerAttributes.Shooting), nameof(PlayerAttributes.InsideScoring), nameof(PlayerAttributes.Passing),
        nameof(PlayerAttributes.BallHandling), nameof(PlayerAttributes.Rebounding), nameof(PlayerAttributes.Defense),
        nameof(PlayerAttributes.Athleticism), nameof(PlayerAttributes.BasketballIq),
    ];
}

internal sealed class PlayerClubHistoryEntryConfiguration : IEntityTypeConfiguration<PlayerClubHistoryEntry>
{
    public void Configure(EntityTypeBuilder<PlayerClubHistoryEntry> builder)
    {
        builder.ToTable("PlayerClubHistory", t => t.HasCheckConstraint("CK_PlayerClubHistory_Dates", "EndDate IS NULL OR EndDate >= StartDate"));
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasIndex(x => new { x.PlayerId, x.StartDate });
        builder.HasIndex(x => x.ClubId);

        // At most one current (open) club spell per player.
        builder.HasIndex(x => x.PlayerId).IsUnique().HasFilter("EndDate IS NULL").HasDatabaseName("IX_PlayerClubHistory_PlayerId_Current");
    }
}

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("Staff");
        builder.Property(x => x.FirstName).HasMaxLength(StaffMember.NameMaxLength).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(StaffMember.NameMaxLength).IsRequired();
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        builder.HasOne<Country>().WithMany().HasForeignKey(x => x.NationalityId);
    }
}

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("Contracts", t =>
        {
            t.HasCheckConstraint("CK_Contracts_OnePerson", "(PlayerId IS NULL) <> (StaffMemberId IS NULL)");
            t.HasCheckConstraint("CK_Contracts_Dates", "EndDate > StartDate");
            t.HasCheckConstraint("CK_Contracts_Salary", "AnnualSalary >= 0");
        });
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId);
    }
}
