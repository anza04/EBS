using Ebm.Domain.Players;

namespace Ebm.Tests.Unit.Domain;

internal static class TestData
{
    public static readonly DateOnly Day = new(2026, 9, 1);

    public static PlayerAttributes Attributes(int value = 50) => new(value, value, value, value, value, value, value, value);
}
