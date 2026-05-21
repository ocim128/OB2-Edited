using Microsoft.EntityFrameworkCore;
using Flux.Core.Entities;
using System.Text.Json.Serialization;

namespace Flux.Web.Models.Pagination;

/// <summary>
/// List with pagination features.
/// </summary>
public class PagedList<T>
{
    /// <summary>
    /// The maximum number of items that can be requested in one page.
    /// </summary>
    public const int MaxPageSize = 500;

    /// <summary>
    /// Parameterless constructor for serialization.
    /// </summary>
    [JsonConstructor]
    public PagedList()
    {
    }

    /// <summary></summary>
    public PagedList(IEnumerable<T> items, int totalCount, int pageNumber,
        int pageSize)
    {
        PageNumber = pageNumber;
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        PageSize = pageSize;
        TotalCount = totalCount;
        Items = items.ToList();
    }

    /// <summary>
    /// The list of items.
    /// </summary>
    [JsonInclude]
    public List<T> Items { get; private set; } = [];

    /// <summary>
    /// The current page.
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// The total number of pages.
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// The page size.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// The total number of items.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Creates a paged list from an <see cref="IQueryable{T}" />, useful
    /// for DB calls to optimize the query.
    /// </summary>
    public static async Task<PagedList<TEntity>> CreateAsync<TEntity>(
        IQueryable<TEntity> source,
        int pageNumber, int pageSize,
        CancellationToken cancellationToken = default) where TEntity : Entity
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var count = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize).ToListAsync(cancellationToken);

        return new PagedList<TEntity>(items, count, pageNumber, pageSize);
    }
}
