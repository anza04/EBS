namespace Ebm.Domain.Development;

/// <summary>
/// When players retire (docs/DEVELOPMENT.md §5). Checked once a season, on its last day. Configurable: the host binds
/// these from the <c>Ebm:Retirement</c> settings section; unset values keep the defaults.
/// </summary>
public sealed class RetirementRules
{
    public static RetirementRules Default { get; } = new();

    /// <summary>Nobody retires younger than this.</summary>
    public int MinAge { get; init; } = 33;

    /// <summary>Everybody retires at this age.</summary>
    public int ForcedAge { get; init; } = 41;

    /// <summary>Chance per season at <see cref="MinAge"/>; it grows by the same amount every further year.</summary>
    public double ChancePerYear { get; init; } = 0.12;

    /// <summary>Added for a player without a club.</summary>
    public double FreeAgentBonus { get; init; } = 0.25;

    /// <summary>Added when the overall rating is below <see cref="LowRating"/>.</summary>
    public double LowRatingBonus { get; init; } = 0.15;

    public int LowRating { get; init; } = 40;

    /// <summary>Subtracted when the overall rating is at least <see cref="HighRating"/>.</summary>
    public double HighRatingReduction { get; init; } = 0.10;

    public int HighRating { get; init; } = 70;

    /// <summary>Added while the player is injured.</summary>
    public double InjuryBonus { get; init; } = 0.10;

    /// <summary>A player with more than a season left on his contract retires at this share of the chance.</summary>
    public double UnderContractFactor { get; init; } = 0.5;

    /// <summary>The chance (0–1) that a player retires at the end of this season.</summary>
    /// <param name="contractRunsOn">Whether his contract runs into the next season.</param>
    public double Chance(int age, int overall, bool hasClub, bool contractRunsOn, bool injured)
    {
        if (age >= ForcedAge)
        {
            return 1;
        }

        if (age < MinAge)
        {
            return 0;
        }

        var chance = (age - MinAge + 1) * ChancePerYear
            + (hasClub ? 0 : FreeAgentBonus)
            + (overall < LowRating ? LowRatingBonus : 0)
            - (overall >= HighRating ? HighRatingReduction : 0)
            + (injured ? InjuryBonus : 0);
        return Math.Clamp(chance * (contractRunsOn ? UnderContractFactor : 1), 0, 1);
    }

    /// <summary>Checks the settings: ages 30–50, forced age above the minimum, chances 0–1.</summary>
    public RetirementRules Validated()
    {
        if (MinAge is < 30 or > 50 || ForcedAge <= MinAge || ForcedAge > 50)
        {
            throw new InvalidOperationException($"Retirement ages must satisfy 30 ≤ MinAge < ForcedAge ≤ 50 (got {MinAge} and {ForcedAge}).");
        }

        double[] chances = [ChancePerYear, FreeAgentBonus, LowRatingBonus, HighRatingReduction, InjuryBonus, UnderContractFactor];
        if (chances.Any(c => c is < 0 or > 1))
        {
            throw new InvalidOperationException("Retirement chances and factors must be between 0 and 1.");
        }

        return this;
    }
}
