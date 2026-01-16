// Infrastructure/Repositories/OrderRepository.cs
public class OrderRepository : Repository<Order>, IOrderRepository
{
    public OrderRepository(ApplicationDbContext context) : base(context) { }

    public async Task<List<OrderSummaryDto>> GetOrdersSummaryAsync(
        int customerId,
        DateTime fromDate,
        DateTime toDate)
    {
        // Query ottimizzata con raw SQL per performance
        var sql = @"
            SELECT 
                o.Id,
                o.OrderNumber,
                o.OrderDate,
                o.TotalAmount,
                o.Status,
                COUNT(oi.Id) AS ItemCount
            FROM dbo.Orders o WITH (NOLOCK)
            LEFT JOIN dbo.OrderItems oi WITH (NOLOCK) ON o.Id = oi.OrderId
            WHERE o.CustomerId = @CustomerId
              AND o.OrderDate BETWEEN @FromDate AND @ToDate
            GROUP BY o.Id, o.OrderNumber, o.OrderDate, o.TotalAmount, o.Status
            ORDER BY o.OrderDate DESC
            OPTION (OPTIMIZE FOR (@CustomerId UNKNOWN));
        ";

        return await _context.Database
            .SqlQueryRaw<OrderSummaryDto>(sql,
                new SqlParameter("@CustomerId", customerId),
                new SqlParameter("@FromDate", fromDate),
                new SqlParameter("@ToDate", toDate))
            .ToListAsync();
    }

    public async Task<Dictionary<int, decimal>> GetCustomerOrderTotalsAsync(
        List<int> customerIds,
        CancellationToken cancellationToken)
    {
        // Query efficiente per aggregazione bulk
        var totals = await _context.Orders
            .AsNoTracking()
            .Where(o => customerIds.Contains(o.CustomerId) && 
                       o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.CustomerId)
            .Select(g => new 
            { 
                CustomerId = g.Key, 
                Total = g.Sum(o => o.TotalAmount) 
            })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total, cancellationToken);

        return totals;
    }

    public async Task<PaginatedList<Order>> GetOrdersWithBatchLoadingAsync(
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Usa AsSplitQuery per evitare cartesian explosion
        var query = _context.Orders
            .AsNoTracking()
            .AsSplitQuery() // Genera query separate per navigation properties
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(oi => oi.Product)
            .OrderByDescending(o => o.OrderDate);

        var count = await query.CountAsync(cancellationToken);
        
        var items = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedList<Order>(items, count, pageIndex, pageSize);
    }
}
```

## 3. Frontend Angular 17 con RxJS

### 3.1 Struttura Progetto Angular
```
enterprise-ui/
├── src/
│   ├── app/
│   │   ├── core/                    # Singleton services
│   │   │   ├── services/
│   │   │   │   ├── api.service.ts
│   │   │   │   ├── auth.service.ts
│   │   │   │   └── signalr.service.ts
│   │   │   ├── interceptors/
│   │   │   └── guards/
│   │   ├── shared/                  # Shared components
│   │   │   ├── components/
│   │   │   ├── directives/
│   │   │   ├── pipes/
│   │   │   └── models/
│   │   ├── features/
│   │   │   ├── orders/
│   │   │   │   ├── store/          # NgRx state
│   │   │   │   ├── components/
│   │   │   │   └── services/
│   │   │   ├── customers/
│   │   │   └── dashboard/
│   │   └── app.config.ts
│   └── environments/

  /* Query Ottimizzate con Hints */
