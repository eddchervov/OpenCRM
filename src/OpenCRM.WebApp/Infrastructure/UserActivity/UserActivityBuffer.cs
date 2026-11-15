using System.Threading.Channels;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;

namespace OpenCRM.WebApp.Infrastructure.UserActivity;

/// <summary> Ограничивает память и отделяет диагностическую запись от рабочих запросов. </summary>
public sealed class UserActivityBuffer(IOptionsMonitor<UserActivityOptions> options)
{
    private readonly Channel<UserActivityEvent> _channel = Channel.CreateBounded<UserActivityEvent>(
        new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public bool TryWrite(UserActivityEvent activity) => options.CurrentValue.Enabled && this._channel.Writer.TryWrite(activity);

    public bool TryRead(out UserActivityEvent? activity) => this._channel.Reader.TryRead(out activity);
}
