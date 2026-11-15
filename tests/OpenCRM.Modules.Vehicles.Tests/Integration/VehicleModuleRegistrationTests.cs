using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.Modules.Vehicles.Photos;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleModuleRegistrationTests
{
    [Fact]
    public void RegistrationMapsEveryPublicContractToScopedImplementationOwnedByVehicleModule()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddOpenCRMVehicles());

        var expectedImplementations = new Dictionary<Type, Type>
        {
            [typeof(IVehicleQueryService)] = typeof(VehicleQueryService),
            [typeof(IVehicleMaintenanceLookupService)] = typeof(VehicleMaintenanceLookupService),
            [typeof(IQuickBookingVehicleLookupService)] = typeof(QuickBookingVehicleLookupService),
            [typeof(IVehicleRegistrationPlatePolicy)] = typeof(VehicleRegistrationPlatePolicy),
            [typeof(IVehicleOptionQueryService)] = typeof(VehicleOptionQueryService),
            [typeof(IVehicleClientSummaryQueryService)] = typeof(VehicleClientSummaryQueryService),
            [typeof(IVehicleCommandService)] = typeof(VehicleCommandService),
            [typeof(IVehicleGalleryService)] = typeof(VehicleGalleryService),
            [typeof(IVehiclePhotoAlbumService)] = typeof(VehiclePhotoAlbumService),
            [typeof(IVehicleCounterQueryService)] = typeof(Counters.VehicleCounterQueryService),
            [typeof(IVehicleCounterCommandService)] = typeof(Counters.VehicleCounterCommandService),
            [typeof(IVehicleLimitQueryService)] = typeof(VehicleLimitQueryService),
            [typeof(IVehicleDocumentHistoryService)] = typeof(VehicleHistoryWriter)
        };

        foreach (var expected in expectedImplementations)
        {
            var descriptor = Assert.Single(services, service => service.ServiceType == expected.Key);

            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            if (descriptor.ImplementationType != null)
            {
                Assert.Equal(expected.Value, descriptor.ImplementationType);
                continue;
            }

            Assert.NotNull(descriptor.ImplementationFactory);
        }
    }

    [Fact]
    public void RegistrationResolvesPlatePolicyFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VehicleRegistrationPlates:MaxLength"] = "16",
                ["VehicleRegistrationPlates:RegexTimeoutMilliseconds"] = "100",
                ["VehicleRegistrationPlates:MaxSearchVariants"] = "64",
                ["VehicleRegistrationPlates:LetterPattern"] = "[A-ZА-ЯЁ]",
                ["VehicleRegistrationPlates:CharacterMappings:A"] = "А",
                ["VehicleRegistrationPlates:Formats:0:Code"] = "car",
                ["VehicleRegistrationPlates:Formats:0:Kinds:0"] = "1",
                ["VehicleRegistrationPlates:Formats:0:Masks:0"] = "L###LL###",
                ["VehicleRegistrationPlates:Formats:0:Pattern"] = "^[A-ZА-ЯЁ][0-9]{3}[A-ZА-ЯЁ]{2}[0-9]{3}$",
                ["VehicleRegistrationPlates:Formats:0:Example"] = "А123ВС154"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOpenCRMVehicles();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<IVehicleRegistrationPlatePolicy>();

        Assert.True(policy.MatchesFormat("А123ВС154", OpenCRM.Domain.Enums.VehicleKind.PassengerCar));
    }

    [Fact]
    public void VehicleModuleDoesNotReferenceApplicationOrOtherFeatureImplementations()
    {
        var moduleAssembly = typeof(VehiclesServiceCollectionExtensions).Assembly;
        var referencedAssemblyNames = moduleAssembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("OpenCRM.Infrastructure.Common", referencedAssemblyNames);
        Assert.DoesNotContain("OpenCRM.WebApp", referencedAssemblyNames);
        Assert.DoesNotContain("OpenCRM.Modules.Inventory", referencedAssemblyNames);
        Assert.DoesNotContain("OpenCRM.Modules.Vehicles", typeof(IVehicleQueryService).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name));

        var moduleImplementations = moduleAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.Namespace?.StartsWith("OpenCRM.Modules.Vehicles", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(moduleImplementations);
        Assert.All(moduleImplementations, implementation =>
            Assert.Equal(moduleAssembly, implementation.Assembly));
    }
}
