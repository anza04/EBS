using Ebm.Domain.Chemistry;
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

        /// <summary>Make-probability change of the day (random, wider for inconsistent players, plus morale).</summary>
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
                    (Normal(random) * (0.01 + (0.03 * (100 - p.Player.Attributes.Consistency) / 100.0))) + ChemistryModel.FormEdge(p.Player.Morale))),
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

        /// <summary>The engine's coach makes rotation substitutions even while the user manages the side.</summary>
        public bool AutoSubstitutions { get; set; }

        public int Score { get; set; }

        public int PeriodFouls { get; set; }

        public int TimeoutsLeft { get; set; }

        /// <summary>Points scored since the opponent last scored.</summary>
        public int Run { get; set; }

        /// <summary>Game seconds elapsed at this team's last timeout.</summary>
        public double LastTimeout { get; set; } = double.MinValue;

        /// <summary>The next possession starts with a play drawn up in the team's own timeout.</summary>
        public bool AfterTimeout { get; set; }

        /// <summary>Points from field goals, and those at the rim or in the post: what the opposing coach reads.</summary>
        public int FieldGoalPoints { get; set; }

        public int InsidePoints { get; set; }

        public int Possessions { get; set; }

        /// <summary>Period whose closing minutes the coach already reviewed.</summary>
        public int ClosingReviewed { get; set; }

        public int[] TurnoverCauses { get; } = new int[Enum.GetValues<TurnoverCause>().Length];

        public int FastBreakPossessions { get; set; }

        public int SecondaryPossessions { get; set; }

        public int FastBreakPoints { get; set; }

        public int SecondChancePoints { get; set; }

        public int PassedUp { get; set; }

        public double MeanSpeed => OnCourt.Select(r => (double)r.A.Speed).DefaultIfEmpty(50).Average();

        public TeamAnalytics Analytics() => new(
            Enum.GetValues<TurnoverCause>().ToDictionary(c => c, c => TurnoverCauses[(int)c]),
            Runners.Sum(r => r.Line.Steals),
            FastBreakPossessions,
            SecondaryPossessions,
            FastBreakPoints,
            SecondChancePoints,
            PassedUp);

        /// <summary>Situational plays run, by description (summarised in the tactical notes).</summary>
        public SortedDictionary<string, int> Situations { get; } = new(StringComparer.Ordinal);

        public int ThreeMade => Runners.Sum(r => r.Line.ThreePointMade);

        public int ThreeAttempted => Runners.Sum(r => r.Line.ThreePointAttempted);

        public int Turnovers => Runners.Sum(r => r.Line.Turnovers);

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
