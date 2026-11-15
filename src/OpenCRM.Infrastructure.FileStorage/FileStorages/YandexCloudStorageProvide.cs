//using OpenCRM.Services.Interfaces.FileStorages.Dtos;
//using OpenCRM.Services.Interfaces.FileStorages;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using FileContent = OpenCRM.Services.Interfaces.FileStorages.Dtos.FileContent;
//using FileInfo = OpenCRM.Services.Interfaces.FileStorages.Dtos.FileInfo;

//namespace OpenCRM.Infrastructure.FileStorage.FileStorages
//{
//    public class YandexCloudStorageProvider : IFileStorageProvider
//    {
//        private readonly string _bucketName;
//        private readonly IAmazonS3 _s3Client;

//        public YandexCloudStorageProvider(string bucketName, string accessKey, string secretKey)
//        {
//            _bucketName = bucketName;
//            var config = new AmazonS3Config
//            {
//                ServiceURL = "https://storage.yandexcloud.net"
//            };
//            _s3Client = new AmazonS3Client(accessKey, secretKey, config);
//        }

//        public async Task<FileContent> GetFileAsync(string container, string fileName)
//        {
//            var key = $"{container}/{fileName}";
//            var response = await _s3Client.GetObjectAsync(_bucketName, key);

//            return new FileContent
//            {
//                Content = response.ResponseStream,
//                ContentType = response.Headers.ContentType,
//                FileName = fileName
//            };
//        }

//        public async Task<FileInfo> SaveFileAsync(string container, string fileName, Stream content, string contentType)
//        {
//            var key = $"{container}/{fileName}";
//            var request = new PutObjectRequest
//            {
//                BucketName = _bucketName,
//                Key = key,
//                InputStream = content,
//                ContentType = contentType
//            };

//            await _s3Client.PutObjectAsync(request);

//            return new FileInfo
//            {
//                FileName = fileName,
//                ContentType = contentType,
//                Size = content.Length,
//                CreatedAt = DateTime.UtcNow,
//                ModifiedAt = DateTime.UtcNow
//            };
//        }

//        // Реализация остальных методов...
//    }
//}

