using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Nebula.Application.Common;

public static class ListQueryExtensions
{
    /// <summary>
    /// Orders by the whitelisted <paramref name="sort"/> key (unknown or missing ⇒ <paramref name="defaultSort"/>),
    /// descending unless <paramref name="dir"/> is <c>asc</c>. The optional <paramref name="tieBreaker"/> is applied
    /// in the same direction so paging is deterministic. Everything is translated to SQL.
    /// </summary>
    public static IOrderedQueryable<T> ApplySort<T>(
        this IQueryable<T> q,
        string? sort,
        string? dir,
        IReadOnlyDictionary<string, Expression<Func<T, object?>>> sortable,
        string defaultSort,
        Expression<Func<T, object?>>? tieBreaker = null)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(sortable);

        var key = sort is not null && sortable.TryGetValue(sort, out var chosen) ? chosen : sortable[defaultSort];
        var descending = dir != "asc";

        var ordered = Order(q, key, descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy));
        return tieBreaker is null
            ? ordered
            : Order(ordered, tieBreaker, descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy));
    }

    /// <summary>Counts, then fetches one page and maps it in memory.</summary>
    public static async Task<Paged<TDto>> ToPagedAsync<T, TDto>(
        this IQueryable<T> q, ListQuery lq, Func<T, TDto> map, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(lq);
        ArgumentNullException.ThrowIfNull(map);

        var total = await q.CountAsync(ct);
        var skip = (long)(lq.Page - 1) * lq.PageSize;
        if (skip >= total)
        {
            return new Paged<TDto>([], total, lq.Page, lq.PageSize);
        }

        var rows = await q.Skip((int)skip).Take(lq.PageSize).ToListAsync(ct);
        return new Paged<TDto>(rows.Select(map).ToList(), total, lq.Page, lq.PageSize);
    }

    /// <summary>Calls <c>Queryable.{method}</c> with the key's real type, so value-type keys are not boxed in SQL.</summary>
    private static IOrderedQueryable<T> Order<T>(IQueryable<T> q, Expression<Func<T, object?>> key, string method)
    {
        var body = key.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert
            ? convert.Operand
            : key.Body;
        var lambda = Expression.Lambda(body, key.Parameters);
        var call = Expression.Call(
            typeof(Queryable), method, [typeof(T), body.Type], q.Expression, Expression.Quote(lambda));
        return (IOrderedQueryable<T>)q.Provider.CreateQuery<T>(call);
    }
}
