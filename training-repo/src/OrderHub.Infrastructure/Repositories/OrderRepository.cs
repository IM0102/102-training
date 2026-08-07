using Microsoft.EntityFrameworkCore;
using OrderHub.Core.Ai;
using OrderHub.Core.Common;
using OrderHub.Core.Domain;
using OrderHub.Core.Interfaces;
using OrderHub.Infrastructure.Data;

namespace OrderHub.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly OrderHubDbContext _db;

    public OrderRepository(OrderHubDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<Order>> GetPagedAsync(int page, int pageSize, OrderStatus? status)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Order>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public Task<Order?> GetWithDetailsAsync(int id) =>
        _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

    public async Task<IReadOnlyList<Order>> GetByCustomerAsync(int customerId) =>
        await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

    public async Task<IReadOnlyDictionary<int, int>> GetUnitsSoldSinceAsync(IReadOnlyCollection<int> productIds, DateTime since)
    {
        if (productIds.Count == 0)
            return new Dictionary<int, int>();

        var totals = await _db.OrderItems
            .Where(i => productIds.Contains(i.ProductId)
                && i.Order!.CreatedAt >= since
                && i.Order.Status != OrderStatus.Cancelled)
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Total = g.Sum(i => i.Quantity) })
            .ToListAsync();

        return totals.ToDictionary(x => x.ProductId, x => x.Total);
    }

    public async Task<IReadOnlyList<Order>> SearchAsync(OrderSearchQuery query)
    {
        var q = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .AsQueryable();

        if (query.Status.HasValue)
            q = q.Where(o => o.Status == query.Status.Value);
        if (query.MemberTier.HasValue)
            q = q.Where(o => o.Customer != null && o.Customer.Tier == query.MemberTier.Value);
        if (query.DateFrom.HasValue)
            q = q.Where(o => o.CreatedAt >= query.DateFrom.Value.Date);
        if (query.DateTo.HasValue)
        {
            var endExclusive = query.DateTo.Value.Date.AddDays(1);   // 含當日
            q = q.Where(o => o.CreatedAt < endExclusive);
        }

        // 上限保險:就算條件很寬,也不把整張表倒出來
        return await q.OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync();
    }

    public async Task AddAsync(Order order) => await _db.Orders.AddAsync(order);

    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
