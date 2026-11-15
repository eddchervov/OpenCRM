using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Domain.Interfaces.Brands;
using OpenCRM.Domain.Interfaces.Models;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

internal sealed class CatalogTestServices : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    internal CatalogTestServices(OpenCRMContext context)
    {
        this._provider = new ServiceCollection().AddSingleton(context)
            .AddSingleton<IBrandRepository>(new BrandRepository(context))
            .AddSingleton<IModelRepository>(new ModelRepository(context))
            .AddSingleton(Mock.Of<IOrganizationSubscriptionService>()).AddOpenCRMCatalog()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        this._scope = this._provider.CreateScope();
        this.Query = this._scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();
        this.Commands = this._scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
    }

    internal ICatalogQueryService Query { get; }

    internal ICatalogCommandService Commands { get; }

    public void Dispose()
    {
        this._scope.Dispose();
        this._provider.Dispose();
    }
}
