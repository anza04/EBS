namespace Ebm.Domain.Transfers;

/// <summary>
/// How attractive a club is to players (docs/TRANSFERS.md §2): its reputation plus a bonus for the international
/// competition it plays this season (EuroLeague +10, tier 2 +6, tier 3 +4, tier 4 +2), at most 100. Used as the club
/// reputation in contract negotiations, so clubs in European competitions sign players for less.
/// </summary>
public static class ClubAttractiveness
{
    public static int Bonus(int? internationalTier) => internationalTier switch
    {
        1 => 10,
        2 => 6,
        3 => 4,
        4 => 2,
        _ => 0,
    };

    public static int Of(int reputation, int? internationalTier) => Math.Clamp(reputation + Bonus(internationalTier), 1, 100);
}
