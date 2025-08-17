using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Lzma_Compressor : BaseExtractor
    {
        private static string _tempExePath;

        static Lzma_Compressor()
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
                    foreach (var file in Directory.GetFiles(compressedDir, "*.lzma"))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToExtract = filesToCompress.Length;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileName(filePath);
                        string outputPath = Path.Combine(compressedDir, fileName + ".lzma");

                        CompressFileWithLzma(filePath, outputPath);

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
                OnExtractionFailed($"LZMA压缩失败: {ex.Message}");
            }
        }

        private void CompressFileWithLzma(string inputPath, string outputPath)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"e \"{inputPath}\" \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                throw new Exception($"LZMA压缩失败(退出代码: {process.ExitCode}): {error}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}