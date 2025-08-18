using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Wiiu_h3appExtractor : BaseExtractor
    {
        private static string _tempExePath;

        static Wiiu_h3appExtractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "cdecrypt.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.cdecrypt.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的EXE资源未找到");

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

            bool hasApp = Directory.GetFiles(directoryPath, "*.app").Length > 0;
            bool hasH3 = Directory.GetFiles(directoryPath, "*.h3").Length > 0;
            bool hasTik = Directory.GetFiles(directoryPath, "*.tik").Length > 0;
            bool hasTmd = Directory.GetFiles(directoryPath, "*.tmd").Length > 0;

            if (!hasApp || !hasH3 || !hasTik || !hasTmd)
            {
                OnExtractionFailed("目录不包含有效的Wii U游戏文件（缺少.app/.h3/.tik/.tmd文件）");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"\"{directoryPath}\"",
                            WorkingDirectory = directoryPath,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    process.OutputDataReceived += (sender, e) => {
                        if (!string.IsNullOrEmpty(e.Data))
                            Debug.WriteLine(e.Data);
                    };
                    process.ErrorDataReceived += (sender, e) => {
                        if (!string.IsNullOrEmpty(e.Data))
                            Debug.WriteLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        OnExtractionFailed($"cdecrypt.exe 处理失败，退出代码: {process.ExitCode}");
                        return;
                    }

                    var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

                    var extractedFiles = Array.FindAll(allFiles, file =>
                        !file.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
                        !file.EndsWith(".h3", StringComparison.OrdinalIgnoreCase) &&
                        !file.EndsWith(".tik", StringComparison.OrdinalIgnoreCase) &&
                        !file.EndsWith(".tmd", StringComparison.OrdinalIgnoreCase));

                    TotalFilesToExtract = extractedFiles.Length;

                    foreach (var file in extractedFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        OnFileExtracted(file);
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"处理失败: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}