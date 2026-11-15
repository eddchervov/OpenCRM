using OpenCRM.Modules.Imports;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports.Tests.Unit;

public sealed class ImportPreparationTests
{
    [Fact]
    public void MappingKeepsUnknownColumnsAndJoinsSelectedNameParts()
    {
        var table = new ImportSourceTable
        {
            Id = "clients", Name = "Клиенты", Columns = ["Код", "Фамилия", "Имя", "Старое поле"],
            Rows = [new ImportSourceRow { Number = 7, Values = ["001", "Иванов", "Иван", "сохранено"] }]
        };

        var rows = ImportMappingBuilder.Build([table],
        [
            new ImportTableMapping
            {
                TableId = table.Id, Kind = "client",
                Fields = new() { ["clientId"] = [0], ["clientName"] = [1, 2] }
            }
        ]);

        var client = Assert.Single(rows);
        Assert.Equal("Иванов Иван", client.Name);
        Assert.Equal("001", client.ExternalId);
        Assert.Equal(7, client.SourceRow);
        Assert.Equal("сохранено", table.Rows[0].Values[3]);
    }

    [Fact]
    public void CombinedRowsPreserveExplicitExternalClientReference()
    {
        var table = new ImportSourceTable
        {
            Id = "all", Columns = ["Код", "Имя", "Госномер"],
            Rows = [new ImportSourceRow { Number = 2, Values = ["c-1", "Анна", "А001АА"] }]
        };

        var rows = ImportMappingBuilder.Build([table],
        [new ImportTableMapping { TableId = "all", Kind = "combined", Fields = new() { ["clientId"] = [0], ["clientName"] = [1], ["plate"] = [2] } }]);

        ImportCandidateValidator.Validate(rows, [], []);

        Assert.Equal(2, rows.Count);
        Assert.Equal("c-1", rows[1].ClientExternalId);
        Assert.Equal(rows[0].Id, rows[1].ResolvedClientRowId);
        Assert.Equal("Анна", rows[1].LinkedClientName);
    }

    [Fact]
    public void MappingRejectsForeignTableAndOutOfRangeColumns()
    {
        var tables = new List<ImportSourceTable> { new() { Id = "one", Columns = ["Имя"] } };
        Assert.Throws<InvalidDataException>(() => ImportMappingBuilder.Build(tables,
            [new() { TableId = "foreign", Kind = "client" }]));
        Assert.Throws<InvalidDataException>(() => ImportMappingBuilder.Build(tables,
            [new() { TableId = "one", Kind = "client", Fields = new() { ["clientName"] = [1] } }]));
    }

    [Fact]
    public void CombinedExportWithSeveralCarsCreatesOneIdenticalClientAndKeepsBothLinks()
    {
        var table = new ImportSourceTable
        {
            Id = "all", Columns = ["Код", "Клиент", "Госномер"],
            Rows =
            [
                new() { Number = 2, Values = ["c1", "Анна", "А001АА"] },
                new() { Number = 3, Values = ["c1", "Анна", "А002АА"] }
            ]
        };

        var rows = ImportMappingBuilder.Build([table],
        [new() { TableId = "all", Kind = "combined", Fields = new() { ["clientId"] = [0], ["clientName"] = [1], ["plate"] = [2] } }]);

        ImportCandidateValidator.Validate(rows, [], []);
        var client = Assert.Single(rows, row => row.Kind == "client");
        Assert.Equal(2, rows.Count(row => row.Kind == "vehicle"));
        Assert.All(rows.Where(row => row.Kind == "vehicle"), row => Assert.Equal(client.Id, row.ResolvedClientRowId));
        Assert.All(rows, row => Assert.Equal("create", row.Status));
    }

    [Fact]
    public void CombinedClientWithoutPhoneOrIdStillOwnsVehicleFromSameSourceRow()
    {
        var table = new ImportSourceTable
        {
            Id = "all", Columns = ["Клиент", "Госномер"],
            Rows = [new() { Number = 2, Values = ["Анна", "А001АА"] }]
        };

        var rows = ImportMappingBuilder.Build([table],
        [new() { TableId = "all", Kind = "combined", Fields = new() { ["clientName"] = [0], ["plate"] = [1] } }]);

        ImportCandidateValidator.Validate(rows, [], []);

        Assert.Equal(rows[0].Id, rows[1].ResolvedClientRowId);
        Assert.All(rows, row => Assert.Equal("create", row.Status));
    }

    [Fact]
    public void MatchingClientNeedsExplicitSelectionAndDoesNotOverwriteItsFields()
    {
        var existing = new ImportClientMatch { Id = Guid.NewGuid(), Name = "Сохранённый клиент", Phone = "+7 999 123-45-67" };
        var row = Client("client-1", "Имя из файла", "79991234567");
        var rows = new List<ImportCandidate> { row };
        ImportCandidateValidator.Validate(rows, [existing], []);
        Assert.Equal("conflict", row.Status);
        Assert.Equal(existing.Id, Assert.Single(row.Matches).Id);

        row.ExistingClientId = existing.Id;
        ImportCandidateValidator.Validate(rows, [existing], []);

        Assert.Equal("link", row.Status);
        Assert.Equal(existing.Id, row.ResolvedClientId);
        Assert.Equal(existing.Name, row.LinkedClientName);
    }

