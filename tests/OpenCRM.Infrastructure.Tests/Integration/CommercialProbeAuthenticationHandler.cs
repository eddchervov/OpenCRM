using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class CommercialProbeAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = this.Request.Headers["X-Commercial-Probe-Role"].ToString();
        if (string.IsNullOrEmpty(role))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "commercial-probe-user"), new Claim(ClaimTypes.Role, role)],
            this.Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
