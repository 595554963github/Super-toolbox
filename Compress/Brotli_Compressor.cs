using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Brotli_Compressor : BaseExtractor
    {
        private static string _tempExePath;

        static Brotli_Compressor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "brotli.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.brotli.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的Brotli压缩工具资源未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempExePath, buffer);
                }
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            var filesToCompress = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
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
                    foreach (var file in Directory.GetFiles(compressedDir, "*.br", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToExtract = filesToCompress.Length;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string relativePath = GetRelativePath(directoryPath, filePath);
                        string outputPath = Path.Combine(compressedDir, relativePath + ".br");

                        string outputDir = Path.GetDirectoryName(outputPath) ??
                            throw new InvalidOperationException($"无法确定输出目录路径: {outputPath}");

                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-c \"{filePath}\" \"{outputPath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            }
                        };

                        Console.WriteLine($"执行命令: {process.StartInfo.FileName} {process.StartInfo.Arguments}");

                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        if (process.ExitCode == 0)
                        {
                            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                            {
                                OnFileExtracted(outputPath);
                            }
                            else
                            {
                                OnExtractionFailed($"压缩成功但输出文件异常: {outputPath}");
                            }
                        }
                        else
                        {
                            OnExtractionFailed($"压缩失败({filePath}): {error}");
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"压缩过程出错: {ex.Message}");
            }
        }

        private string GetRelativePath(string rootPath, string fullPath)
        {
            Uri rootUri = new Uri(rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar);

            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}