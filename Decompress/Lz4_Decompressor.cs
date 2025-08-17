using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using K4os.Compression.LZ4.Streams;

namespace super_toolbox
{
    public class Lz4_Decompressor : BaseExtractor
    {
        private static readonly byte[] LZ4MagicNumber = new byte[] { 0x04, 0x22, 0x4D, 0x18 };

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
                    var filesToProcess = allFiles.Where(IsLz4File).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        OnExtractionFailed("未找到有效的LZ4压缩文件");
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

                        if (DecompressLz4File(filePath, outputPath))
                        {
                            OnFileExtracted(outputPath);
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"LZ4解压失败: {ex.Message}");
            }
        }

        private bool IsLz4File(string filePath)
        {
            try
            {
                using (var file = File.OpenRead(filePath))
                {
                    if (file.Length < 4) return false;

                    byte[] header = new byte[4];
                    file.Read(header, 0, 4);

                    return header.SequenceEqual(LZ4MagicNumber);
                }
            }
            catch { }
            return false;
        }

        private bool DecompressLz4File(string inputPath, string outputPath)
        {
            try
            {
                using (var inputStream = File.OpenRead(inputPath))
                using (var outputStream = File.Create(outputPath))
                using (var decompressionStream = LZ4Stream.Decode(inputStream))
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