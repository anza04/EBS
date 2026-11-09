namespace Ebm.Tests.Integration;

/// <summary>The UI talks to the game through Services only; it must not reach into storage or import code.</summary>
public sealed class DesktopArchitectureTests
{
    [Theory]
    [InlineData("Ebm.Persistence")]
    [InlineData("Ebm.DataImport")]
    public void Desktop_does_not_reference_infrastructure_layers_directly(string forbidden)
    {
        var referenced = typeof(Ebm.Desktop.App).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain(forbidden, referenced);
    }
}
