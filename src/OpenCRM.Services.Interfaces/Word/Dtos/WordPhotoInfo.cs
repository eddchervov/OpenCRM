using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Word.Dtos
{
    /// <summary> DTO фотографии для Word-документов. </summary>
    public class WordPhotoInfo
    {
        /// <summary> Двоичные данные фотографии для вставки в документ. </summary>
        public byte[]? ImageData { get; set; }
    }
}

