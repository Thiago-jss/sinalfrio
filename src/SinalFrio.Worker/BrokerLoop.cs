using Microsoft.Extensions.Logging;

namespace SinalFrio.Worker;

internal static class BrokerLoop
{
    internal static async Task RunAsync(Func<Action, CancellationToken, Task> session, ILogger logger, CancellationToken ct)
    {
        var delay = 1;
        while (!ct.IsCancellationRequested)
        {
            try { await session(() => delay = 1, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Session failed ({FailureType}); retrying in {DelaySeconds}s", error.GetType().Name, delay);
            }
            catch (Exception) when (ct.IsCancellationRequested) { break; }

            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            delay = Math.Min(delay * 2, 15);
        }
    }
}
