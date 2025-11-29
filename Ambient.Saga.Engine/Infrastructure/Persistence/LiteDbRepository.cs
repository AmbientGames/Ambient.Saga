using Ambient.Application.Contracts;
using LiteDB;
using System.Linq.Expressions;

namespace Ambient.Saga.Engine.Infrastructure.Persistence;

/// <summary>
/// Generic LiteDB repository implementation.
/// Provides CRUD operations for any entity type.
/// </summary>
internal class LiteDbRepository<T> : IRepository<T> where T : class
{
    private readonly ILiteCollection<T> _collection;

    public LiteDbRepository(ILiteDatabase database, string? collectionName = null)
    {
        if (database == null)
            throw new ArgumentNullException(nameof(database));

        _collection = database.GetCollection<T>(collectionName);
    }

    public Task<T> GetByIdAsync(Guid id)
    {
        var result = _collection.FindById(new BsonValue(id));
        return Task.FromResult(result);
    }

    public Task<Guid> InsertAsync(T obj)
    {
        var bsonValue = _collection.Insert(obj);
        return Task.FromResult(bsonValue.AsGuid);
    }

    public Task<bool> UpdateAsync(T obj)
    {
        var result = _collection.Update(obj);
        return Task.FromResult(result);
    }

    public Task<bool> DeleteAsync(Guid id)
    {
        var result = _collection.Delete(new BsonValue(id));
        return Task.FromResult(result);
    }

    public Task<Guid> UpsertAsync(T obj)
    {
        // LiteDB's Upsert will insert or update based on the _id field
        var result = _collection.Upsert(obj);

        // Get the ID from the object itself
        // This assumes the entity has an Id property or _id field
        var doc = BsonMapper.Global.ToDocument(obj);
        if (doc.TryGetValue("_id", out var idValue))
        {
            return Task.FromResult(idValue.AsGuid);
        }
        else if (doc.TryGetValue("Id", out var idValue2))
        {
            return Task.FromResult(idValue2.AsGuid);
        }

        // If we can't find an ID, return empty (shouldn't happen with proper entities)
        return Task.FromResult(Guid.Empty);
    }

    public Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        var results = _collection.Find(predicate);
        return Task.FromResult(results.AsEnumerable());
    }

    public Task<List<T>> FindManyAsync(List<Guid> ids)
    {
        var results = new List<T>();
        foreach (var id in ids)
        {
            var item = _collection.FindById(new BsonValue(id));
            if (item != null)
                results.Add(item);
        }
        return Task.FromResult(results);
    }

    public Task<IEnumerable<TField>> GetDistinctAsync<TField>(Expression<Func<T, TField>> field)
    {
        var allItems = _collection.FindAll();
        var compiled = field.Compile();
        var distinct = allItems.Select(compiled).Distinct();
        return Task.FromResult(distinct);
    }

    public Task<bool> DeleteManyAsync(Expression<Func<T, bool>> predicate)
    {
        var count = _collection.DeleteMany(predicate);
        return Task.FromResult(count > 0);
    }

    public Task InsertManyAsync(IEnumerable<T> documents)
    {
        _collection.InsertBulk(documents);
        return Task.CompletedTask;
    }

    public Task BulkUpdateAsync(IEnumerable<T> documents)
    {
        foreach (var doc in documents)
        {
            _collection.Update(doc);
        }
        return Task.CompletedTask;
    }

    public IQueryable<T> Query()
    {
        // LiteDB doesn't support IQueryable directly, so we return all items as queryable
        // This is not ideal for large datasets but works for the Sandbox use case
        return _collection.FindAll().AsQueryable();
    }
}
