using OpenCRM.Modules.Documents.Pdf;
using OpenCRM.Modules.Documents.Word;
using OpenCRM.Modules.Documents;

namespace OpenCRM.Modules.Documents.Tests.Unit;

public sealed class DocumentModuleBoundaryTests
{
    [Theory]
    [InlineData(typeof(PdfDocumentExportService))]
    [InlineData(typeof(WordDocumentExportService))]
    public void ExportFacadeDependsOnlyOnModuleWorkflows(Type facadeType)
    {
        var constructors = facadeType.GetConstructors();

        Assert.Single(constructors);
        Assert.All(constructors[0].GetParameters(), parameter =>
        {
            Assert.Equal(typeof(DocumentServiceCollectionExtensions).Assembly, parameter.ParameterType.Assembly);
            Assert.EndsWith("Workflow", parameter.ParameterType.Name);
        });
    }
}
