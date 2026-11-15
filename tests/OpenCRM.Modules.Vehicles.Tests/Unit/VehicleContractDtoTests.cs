using System.ComponentModel.DataAnnotations;
using OpenCRM.Domain.Pagination;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Tests.Unit;

public sealed class VehicleContractDtoTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    public void PagedResultCalculatesPageCount(int totalCount, int pageSize, int expectedPages)
    {
        var result = new PagedResult<int>
        {
            Items = [],
            TotalCount = totalCount,
            Page = 1,
            PageSize = pageSize,
            AllCount = totalCount
        };

        Assert.Equal(expectedPages, result.TotalPages);
    }

    [Theory]
    [InlineData("12345678901234567890123456789012", true)]
    [InlineData("123456789012345678901234567890123", false)]
    public void CreateVehicleDtoValidatesVinLength(string vin, bool isValid)
    {
        var dto = new CreateVehicleDto { Vin = vin };
        var validationResults = new List<ValidationResult>();

        var result = Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true);

        Assert.Equal(isValid, result);
        Assert.Equal(isValid ? 0 : 1, validationResults.Count);
    }

    [Theory]
    [InlineData("А123АА154", true)]
    [InlineData("1234567890123456789012345", false)]
    public void UpdateVehicleDtoValidatesRegistrationPlateLength(string registrationPlate, bool isValid)
    {
        var dto = new UpdateVehicleDto { RegistrationPlate = registrationPlate };
        var validationResults = new List<ValidationResult>();

        var result = Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true);

        Assert.Equal(isValid, result);
        Assert.Equal(isValid ? 0 : 1, validationResults.Count);
    }
}
