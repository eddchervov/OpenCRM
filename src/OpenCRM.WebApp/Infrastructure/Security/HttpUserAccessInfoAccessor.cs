using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;
using OpenCRM.WebApp.Controllers.Api.V1;
using OpenCRM.WebApp.Controllers.Auth;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Получает ограниченный снимок клиента без секретов и содержимого cookie. </summary>
public sealed class HttpUserAccessInfoAccessor(
    IHttpContextAccessor contextAccessor,
    IDataProtectionProvider protectionProvider) : IUserAccessInfoAccessor
{
    public UserAccessInfoDto? GetCurrent()
    {
        var context = contextAccessor.HttpContext;
        if (context == null || this.IsImpersonating(context))
        {
            return null;
        }

        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true)
        {
            address = address.MapToIPv4();
        }

        return new UserAccessInfoDto
        {
            IpAddress = address?.ToString(),
            UserAgent = HttpUserAccessInfoAccessor.Limit(context.Request.Headers.UserAgent.ToString(), 2048),
            Language = HttpUserAccessInfoAccessor.Limit(context.Request.Headers.AcceptLanguage.ToString(), 512),
            ClientBrands = HttpUserAccessInfoAccessor.Limit(context.Request.Headers["Sec-CH-UA"].ToString(), 1024),
            Platform = HttpUserAccessInfoAccessor.Limit(context.Request.Headers["Sec-CH-UA-Platform"].ToString(), 256),
            IsMobile = context.Request.Headers["Sec-CH-UA-Mobile"].ToString() switch
            {
                "?1" => true,
                "?0" => false,
                _ => null
            },
            AuthenticationScheme = HttpUserAccessInfoAccessor.GetAuthenticationScheme(context)
        };
    }

    private bool IsImpersonating(HttpContext context)
    {
        if (context.Items.ContainsKey(nameof(HttpUserAccessInfoAccessor))
            || !context.Request.Cookies.TryGetValue("ImpersonatorId", out var cookie))
        {
            return false;
        }

        try
        {
            return !string.IsNullOrEmpty(protectionProvider.CreateProtector("OpenCRM.Auth.Impersonation.v1").Unprotect(cookie));
        }
        catch (CryptographicException)
        {
            // Повреждённая cookie не должна позволять отключить запись посещений.
            return false;
        }
    }

    private static string? GetAuthenticationScheme(HttpContext context)
    {
        var controller = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType();
        if (controller == typeof(AuthApiController))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        if (controller == typeof(AuthController))
        {
            return IdentityConstants.ApplicationScheme;
        }

        return HttpUserAccessInfoAccessor.Limit(context.User.Identity?.AuthenticationType, 64);
    }

    private static string? Limit(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= length ? value : value[..length];
    }
}
