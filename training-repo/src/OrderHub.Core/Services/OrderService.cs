using OrderHub.Core.Common;
using OrderHub.Core.Domain;
using OrderHub.Core.Interfaces;

namespace OrderHub.Core.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICustomerRepository _customerRepository;

    public OrderService(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        ICustomerRepository customerRepository)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _customerRepository = customerRepository;
    }

    public Task<PagedResult<Order>> GetOrdersAsync(int page, int pageSize, OrderStatus? status)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        return _orderRepository.GetPagedAsync(page, pageSize, status);
    }

    public Task<Order?> GetOrderAsync(int id) => _orderRepository.GetWithDetailsAsync(id);

    public Task<IReadOnlyList<Order>> GetCustomerOrdersAsync(int customerId) =>
        _orderRepository.GetByCustomerAsync(customerId);

    public async Task<ServiceResult<Order>> CreateOrderAsync(int customerId, IReadOnlyList<NewOrderLine> lines)
    {
        var customer = await _customerRepository.GetByIdAsync(customerId);
        if (customer is null)
            return ServiceResult<Order>.Fail("找不到指定的客戶");

        if (lines is null || lines.Count == 0)
            return ServiceResult<Order>.Fail("訂單至少需要一項商品");

        if (lines.Any(l => l.Quantity <= 0))
            return ServiceResult<Order>.Fail("商品數量必須大於 0");

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
            return ServiceResult<Order>.Fail("同一商品請勿重複加入，請調整數量即可");

        var errors = new List<string>();
        var order = new Order
        {
            CustomerId = customer.Id,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var line in lines)
        {
            var product = await _productRepository.GetByIdAsync(line.ProductId);
            if (product is null || !product.IsActive)
            {
                errors.Add($"商品（Id={line.ProductId}）不存在或已停售");
                continue;
            }

            if (product.StockQuantity < line.Quantity)
            {
                errors.Add($"商品「{product.Name}」庫存不足（現有 {product.StockQuantity}，需求 {line.Quantity}）");
                continue;
            }

            product.StockQuantity -= line.Quantity;

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = line.Quantity,
                UnitPriceSnapshot = product.UnitPrice
            });
        }

        if (errors.Count > 0)
            return ServiceResult<Order>.Fail(errors);

        await _orderRepository.AddAsync(order);
        await _orderRepository.SaveChangesAsync();

        return ServiceResult<Order>.Ok(order);
    }

    public async Task<ServiceResult<Order>> CancelOrderAsync(int id)
    {
        var order = await _orderRepository.GetWithDetailsAsync(id);
        if (order is null)
            return ServiceResult<Order>.Fail("找不到指定的訂單");

        if (order.Status != OrderStatus.Pending && order.Status != OrderStatus.Confirmed)
            return ServiceResult<Order>.Fail($"狀態為 {order.Status} 的訂單不可取消");

        foreach (var item in order.Items)
        {
            var product = await _productRepository.GetByIdAsync(item.ProductId);
            if (product is not null)
                product.StockQuantity += item.Quantity;
        }

        order.Status = OrderStatus.Cancelled;

        await _orderRepository.SaveChangesAsync();

        return ServiceResult<Order>.Ok(order);
    }

    public decimal GetDiscountRate(CustomerTier tier) => tier switch
    {
        CustomerTier.Gold => 0.10m,
        CustomerTier.Silver => 0.05m,
        _ => 0m
    };

    public decimal CalculateSubtotal(Order order) =>
        order.Items.Sum(i => i.UnitPriceSnapshot * i.Quantity);

    public decimal CalculateTotal(Order order)
    {
        var tier = order.Customer?.Tier ?? CustomerTier.Standard;
        var subtotal = CalculateSubtotal(order);
        return Math.Round(subtotal * (1 - GetDiscountRate(tier)), 2);
    }
}
