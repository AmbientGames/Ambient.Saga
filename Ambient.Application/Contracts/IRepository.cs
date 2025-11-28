using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Ambient.Application.Contracts;

public interface IRepository<T> where T : class
{
    // Common CRUD operations
    Task<T> GetByIdAsync(Guid id);
    Task<Guid> InsertAsync(T obj);
    Task<bool> UpdateAsync(T obj);
    Task<bool> DeleteAsync(Guid id);

    /// <summary>
    /// Inserts or updates an entity based on its existence.
    /// </summary>
    /// <param name="obj">The entity to upsert</param>
    /// <returns>The ID of the entity</returns>
    Task<Guid> UpsertAsync(T obj);

    // Query operations
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task<List<T>> FindManyAsync(List<Guid> ids);
    Task<IEnumerable<TField>> GetDistinctAsync<TField>(Expression<Func<T, TField>> field);

    // Bulk operations
    Task<bool> DeleteManyAsync(Expression<Func<T, bool>> predicate);
    Task InsertManyAsync(IEnumerable<T> documents);
    Task BulkUpdateAsync(IEnumerable<T> documents);

    // Queryable interface for EF-like querying
    IQueryable<T> Query();
}