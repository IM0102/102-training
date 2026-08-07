using OrderHub.Core.Common;
using OrderHub.Core.Domain;

namespace OrderHub.Core.Services;

public interface IOrderSearchService
{
    Task<ServiceResult<IReadOnlyList<Order>>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
