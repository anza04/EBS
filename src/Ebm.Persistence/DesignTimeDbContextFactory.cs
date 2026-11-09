using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ebm.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> to create migrations. The application builds the context through DI.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EbmDbContext>
{
    public EbmDbContext CreateDbContext(string[] args)
    {
        var path = Path.Combine(Path.GetTempPath(), "ebm-design-time.db");
        var options = new DbContextOptionsBuilder<EbmDbContext>()
            .UseSqlite(new PersistenceOptions(path).ConnectionString)
            .Options;
        return new EbmDbContext(options);
    }
}
