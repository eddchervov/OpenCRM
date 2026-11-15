namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Разрешает доступ к действию до подтверждения email аккаунта. </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowUnconfirmedEmailAttribute : Attribute;
