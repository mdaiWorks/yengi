using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent
{
    public interface IAiRouter
    {
        Task<RouterDecision?> RouteAsync(
            string userTask,
            RouterContext context,
            CancellationToken cancellationToken = default);
    }
}
