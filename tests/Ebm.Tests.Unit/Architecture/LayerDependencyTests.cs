using System.Reflection;

namespace Ebm.Tests.Unit.Architecture;

/// <summary>
/// Enforces the module boundaries described in docs/TECHNICAL_ARCHITECTURE.md.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(Ebm.Domain.Common.DomainException).Assembly;
    private static readonly Assembly Services = typeof(Ebm.Services.ServiceCollectionExtensions).Assembly;
    private static readonly Assembly Persistence = typeof(Ebm.Persistence.ServiceCollectionExtensions).Assembly;
    private static readonly Assembly DataImport = typeof(Ebm.DataImport.ServiceCollectionExtensions).Assembly;

    public static TheoryData<string, string[]> ForbiddenReferences => new()
    {
        { "Ebm.Domain", ["Ebm.Services", "Ebm.Persistence", "Ebm.DataImport", "Ebm.Hosting", "Ebm.Desktop"] },
        { "Ebm.Services", ["Ebm.Persistence", "Ebm.DataImport", "Ebm.Hosting", "Ebm.Desktop"] },
        { "Ebm.Persistence", ["Ebm.DataImport", "Ebm.Hosting", "Ebm.Desktop"] },
        { "Ebm.DataImport", ["Ebm.Persistence", "Ebm.Hosting", "Ebm.Desktop"] },
    };

    [Theory]
    [MemberData(nameof(ForbiddenReferences))]
    public void Layer_does_not_reference_forbidden_layers(string layer, string[] forbidden)
    {
        var referenced = Load(layer).GetReferencedAssemblies().Select(a => a.Name).ToList();

        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(name, referenced);
        }
    }

    [Fact]
    public void Domain_has_no_package_dependencies()
    {
        var referenced = Domain.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain(referenced, n => n.StartsWith("Microsoft.Extensions", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Serilog", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_persistence_depends_on_entity_framework()
    {
        foreach (var assembly in new[] { Domain, Services, DataImport })
        {
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                a => a.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Lower_layers_do_not_depend_on_logging_implementation()
    {
        foreach (var assembly in new[] { Domain, Services, Persistence, DataImport })
        {
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                a => a.Name!.StartsWith("Serilog", StringComparison.Ordinal));
        }
    }

    private static Assembly Load(string name) => name switch
    {
        "Ebm.Domain" => Domain,
        "Ebm.Services" => Services,
        "Ebm.Persistence" => Persistence,
        "Ebm.DataImport" => DataImport,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };
}
