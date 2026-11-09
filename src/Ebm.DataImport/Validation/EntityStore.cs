using Ebm.Domain.Common;
using Ebm.Services.Data;

namespace Ebm.DataImport.Validation;

/// <summary>
/// The records of one type known to an import: those already in the database plus those accepted so far,
/// indexed by ID and by duplicate key.
/// </summary>
internal sealed class EntityStore<TEntity, TId>
    where TEntity : Entity<TId>
    where TId : struct, IEntityId<TId>
{
    private readonly Dictionary<TId, TEntity> _byId = [];
    private readonly Dictionary<string, TId> _byKey = new(StringComparer.Ordinal);
    private readonly HashSet<TId> _imported = [];
    private readonly Func<TEntity, string> _key;

    public EntityStore(IRepository<TEntity, TId> repository, IEnumerable<TEntity> existing, Func<TEntity, string> key, Func<TEntity, string> label)
    {
        Repository = repository;
        _key = key;
        Label = label;
        foreach (var entity in existing)
        {
            Index(entity);
        }
    }

    public IRepository<TEntity, TId> Repository { get; }

    public Func<TEntity, string> Label { get; }

    public bool Contains(TId id) => _byId.ContainsKey(id);

    public TEntity? Find(TId id) => _byId.GetValueOrDefault(id);

    public string KeyOf(TEntity entity) => _key(entity);

    public TEntity? FindByKey(string key) => _byKey.TryGetValue(key, out var id) ? _byId[id] : null;

    /// <summary>Whether the record was added or updated by an earlier record of this import.</summary>
    public bool WasImported(TId id) => _imported.Contains(id);

    public void Add(TEntity entity)
    {
        Repository.Add(entity);
        Index(entity);
        _imported.Add(entity.Id);
    }

    /// <summary>Applies <paramref name="update"/> to a stored entity and re-indexes its key.</summary>
    public void Update(TEntity entity, Action<TEntity> update)
    {
        _byKey.Remove(_key(entity));
        try
        {
            update(entity);
        }
        finally
        {
            Index(entity);
        }

        _imported.Add(entity.Id);
    }

    private void Index(TEntity entity)
    {
        _byId[entity.Id] = entity;
        _byKey.TryAdd(_key(entity), entity.Id);
    }
}
