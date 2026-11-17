namespace Ebm.Domain.Medical;

/// <summary>Where on the body an injury is. Recurrence risk is per body part (docs/INJURIES.md §4).</summary>
public enum BodyPart
{
    Ankle,
    Knee,
    Thigh,
    Hip,
    Leg,
    Foot,
    Back,
    Hand,
    Shoulder,
    Head,
    Illness,
}

/// <summary>The kinds of injury the game knows (docs/INJURIES.md §1).</summary>
public enum InjuryType
{
    AnkleSprain,
    KneeSprain,
    TornAcl,
    TornMeniscus,
    PatellarTendinitis,
    HamstringStrain,
    ThighBruise,
    GroinStrain,
    CalfStrain,
    AchillesRupture,
    StressFracture,
    PlantarFasciitis,
    BackSpasms,
    FingerSprain,
    BrokenHand,
    ShoulderSprain,
    Concussion,
    Illness,
}

/// <summary>How serious an injury is, from its recovery time: minor up to a week, moderate up to three, serious up to eight, severe beyond.</summary>
public enum InjurySeverity
{
    Minor,
    Moderate,
    Serious,
    Severe,
}

/// <summary>One injury type: body part, how common it is, its recovery range in days and whether it comes from contact.</summary>
/// <param name="Weight">Relative frequency among injuries.</param>
/// <param name="Contact">Contact injuries are rarer in training (<see cref="InjuryCatalog.TrainingContactFactor"/>).</param>
public sealed record InjuryDefinition(InjuryType Type, BodyPart BodyPart, string Name, double Weight, int MinDays, int MaxDays, bool Contact);

/// <summary>The injury types and their recovery ranges (docs/INJURIES.md §1).</summary>
public static class InjuryCatalog
{
    /// <summary>Contact injuries are this much less likely to be the type of a training injury.</summary>
    public const double TrainingContactFactor = 0.3;

    public static readonly IReadOnlyList<InjuryDefinition> All =
    [
        new(InjuryType.AnkleSprain, BodyPart.Ankle, "Ankle sprain", 22, 3, 35, true),
        new(InjuryType.KneeSprain, BodyPart.Knee, "Knee sprain", 9, 7, 42, true),
        new(InjuryType.TornAcl, BodyPart.Knee, "Torn ACL", 0.8, 200, 300, false),
        new(InjuryType.TornMeniscus, BodyPart.Knee, "Torn meniscus", 2, 30, 90, false),
        new(InjuryType.PatellarTendinitis, BodyPart.Knee, "Patellar tendinitis", 6, 3, 21, false),
        new(InjuryType.HamstringStrain, BodyPart.Thigh, "Hamstring strain", 9, 7, 35, false),
        new(InjuryType.ThighBruise, BodyPart.Thigh, "Thigh bruise", 8, 1, 10, true),
        new(InjuryType.GroinStrain, BodyPart.Hip, "Groin strain", 6, 5, 28, false),
        new(InjuryType.CalfStrain, BodyPart.Leg, "Calf strain", 6, 5, 28, false),
        new(InjuryType.AchillesRupture, BodyPart.Leg, "Achilles rupture", 0.4, 180, 300, false),
        new(InjuryType.StressFracture, BodyPart.Foot, "Stress fracture", 1.5, 42, 100, false),
        new(InjuryType.PlantarFasciitis, BodyPart.Foot, "Plantar fasciitis", 3, 7, 42, false),
        new(InjuryType.BackSpasms, BodyPart.Back, "Back spasms", 8, 2, 14, false),
        new(InjuryType.FingerSprain, BodyPart.Hand, "Finger sprain", 8, 1, 10, true),
        new(InjuryType.BrokenHand, BodyPart.Hand, "Broken hand", 1.5, 28, 56, true),
        new(InjuryType.ShoulderSprain, BodyPart.Shoulder, "Shoulder sprain", 3, 7, 28, true),
        new(InjuryType.Concussion, BodyPart.Head, "Concussion", 2.5, 5, 21, true),
        new(InjuryType.Illness, BodyPart.Illness, "Illness", 5, 1, 7, false),
    ];

    private static readonly Dictionary<InjuryType, InjuryDefinition> ByType = All.ToDictionary(d => d.Type);

    public static InjuryDefinition Of(InjuryType type) =>
        ByType.TryGetValue(type, out var definition) ? definition : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown injury type.");

    public static InjurySeverity SeverityFor(int recoveryDays) => recoveryDays switch
    {
        <= 7 => InjurySeverity.Minor,
        <= 21 => InjurySeverity.Moderate,
        <= 56 => InjurySeverity.Serious,
        _ => InjurySeverity.Severe,
    };
}
