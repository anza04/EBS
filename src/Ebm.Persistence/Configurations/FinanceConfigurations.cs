using Ebm.Domain.Clubs;
using Ebm.Domain.Finances;
using Ebm.Domain.Seasons;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class FinancialTransactionConfiguration : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> builder)
    {
        builder.ToTable("FinancialTransactions");
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Description).HasMaxLength(FinancialTransaction.DescriptionMaxLength);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasIndex(x => new { x.ClubId, x.Date });
    }
}

internal sealed class ClubBudgetConfiguration : IEntityTypeConfiguration<ClubBudget>
{
    public void Configure(EntityTypeBuilder<ClubBudget> builder)
    {
        builder.ToTable("ClubBudgets");
        builder.Ignore(x => x.ExpectedIncome);
        builder.Ignore(x => x.ExpectedCosts);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        builder.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId);
        builder.HasIndex(x => new { x.ClubId, x.SeasonId }).IsUnique();
        builder.HasIndex(x => x.SeasonId);
    }
}
