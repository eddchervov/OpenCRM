namespace OpenCRM.Services.Interfaces.Media.Dto;

/// <summary> Параметры обработки изображения. </summary>
public class ImageProcessingOptions
{
    /// <summary> Максимальная ширина изображения. </summary>
    public int MaxWidth { get; set; } 

    /// <summary> Максимальная высота изображения. </summary>
    public int MaxHeight { get; set; }

    /// <summary> Качество сжатия изображения. </summary>
    public int Quality { get; set; } 

    /// <summary> Целевая папка для сохранения. </summary>
    public string TargetFolder { get; set; } = string.Empty;

    /// <summary> Признак необходимости вычисления хеша изображения. </summary>
    public bool GenerateHash { get; set; } = true;
}
