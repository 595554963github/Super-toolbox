using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Gzip_Decompressor : BaseExtractor
    {
        private static readonly byte[] GzipHeader = new byte[] { 0x1F, 0x8B };

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
                    var filesToProcess = allFiles.Where(IsGzipFile).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        OnExtractionFailed("未找到有效的GZIP压缩文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);

                    TotalFilesToExtract = filesToProcess.Length;

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        if (fileName.EndsWith(".tar"))
                        {
                            fileName = Path.GetFileNameWithoutExtension(fileName);
                        }
                        string outputPath = Path.Combine(decompressedDir, fileName);

                        if (DecompressGzipFile(filePath, outputPath))
                        {
                            OnFileExtracted(outputPath);
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"GZIP解压失败: {ex.Message}");
            }
        }

        private bool IsGzipFile(string filePath)
        {
            try
            {
                using (var file = File.OpenRead(filePath))
                {
                    if (file.Length < 2) return false;

                    byte[] header = new byte[2];
                    file.Read(header, 0, 2);

                    return header[0] == GzipHeader[0] && header[1] == GzipHeader[1];
                }
            }
            catch
            {
                return false;
            }
        }

        private bool DecompressGzipFile(string inputPath, string outputPath)
        {
            try
            {
                using (var inputStream = File.OpenRead(inputPath))
                using (var outputStream = File.Create(outputPath))
                using (var decompressionStream = new GZipStream(inputStream, CompressionMode.Decompress))
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