using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class PsarcExtractor : BaseExtractor
    {
        private static string _tempExePath;
        private static string _tempDllPath;

        static PsarcExtractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);

            _tempExePath = Path.Combine(tempDir, "psarc-tool.exe");
            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.psarc-tool.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的psarc-tool.exe资源未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempExePath, buffer);
                }
            }

            _tempDllPath = Path.Combine(tempDir, "zlib1.dll");
            if (!File.Exists(_tempDllPath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.zlib1.dll"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的zlib1.dll资源未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempDllPath, buffer);
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

            var psarcFiles = Directory.GetFiles(directoryPath, "*.psarc");
            if (psarcFiles.Length == 0)
            {
                OnExtractionFailed("未找到.psarc文件");
                return;
            }

            TotalFilesToExtract = psarcFiles.Length;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var psarcFilePath in psarcFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string psarcDir = Path.GetDirectoryName(psarcFilePath) ?? Directory.GetCurrentDirectory();
                        string tmpDir = Path.Combine(psarcDir, "tmp");

                        if (!Directory.Exists(tmpDir))
                        {
                            Directory.CreateDirectory(tmpDir);
                        }

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-x \"{psarcFilePath}\"",
                                WorkingDirectory = psarcDir,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            }
                        };

                        process.Start();

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();

                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            OnExtractionFailed($"解包失败: {psarcFilePath} - 错误: {error}");
                            continue;
                        }

                        try
                        {
                            if (Directory.Exists(tmpDir))
                            {
                                Directory.Delete(tmpDir, true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"清理临时文件夹失败: {ex.Message}");
                        }

                        OnFileExtracted(psarcFilePath);
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