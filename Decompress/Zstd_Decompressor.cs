using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZstdNet;

namespace super_toolbox
{
    public class Zstd_Decompressor : BaseExtractor
    {
        private static readonly byte[] ZstdMagic = { 0x28, 0xB5, 0x2F, 0xFD };

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
                    var filesToProcess = allFiles.Where(IsZstdFile).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        OnExtractionFailed("未找到有效的Zstd压缩文件");
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

                        if (DecompressZstdFile(filePath, outputPath))
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

        private bool IsZstdFile(string filePath)
        {
            try
            {
                using (var file = File.OpenRead(filePath))
                {
                    if (file.Length < 4) return false;

                    byte[] header = new byte[4];
                    file.Read(header, 0, 4);

                    return header.SequenceEqual(ZstdMagic);
                }
            }
            catch { }
            return false;
        }

        private bool DecompressZstdFile(string inputPath, string outputPath)
        {
            try
            {
                using (var decompressor = new Decompressor())
                using (var inputStream = File.OpenRead(inputPath))
                using (var outputStream = File.Create(outputPath))
                {
                    byte[] compressedData = new byte[inputStream.Length];
                    inputStream.Read(compressedData, 0, compressedData.Length);

                    byte[] decompressedData = decompressor.Unwrap(compressedData);
                    outputStream.Write(decompressedData, 0, decompressedData.Length);
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