using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using OrderHub.Core.Ai;
using OrderHub.Core.Domain;
using OrderHub.Core.Services;
using OrderHub.Web.Helpers;
using OrderHub.Web.ViewModels;

namespace OrderHub.Web.Controllers;

public class OrdersController : Controller
{
    private const int PageSize = 20;

    private readonly IOrderService _orderService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly IOrderSearchService _orderSearchService;

    public OrdersController(
        IOrderService orderService,
        ICustomerService customerService,
        IProductService productService,
        IOrderSearchService orderSearchService)
    {
        _orderService = orderService;
        _customerService = customerService;
        _productService = productService;
        _orderSearchService = orderSearchService;
    }

    public async Task<IActionResult> Index(int page = 1, OrderStatus? status = null)
    {
        var result = await _orderService.GetOrdersAsync(page, PageSize, status);

        var vm = new OrderListViewModel
        {
            Orders = result.Items.Select(o => new OrderRowViewModel
            {
                Id = o.Id,
                CustomerName = o.Customer?.Name ?? "-",
                Status = o.Status,
                Total = _orderService.CalculateTotal(o),
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt
            }).ToList(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            TotalPages = result.TotalPages,
            Status = status
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q, CancellationToken cancellationToken)
    {
        var vm = new OrderSearchViewModel { Query = q ?? string.Empty };
        if (string.IsNullOrWhiteSpace(q))
            return View(vm);

        try
        {
            var result = await _orderSearchService.SearchAsync(q, cancellationToken);
            if (!result.Success)
                vm.ErrorMessage = result.ErrorMessage;
            else
                vm.Orders = result.Value!.Select(o => new OrderRowViewModel
                {
                    Id = o.Id,
                    CustomerName = o.Customer?.Name ?? "-",
                    Status = o.Status,
                    Total = _orderService.CalculateTotal(o),
                    ItemCount = o.Items.Count,
                    CreatedAt = o.CreatedAt
                }).ToList();
        }
        catch (AiServiceUnavailableException ex)
        {
            vm.ErrorMessage = ex.Message;
        }

        return View(vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _orderService.GetOrderAsync(id);
        if (order is null)
            return NotFound();

        return View(MapToDetails(order));
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new CreateOrderViewModel
        {
            Lines = { new CreateOrderLineViewModel() }
        };
        await PopulateOptionsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateOrderViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(vm);
            return View(vm);
        }

        var lines = vm.Lines
            .Select(l => new NewOrderLine(l.ProductId!.Value, l.Quantity))
            .ToList();

        var result = await _orderService.CreateOrderAsync(vm.CustomerId!.Value, lines);
        if (!result.Success)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);

            await PopulateOptionsAsync(vm);
            return View(vm);
        }

        TempData["Success"] = $"訂單 #{result.Value!.Id} 建立成功";
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var result = await _orderService.CancelOrderAsync(id);
        if (!result.Success)
        {
            TempData["Error"] = result.ErrorMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = $"訂單 #{id} 已取消";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PopulateOptionsAsync(CreateOrderViewModel vm)
    {
        var customers = await _customerService.GetAllAsync();
        var products = await _productService.GetActiveAsync();

        vm.CustomerOptions = customers
            .Select(c => new SelectListItem(
                $"{c.Name}（{DisplayHelper.TierLabel(c.Tier)}）",
                c.Id.ToString()))
            .ToList();

        vm.ProductOptions = products
            .Select(p => new SelectListItem(
                $"{p.Sku} {p.Name}（庫存 {p.StockQuantity}，{DisplayHelper.Money(p.UnitPrice)}）",
                p.Id.ToString()))
            .ToList();
    }

    private OrderDetailsViewModel MapToDetails(Order order)
    {
        var subtotal = _orderService.CalculateSubtotal(order);
        var total = _orderService.CalculateTotal(order);
        var tier = order.Customer?.Tier ?? CustomerTier.Standard;

        return new OrderDetailsViewModel
        {
            Id = order.Id,
            CustomerId = order.CustomerId,
            CustomerName = order.Customer?.Name ?? "-",
            CustomerEmail = order.Customer?.Email ?? "-",
            CustomerTier = tier,
            Status = order.Status,
            CreatedAt = order.CreatedAt,
            Items = order.Items.Select(i => new OrderItemRowViewModel
            {
                ProductSku = i.Product?.Sku ?? "-",
                ProductName = i.Product?.Name ?? "-",
                Quantity = i.Quantity,
                UnitPrice = i.UnitPriceSnapshot,
                LineTotal = i.UnitPriceSnapshot * i.Quantity
            }).ToList(),
            Subtotal = subtotal,
            DiscountRate = _orderService.GetDiscountRate(tier),
            DiscountAmount = subtotal - total,
            Total = total
        };
    }
}
