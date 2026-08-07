using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using OrderHub.Core.Ai;
using OrderHub.Core.Services;

namespace OrderHub.Web.Controllers.Api;

[ApiController]
[Route("api/orders")]
public class OrdersApiController : ControllerBase
{
    private readonly IOrderSearchService _searchService;
    private readonly IOrderService _orderService;

    public OrdersApiController(IOrderSearchService searchService, IOrderService orderService)
    {
        _searchService = searchService;
        _orderService = orderService;
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchOrdersRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _searchService.SearchAsync(request.Text, cancellationToken);
            if (!result.Success)
                return UnprocessableEntity(new { error = result.ErrorMessage });

            // 金額照舊交給 OrderService 算,不在這裡重複折扣規則(活動 2 同一堂課)
            return Ok(result.Value!.Select(o => new
            {
                o.Id,
                CustomerName = o.Customer?.Name,
                Tier = o.Customer?.Tier.ToString(),
                Status = o.Status.ToString(),
                Total = _orderService.CalculateTotal(o),
                o.CreatedAt
            }));
        }
        catch (AiServiceUnavailableException ex)
        {
            // 上游暫時不可用 → 503 與清楚訊息,不是 500
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }
}

public class SearchOrdersRequest
{
    [Required(ErrorMessage = "text 為必填")]
    public string Text { get; set; } = string.Empty;
}
