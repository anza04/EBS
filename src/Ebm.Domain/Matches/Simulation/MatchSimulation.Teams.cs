using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Matches.Simulation;

public sealed partial class MatchSimulation
{
    /// <summary>Lowest energy a player starts with, whatever his fitness.</summary>
    private const double MinStartEnergy = 40;

    private static readonly Position[] Slots = Enum.GetValues<Position>();

    /// <summary>A player in the match: his line, ratings, assignment and form of the day.</summary>
    private sealed class Runner
    {
        private readonly int[] _ratings;

        public Runner(SheetPlayer sheet, PlayerEvaluation evaluation, TacticalAssignment assignment, double form)
        {
            Player = sheet.Player;
            Priority = sheet.Priority;
            Assignment = assignment;
            Form = form;
            _ratings = [.. Slots.Select(p => evaluation.At(p).EffectiveRating)];
            MaxEnergy = Math.Max(MinStartEnergy, Player.Fitness);
            Line = new PlayerLine(Player, sheet.StartingPosition) { Energy = MaxEnergy };
        }

        public Player Player { get; }

        public PlayerAttributes A => Player.Attributes;

        public PlayerTendencies T => Player.Tendencies;

        public string Name => Player.LastName;

        public PlayerLine Line { get; }

        public int Priority { get; }

        public TacticalAssignment Assignment { get; set; }

        /// <summary>Make-probability change of the day (random, wider for inconsistent players).</summary>
        public double Form { get; }

        public double MaxEnergy { get; }

        public double Energy
        {
            get => Line.Energy;
            set => Line.Energy = Math.Clamp(value, 0, MaxEnergy);
        }

        public int Rating(Position position) => _ratings[(int)position];
    }

    /// <summary>A team's state: players, the five on court (one per slot, guards first), score, fouls and timeouts.</summary>
    private sealed class Team
    {
        public Team(TeamSide side, TeamSheet sheet, PlayerRatingCalculator calculator, Random random)
        {
            Side = side;
            Sheet = sheet;
            Tactics = sheet.Tactics;
            Runners =
            [
                .. sheet.Players.Select(p => new Runner(
                    p,
                    calculator.Evaluate(p.Player),
                    sheet.Tactics.AssignmentOf(p.Player),
                    Normal(random) * (0.01 + (0.03 * (100 - p.Player.Attributes.Consistency) / 100.0)))),
            ];
            foreach (var runner in Runners.Where(r => r.Line.StartingPosition is not null))
            {
                Court[(int)runner.Line.StartingPosition!.Value] = runner;
                runner.Line.IsOnCourt = true;
            }
        }

        public TeamSide Side { get; }

        public TeamSheet Sheet { get; }

        public List<Runner> Runners { get; }

        public Runner?[] Court { get; } = new Runner?[5];

        public IEnumerable<Runner> OnCourt => Court.Where(r => r is not null)!;

        public TeamTactics Tactics { get; private set; }

        public bool Automatic { get; set; } = true;

        public int Score { get; set; }

        public int PeriodFouls { get; set; }

        public int TimeoutsLeft { get; set; }

        /// <summary>Points scored since the opponent last scored.</summary>
        public int Run { get; set; }

        /// <summary>Game seconds elapsed at this team's last timeout.</summary>
        public double LastTimeout { get; set; } = double.MinValue;

        public Runner? Find(PlayerId id) => Runners.FirstOrDefault(r => r.Player.Id == id);

        public void SetTactics(TeamTactics tactics)
        {
            Tactics = tactics;
            foreach (var runner in Runners)
            {
                runner.Assignment = tactics.AssignmentOf(runner.Player);
            }
        }

        public int SlotOf(Runner runner) => Array.IndexOf(Court, runner);
    }

    private static double Normal(Random random)
    {
        // Box–Muller.
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
