using Ebm.Domain.Clubs;
using Ebm.Domain.Finances;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Finances;

/// <summary>
/// The only way game code moves a club's money (docs/FINANCES.md §1): changes the balance and writes the ledger row in the
/// caller's unit of work. Data tools (editor, importer) set balances directly without bookings.
/// </summary>
internal static class Ledger
{
    public static void Book(IServiceProvider s, Club club, DateOnly date, FinanceCategory category, long amount, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(club);
        if (amount == 0)
        {
            return;
        }

        club.ChangeBalance(amount);
        s.GetRequiredService<IRepository<FinancialTransaction, FinancialTransactionId>>()
            .Add(new FinancialTransaction(FinancialTransactionId.New(), club.Id, date, category, amount, description));
    }
}
