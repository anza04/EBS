using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;

namespace Ebm.Tests.Unit.Transfers;

public sealed class TransferRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static Contract Running(long salary = 300_000) =>
        Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), new ContractTerms(new DateOnly(2026, 7, 1), new DateOnly(2028, 6, 30), salary));

    [Fact]
    public void A_contract_sent_on_loan_keeps_running_and_comes_back_active()
    {
        var c = Running();
        c.SendOnLoan(Today, new DateOnly(2027, 6, 30));

        Assert.Equal(ContractStatus.OnLoan, c.Status);
        Assert.True(c.IsRunningOn(Today)); // the player is still under contract: nobody else can sign him
        Assert.Equal(ContractStatus.OnLoan, c.StatusOn(Today));
        var blocked = Assert.Throws<DomainValidationException>(() => c.Terminate(Today, 0));
        Assert.Contains("out on loan", blocked.Message, StringComparison.Ordinal);
        Assert.Throws<DomainValidationException>(() => c.Supersede(new DateOnly(2027, 1, 1)));
        Assert.Throws<DomainValidationException>(() => c.SendOnLoan(Today, new DateOnly(2027, 6, 30)));

        c.ReturnFromLoan();
        Assert.Equal(ContractStatus.Active, c.Status);
        Assert.Throws<DomainValidationException>(c.ReturnFromLoan);
    }

    [Fact]
    public void A_loan_must_end_within_the_contract_and_within_two_years()
    {
        var c = Running();
        Assert.Throws<DomainValidationException>(() => c.SendOnLoan(Today, Today)); // at least one day
        Assert.Throws<DomainValidationException>(() => c.SendOnLoan(Today, new DateOnly(2028, 7, 1))); // past the end date
        Assert.Throws<DomainValidationException>(() => c.SendOnLoan(new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 30))); // not running yet

        var five = Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), new ContractTerms(new DateOnly(2026, 7, 1), new DateOnly(2031, 6, 30), 1));
        Assert.Throws<DomainValidationException>(() => five.SendOnLoan(Today, Today.AddYears(3)));
        five.SendOnLoan(Today, Today.AddYears(2));
        Assert.Equal(ContractStatus.OnLoan, five.Status);
    }

    [Fact]
    public void A_transferred_contract_ends_on_the_transfer_date_without_compensation()
    {
        var c = Running();
        c.Transfer(Today);

        Assert.Equal((ContractStatus.Transferred, (DateOnly?)Today, 0L), (c.Status, c.TerminationDate, c.TerminationCompensation));
        Assert.Equal(Today.AddDays(-1), c.EffectiveEndDate);
        Assert.False(c.IsRunningOn(Today));
        Assert.True(c.IsRunningOn(Today.AddDays(-1)));
        Assert.Throws<DomainValidationException>(() => c.Transfer(Today));
        Assert.Throws<DomainValidationException>(() => Running().Transfer(new DateOnly(2028, 7, 1)));
    }

    [Fact]
    public void Commitments_and_the_wage_bill_ignore_players_out_on_loan()
    {
        var stays = Running(100_000);
        var loaned = Running(250_000);
        loaned.SendOnLoan(Today, new DateOnly(2027, 6, 30));

        var c = SalaryCommitments.Calculate([stays, loaned], Today);

        Assert.Equal((100_000L, 1, 0), (c.WageBill, c.RunningContracts, c.UpcomingContracts));
        Assert.Equal(SalaryCalculator.RemainingSalary(stays, Today), c.RemainingSalary);
    }

    [Fact]
    public void Transfer_records_check_their_kind_and_fee()
    {
        var player = PlayerId.New();
        var (from, to) = (ClubId.New(), ClubId.New());
        var (contract, parent) = (ContractId.New(), ContractId.New());

        var signing = Transfer.Signing(TransferId.New(), player, null, to, Today, contract);
        Assert.Equal((TransferKind.FreeAgentSigning, (ClubId?)null, 0L, false), (signing.Kind, signing.FromClubId, signing.Fee, signing.IsOpenLoan));

        Assert.Equal(TransferKind.Transfer, Transfer.Permanent(TransferId.New(), player, from, to, Today, 500_000, contract, viaReleaseClause: false).Kind);
        Assert.Equal(TransferKind.ReleaseClause, Transfer.Permanent(TransferId.New(), player, from, to, Today, 500_000, contract, viaReleaseClause: true).Kind);
        Assert.Throws<DomainValidationException>(() => Transfer.Permanent(TransferId.New(), player, to, to, Today, 0, contract, false)); // same club
        Assert.Throws<DomainValidationException>(() => Transfer.Permanent(TransferId.New(), player, from, to, Today, -1, contract, false));
        Assert.Throws<DomainValidationException>(() => Transfer.Permanent(TransferId.New(), player, from, to, Today, Transfer.MaxFee + 1, contract, false));

        Assert.Throws<DomainValidationException>(() => Transfer.OnLoan(TransferId.New(), player, from, to, Today, Today, 0, contract, parent));
        Assert.Throws<DomainValidationException>(() => Transfer.OnLoan(TransferId.New(), player, from, to, Today, Today.AddYears(2).AddDays(1), 0, contract, parent));
        var loan = Transfer.OnLoan(TransferId.New(), player, from, to, Today, new DateOnly(2027, 6, 30), 20_000, contract, parent);
        Assert.True(loan.IsOpenLoan);
        Assert.Equal((new DateOnly(2027, 6, 30), parent), (loan.LoanEndDate!.Value, loan.ParentContractId!.Value));
        Assert.Throws<DomainValidationException>(() => loan.Return(Today)); // not before it started
        loan.Return(new DateOnly(2027, 7, 1));
        Assert.False(loan.IsOpenLoan);
        Assert.Throws<DomainValidationException>(() => loan.Return(new DateOnly(2027, 7, 2)));
        Assert.Throws<DomainValidationException>(() => signing.Return(new DateOnly(2027, 7, 1)));

        var back = Transfer.LoanReturn(TransferId.New(), player, to, from, new DateOnly(2027, 7, 1), parent);
        Assert.Equal((TransferKind.LoanReturn, (ClubId?)to, from, (ContractId?)null), (back.Kind, back.FromClubId, back.ToClubId, back.ContractId));
    }

    [Fact]
    public void The_strictest_squad_rules_take_the_tightest_limit_of_each_setting()
    {
        var rules = SquadRules.Strictest(
        [
            SquadRules.Default,
            new SquadRules { MaxPlayers = 14, MaxForeignPlayers = 5, MinPlayers = 11 },
            new SquadRules { MaxForeignPlayers = 3, MinAge = 16, MatchdayPlayers = 11 },
        ]);

        Assert.Equal((11, 14, 11, 3, 16), (rules.MinPlayers, rules.MaxPlayers, rules.MatchdayPlayers, rules.MaxForeignPlayers, rules.MinAge));
        Assert.Same(SquadRules.Default, SquadRules.Strictest([]));
        Assert.Same(SquadRules.Default, SquadRules.Strictest([new SquadRules { MinPlayers = 16 }, new SquadRules { MaxPlayers = 12 }])); // contradictory
    }
}
