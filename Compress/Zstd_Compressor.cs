using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZstdNet;

namespace super_toolbox
{
    public class Zstd_Compressor : BaseExtractor
    {
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            var filesToCompress = Directory.GetFiles(directoryPath, "*.*");
            if (filesToCompress.Length == 0)
            {
                OnExtractionFailed("未找到需要压缩的文件");
                return;
            }

            string compressedDir = Path.Combine(directoryPath, "Compressed");
            Directory.CreateDirectory(compressedDir);

            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in Directory.GetFiles(compressedDir, "*.zst"))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToExtract = filesToCompress.Length;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileName(filePath);
                        string outputPath = Path.Combine(compressedDir, fileName + ".zst");

                        CompressFileWithZstd(filePath, outputPath);

                        if (File.Exists(outputPath))
                        {
                            OnFileExtracted(outputPath);
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"压缩失败: {ex.Message}");
            }
        }

        private void CompressFileWithZstd(string inputPath, string outputPath)
        {
            using (var compressor = new Compressor())
            using (var inputStream = File.OpenRead(inputPath))
            using (var outputStream = File.Create(outputPath))
            {
                byte[] inputData = new byte[inputStream.Length];
                inputStream.Read(inputData, 0, inputData.Length);

                byte[] compressedData = compressor.Wrap(inputData);
                outputStream.Write(compressedData, 0, compressedData.Length);
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}