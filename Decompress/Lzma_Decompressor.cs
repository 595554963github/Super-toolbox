using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Lzma_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        private static readonly byte[] LzmaMagicNumber = new byte[] { 0x5D, 0x00, 0x00 };

        static Lzma_Decompressor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.lzma.exe", "lzma.exe");
        }

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
                    var filesToProcess = allFiles.Where(IsLzmaFile).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        OnExtractionFailed("未找到有效的LZMA压缩文件");
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

                        if (DecompressLzmaFile(filePath, outputPath))
                        {
                            OnFileExtracted(outputPath);
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"LZMA解压失败: {ex.Message}");
            }
        }

        private bool IsLzmaFile(string filePath)
        {
            try
            {
                using (var file = File.OpenRead(filePath))
                {
                    if (file.Length < 13) return false; // LZMA头至少13字节

                    byte[] header = new byte[3];
                    file.Read(header, 0, 3);

                    // 检查LZMA魔数
                    return header[0] == LzmaMagicNumber[0] &&
                           header[1] == LzmaMagicNumber[1] &&
                           header[2] == LzmaMagicNumber[2];
                }
            }
            catch { }
            return false;
        }

        private bool DecompressLzmaFile(string inputPath, string outputPath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"d \"{inputPath}\" \"{outputPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                process.WaitForExit();

                return process.ExitCode == 0;
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