    [Fact]
    public void AmbiguousExternalIdsAndExcludedParentsBlockVehicleLinks()
    {
        var first = Client("first", "Первый", "111");
        var second = Client("second", "Второй", "222");
        first.ExternalId = second.ExternalId = "same";
        var vehicle = new ImportCandidate { Id = "car", Kind = "vehicle", Plate = "А001АА", ClientExternalId = "same" };
        var rows = new List<ImportCandidate> { first, second, vehicle };
        ImportCandidateValidator.Validate(rows, [], []);
        Assert.Equal("conflict", first.Status);
        Assert.Equal("conflict", second.Status);
        Assert.Equal("error", vehicle.Status);

        first.Excluded = second.Excluded = true;
        ImportCandidateValidator.Validate(rows, [], []);

        Assert.Equal("error", vehicle.Status);
        Assert.Null(vehicle.ResolvedClientRowId);
    }

    [Fact]
    public void DuplicateVehicleIsSkippedAndInvalidYearRemainsCorrectable()
    {
        var duplicate = new ImportCandidate { Id = "one", Kind = "vehicle", Plate = "а 001 аа" };
        var invalid = new ImportCandidate { Id = "two", Kind = "vehicle", Vin = "VIN22", Year = "202X" };
        var rows = new List<ImportCandidate> { duplicate, invalid };
        ImportCandidateValidator.Validate(rows, [], [(null, "А001АА")]);
        Assert.Equal("duplicate", duplicate.Status);
        Assert.Equal("error", invalid.Status);

        invalid.Year = "2020";
        ImportCandidateValidator.Validate(rows, [], [(null, "А001АА")]);

        Assert.Equal("create", invalid.Status);
        Assert.Equal("2020", invalid.Year);
    }

    [Fact]
    public void ExistingClientFromAnotherOrganizationCannotBeSelected()
    {
        var row = Client("one", "Клиент", null);
        row.ExistingClientId = Guid.NewGuid();
        ImportCandidateValidator.Validate([row], [], []);

        Assert.Equal("error", row.Status);
        Assert.Null(row.ResolvedClientId);
    }

    [Fact]
    public void UserCanExplicitlyCreateDifferentPersonWithSharedPhone()
    {
        var existing = new ImportClientMatch { Id = Guid.NewGuid(), Name = "Первый человек", Phone = "79990000001" };
        var row = Client("second", "Второй человек", existing.Phone);
        ImportCandidateValidator.Validate([row], [existing], []);
        Assert.Equal("conflict", row.Status);

        row.CreateSeparateClient = true;
        ImportCandidateValidator.Validate([row], [existing], []);

        Assert.Equal("create", row.Status);
        Assert.Null(row.ResolvedClientId);
        Assert.Equal(existing.Id, Assert.Single(row.Matches).Id);
    }

    [Fact]
    public void ExplicitSeparateClientsStillRequireDistinctExternalIdsForVehicleLinks()
    {
        var first = Client("first", "Первый человек", "111");
        var second = Client("second", "Второй человек", "111");
        first.CreateSeparateClient = second.CreateSeparateClient = true;
        first.ExternalId = "first-id";
        second.ExternalId = "second-id";
        var vehicle = new ImportCandidate { Id = "car", Kind = "vehicle", Plate = "А001АА", ClientExternalId = second.ExternalId };
        ImportCandidateValidator.Validate([first, second, vehicle], [], []);
        Assert.Equal("create", first.Status);
        Assert.Equal("create", second.Status);
        Assert.Equal(second.Id, vehicle.ResolvedClientRowId);

        first.ExternalId = second.ExternalId;
        ImportCandidateValidator.Validate([first, second, vehicle], [], []);

        Assert.Equal("conflict", first.Status);
        Assert.Equal("conflict", second.Status);
        Assert.Equal("error", vehicle.Status);
    }

    [Theory]
    [InlineData("VIN11", "В002ВВ")]
    [InlineData("VIN33", "А001АА")]
    public void ConflictingVehicleIdentifiersRequireReview(string vin, string plate)
    {
        var row = new ImportCandidate { Id = "car", Kind = "vehicle", Vin = vin, Plate = plate };
        ImportCandidateValidator.Validate([row], [], [("VIN11", "А001АА"), ("VIN22", "В002ВВ")]);

        Assert.Equal("conflict", row.Status);
    }

    [Theory]
    [InlineData("А", "А001АА", null)]
    [InlineData(null, "<script>", null)]
    [InlineData("VIN11", null, "2099")]
    public void ImportRejectsValuesRejectedByVehicleCard(string? vin, string? plate, string? year)
    {
        var row = new ImportCandidate { Id = "car", Kind = "vehicle", Vin = vin, Plate = plate, Year = year };
        ImportCandidateValidator.Validate([row], [], []);

        Assert.Equal("error", row.Status);
    }

    private static ImportCandidate Client(string id, string name, string? phone) =>
        new() { Id = id, Kind = "client", Name = name, Phone = phone };
}
