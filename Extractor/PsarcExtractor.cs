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

        static PsarcExtractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);

            _tempExePath = Path.Combine(tempDir, "Unpsarc.exe");
            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.Unpsarc.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的Unpsarc.exe资源未找到");

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
                        string psarcFileName = Path.GetFileNameWithoutExtension(psarcFilePath);

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-x \"{psarcFileName}\"",
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

                        string expectedOutputDir = Path.Combine(psarcDir, psarcFileName);
                        if (Directory.Exists(expectedOutputDir))
                        {
                            OnFileExtracted(psarcFilePath);
                        }
                        else
                        {
                            OnExtractionFailed($"解包失败: 未找到输出目录 {expectedOutputDir}");
                        }
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
