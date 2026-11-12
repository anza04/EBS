using Ebm.Domain.Clubs;
using Ebm.Domain.Countries;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Tactics;

namespace Ebm.Tests.Unit.Matches;

/// <summary>Generated teams with position-typical attributes, for engine tests and statistical validation.</summary>
internal static class SimulationTeams
{
    private static readonly Position[] Roster =
    [
        Position.PointGuard, Position.ShootingGuard, Position.SmallForward, Position.PowerForward, Position.Center,
        Position.PointGuard, Position.ShootingGuard, Position.SmallForward, Position.PowerForward, Position.Center,
        Position.ShootingGuard, Position.PowerForward,
    ];

    private static readonly CountryId Country = CountryId.New();

    public static TeamSheet Team(string name, int quality, int seed, TeamTactics? tactics = null, int players = 12)
    {
        var random = new Random(seed);
        var list = Roster.Take(players).Select((position, i) =>
        {
            // Starters a little better than the bench.
            var level = quality + (i < 5 ? 3 : i < 9 ? 0 : -5);
            return new SheetPlayer(CreatePlayer(position, level, random, $"{name}{i + 1}"), i < 5 ? position : null, i < 5 ? 0 : i - 4);
        }).ToList();
        return new TeamSheet(ClubId.New(), name, list, tactics ?? TeamTactics.Default);
    }

    public static Player CreatePlayer(Position position, int level, Random random, string lastName)
    {
        int Offset(PlayerAttribute a) => position switch
        {
            Position.PointGuard => a switch
            {
                PlayerAttribute.BallHandling or PlayerAttribute.Passing => 14,
                PlayerAttribute.CourtVision or PlayerAttribute.DecisionMaking or PlayerAttribute.Speed => 10,
                PlayerAttribute.ThreePoint or PlayerAttribute.FreeThrow or PlayerAttribute.Steal or PlayerAttribute.PerimeterDefense or PlayerAttribute.Agility => 6,
                PlayerAttribute.OffensiveRebound or PlayerAttribute.DefensiveRebound or PlayerAttribute.InteriorDefense or PlayerAttribute.PostMoves => -15,
                PlayerAttribute.Block or PlayerAttribute.Strength or PlayerAttribute.Dunk => -15,
                _ => 0,
            },
            Position.ShootingGuard => a switch
            {
                PlayerAttribute.ThreePoint or PlayerAttribute.MidRange or PlayerAttribute.FreeThrow => 10,
                PlayerAttribute.BallHandling or PlayerAttribute.PerimeterDefense or PlayerAttribute.Speed or PlayerAttribute.Agility => 5,
                PlayerAttribute.OffensiveRebound or PlayerAttribute.DefensiveRebound or PlayerAttribute.InteriorDefense or PlayerAttribute.PostMoves or PlayerAttribute.Block => -12,
                _ => 0,
            },
            Position.SmallForward => a switch
            {
                PlayerAttribute.Layup or PlayerAttribute.Dunk or PlayerAttribute.PerimeterDefense or PlayerAttribute.Agility => 6,
                PlayerAttribute.ThreePoint or PlayerAttribute.MidRange => 3,
                PlayerAttribute.Block or PlayerAttribute.PostMoves => -5,
                _ => 0,
            },
            Position.PowerForward => a switch
            {
                PlayerAttribute.OffensiveRebound or PlayerAttribute.DefensiveRebound or PlayerAttribute.Strength or PlayerAttribute.InteriorDefense => 9,
                PlayerAttribute.PostMoves or PlayerAttribute.CloseShot or PlayerAttribute.Block or PlayerAttribute.Dunk => 6,
                PlayerAttribute.BallHandling or PlayerAttribute.Passing or PlayerAttribute.Speed or PlayerAttribute.Steal => -8,
                PlayerAttribute.ThreePoint or PlayerAttribute.PerimeterDefense => -5,
                _ => 0,
            },
            _ => a switch
            {
                PlayerAttribute.OffensiveRebound or PlayerAttribute.DefensiveRebound or PlayerAttribute.Block or PlayerAttribute.InteriorDefense => 15,
                PlayerAttribute.Strength or PlayerAttribute.PostMoves or PlayerAttribute.CloseShot or PlayerAttribute.Dunk => 10,
                PlayerAttribute.ThreePoint or PlayerAttribute.BallHandling or PlayerAttribute.Speed or PlayerAttribute.PerimeterDefense => -16,
                PlayerAttribute.Passing or PlayerAttribute.Steal or PlayerAttribute.Agility or PlayerAttribute.FreeThrow => -10,
                _ => 0,
            },
        };

        var attributes = new PlayerAttributes(PlayerCatalog.Attributes.ToDictionary(
            a => a,
            a => Math.Clamp(level + Offset(a) + random.Next(-7, 8), 1, 100)));
        var height = position switch
        {
            Position.PointGuard => 188,
            Position.ShootingGuard => 194,
            Position.SmallForward => 200,
            Position.PowerForward => 205,
            _ => 210,
        } + random.Next(-3, 4);
        var player = new Player(PlayerId.New(), "Sim", lastName, new DateOnly(1998, 1, 1), Country, height, 70 + ((height - 170) * 1), position, attributes);
        player.UpdateProfile(height + 4, PositionSet.Empty, 50, 50, 60);
        return player;
    }
}
