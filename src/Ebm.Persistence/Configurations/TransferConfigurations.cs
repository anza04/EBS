using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable("Transfers", t =>
        {
            t.HasCheckConstraint("CK_Transfers_Fee", "Fee >= 0");
            t.HasCheckConstraint("CK_Transfers_Clubs", "FromClubId IS NULL OR FromClubId <> ToClubId");
            t.HasCheckConstraint("CK_Transfers_Loan", "(Kind = 'Loan') = (LoanEndDate IS NOT NULL) AND (LoanEndDate IS NULL OR LoanEndDate > Date)");
            t.HasCheckConstraint("CK_Transfers_Return", "ReturnedOn IS NULL OR (Kind = 'Loan' AND ReturnedOn > Date)");
        });
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.FromClubId);
        builder.HasOne<Club>().WithMany().HasForeignKey(x => x.ToClubId);
        builder.HasOne<Contract>().WithMany().HasForeignKey(x => x.ContractId);
        builder.HasOne<Contract>().WithMany().HasForeignKey(x => x.ParentContractId);
        builder.HasIndex(x => new { x.PlayerId, x.Date });
        builder.HasIndex(x => new { x.ToClubId, x.Date });
        builder.HasIndex(x => new { x.FromClubId, x.Date });
        builder.HasIndex(x => new { x.Kind, x.ReturnedOn });
    }
}
