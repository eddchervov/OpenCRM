using System.Net;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Infrastructure;

/// <summary>
/// Реализует прикладные часы на основе системного времени UTC.
/// </summary>
public sealed class SystemClock : ISystemClock
{
    /// <summary>Возвращает текущий момент времени в UTC.</summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
