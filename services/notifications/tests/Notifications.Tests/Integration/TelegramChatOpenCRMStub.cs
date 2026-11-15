using System.Net;
using System.Collections.Concurrent;

namespace Notifications.Tests.Integration;

/// <summary>Изолирует интеграционные проверки от настоящего core API и Telegram.</summary>
public sealed class TelegramChatOpenCRMStub
{
    private int _requestCount;
    private int _activeRequests;
    private int _maxConcurrentRequests;
    private int _failedRequestsRemaining;
    private readonly ConcurrentDictionary<Guid, int> _requestCounts = new();

    public int RequestCount => Volatile.Read(ref this._requestCount);

    public int MaxConcurrentRequests => Volatile.Read(ref this._maxConcurrentRequests);

    public int GetRequestCount(Guid messageId) => this._requestCounts.GetValueOrDefault(messageId);

    public void FailNextRequests(int count)
    {
        Interlocked.Exchange(ref this._failedRequestsRemaining, count);
    }

    private bool TryConsumeFailure()
    {
        var remaining = Volatile.Read(ref this._failedRequestsRemaining);
        while (remaining > 0)
        {
            var previous = Interlocked.CompareExchange(
                ref this._failedRequestsRemaining,
                remaining - 1,
                remaining);
            if (previous == remaining)
            {
                return true;
            }

            remaining = previous;
        }

        return false;
    }

    public HttpMessageHandler CreateHandler() => new StubHandler(this);

    private sealed class StubHandler(TelegramChatOpenCRMStub owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref owner._requestCount);
            if (TryGetMessageId(request.RequestUri, out var messageId))
            {
                owner._requestCounts.AddOrUpdate(messageId, 1, static (_, count) => count + 1);
            }

            var activeRequests = Interlocked.Increment(ref owner._activeRequests);
            UpdateMaximum(activeRequests);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken);
                if (owner.TryConsumeFailure())
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            finally
            {
                Interlocked.Decrement(ref owner._activeRequests);
            }
        }

        private void UpdateMaximum(int activeRequests)
        {
            var maximum = Volatile.Read(ref owner._maxConcurrentRequests);
            while (activeRequests > maximum)
            {
                var previousMaximum = Interlocked.CompareExchange(
                    ref owner._maxConcurrentRequests,
                    activeRequests,
                    maximum);
                if (previousMaximum == maximum)
                {
                    return;
                }

                maximum = previousMaximum;
            }
        }

        private static bool TryGetMessageId(Uri? requestUri, out Guid messageId)
        {
            messageId = Guid.Empty;
            if (requestUri is null)
            {
                return false;
            }

            var segments = requestUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            return segments.Length >= 5
                && segments[^1] == "claim"
                && Guid.TryParse(segments[^2], out messageId);
        }

    }
}
