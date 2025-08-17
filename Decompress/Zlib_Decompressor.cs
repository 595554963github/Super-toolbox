using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Zlib_Decompressor : BaseExtractor
    {
        private static readonly byte[][] SupportedHeaders =
        {
            new byte[] { 0x78, 0x01 }, // 无压缩
            new byte[] { 0x78, 0x5E }, // 快速压缩
            new byte[] { 0x78, 0x9C }, // 默认压缩
            new byte[] { 0x78, 0xDA }  // 最大压缩
        };

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsZlibFile).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        OnExtractionFailed("未找到有效的Zlib压缩文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);

                    TotalFilesToExtract = filesToProcess.Length;

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputPath = Path.Combine(decompressedDir, fileName);

                        if (DecompressZlibFile(filePath, outputPath))
                        {
                            OnFileExtracted(outputPath);
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"解压失败: {ex.Message}");
            }
        }

        private bool IsZlibFile(string filePath)
        {
            try
            {
                using (var file = File.OpenRead(filePath))
                {
                    if (file.Length < 2) return false;

                    byte[] header = new byte[2];
                    file.Read(header, 0, 2);

                    foreach (var supportedHeader in SupportedHeaders)
                    {
                        if (header[0] == supportedHeader[0] && header[1] == supportedHeader[1])
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool DecompressZlibFile(string inputPath, string outputPath)
        {
            try
            {
                using (var inputStream = File.OpenRead(inputPath))
                using (var outputStream = File.Create(outputPath))
                using (var decompressionStream = new ZLibStream(inputStream, CompressionMode.Decompress))
                {
                    decompressionStream.CopyTo(outputStream);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}