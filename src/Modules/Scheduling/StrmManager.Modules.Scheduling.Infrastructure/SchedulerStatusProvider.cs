using Microsoft.Extensions.Options;
using StrmManager.Modules.Catalog.Application.Status;

namespace StrmManager.Modules.Scheduling.Infrastructure;

internal sealed class SchedulerStatusProvider(IOptions<SchedulingOptions> options) : ISchedulerStatusProvider
{
    public bool Enabled => options.Value.Enabled;
}
