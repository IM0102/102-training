using Microsoft.AspNetCore.Mvc;
using OrderHub.Core.Services;
using OrderHub.Web.ViewModels;

namespace OrderHub.Web.Controllers;

public class CustomersController : Controller
{
    private readonly ICustomerService _customerService;
    private readonly IOrderService _orderService;

    public CustomersController(ICustomerService customerService, IOrderService orderService)
    {
        _customerService = customerService;
        _orderService = orderService;
    }

    public async Task<IActionResult> Index()
    {
        var customers = await _customerService.GetAllAsync();

        var vm = new CustomerListViewModel
        {
            Customers = customers.Select(c => new CustomerRowViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Tier = c.Tier,
                CreatedAt = c.CreatedAt
            }).ToList()
        };

        return View(vm);
    }

    [HttpGet("Customers/{id:int}/Orders")]
    public async Task<IActionResult> Orders(int id)
    {
        var customer = await _customerService.GetByIdAsync(id);
        if (customer is null)
            return NotFound();

        var orders = await _orderService.GetCustomerOrdersAsync(id);

        var vm = new CustomerOrdersViewModel
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CustomerEmail = customer.Email,
            Tier = customer.Tier,
            Orders = orders.Select(o => new OrderRowViewModel
            {
                Id = o.Id,
                CustomerName = customer.Name,
                Status = o.Status,
                Total = _orderService.CalculateTotal(o),
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt
            }).ToList()
        };

        return View(vm);
    }
}
