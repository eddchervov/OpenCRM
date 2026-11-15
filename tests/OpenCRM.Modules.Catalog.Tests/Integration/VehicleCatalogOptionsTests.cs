using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Catalog.Brands;
using OpenCRM.Modules.Catalog.Models;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class VehicleCatalogOptionsTests(CatalogDatabaseFixture fixture)
{
    [Fact]
    public async Task OptionsKeepVehicleKindBrandAndDeletionFilters()
    {
        await using var context = fixture.CreateContext();
        var carBrand = new Brand { Name = "Марка легкового авто", Kind = VehicleKind.PassengerCar };
        var truckBrand = new Brand { Name = "Марка грузовика", Kind = VehicleKind.Truck };
        var deletedBrand = new Brand { Name = "Удалённая марка", IsDeleted = true };
        var carModel = new Model { Brand = carBrand, Name = "Легковая модель", Kind = VehicleKind.PassengerCar };
        var truckModel = new Model { Brand = truckBrand, Name = "Грузовая модель", Kind = VehicleKind.Truck };
        var deletedModel = new Model { Brand = carBrand, Name = "Удалённая модель", IsDeleted = true };
        context.Brands.AddRange(carBrand, truckBrand, deletedBrand);
        context.Models.AddRange(carModel, truckModel, deletedModel);
        await context.SaveChangesAsync();
        var brands = new BrandService(new BrandRepository(context));
        var models = new ModelService(new ModelRepository(context));

        var allBrands = await brands.GetOptions();
        var carBrands = await brands.GetOptions(VehicleKind.PassengerCar);
        var carModels = await models.GetOptions(carBrand.Id, VehicleKind.PassengerCar);

        Assert.Contains(allBrands, item => item.Id == truckBrand.Id);
        Assert.DoesNotContain(allBrands, item => item.Id == deletedBrand.Id);
        Assert.Contains(carBrands, item => item.Id == carBrand.Id);
        Assert.DoesNotContain(carBrands, item => item.Id == truckBrand.Id);
        Assert.Equal(carModel.Id, Assert.Single(carModels).Id);
        Assert.Empty(await models.GetOptions(carBrand.Id, VehicleKind.Truck));
        Assert.Equal(carModel.Id, Assert.Single(await models.GetOptions(carBrand.Id)).Id);
    }
}
