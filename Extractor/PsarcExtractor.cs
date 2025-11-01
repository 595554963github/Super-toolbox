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
            var pakFiles = Directory.GetFiles(directoryPath, "*.pak");
            var archiveFiles = new string[psarcFiles.Length + pakFiles.Length];
            psarcFiles.CopyTo(archiveFiles, 0);
            pakFiles.CopyTo(archiveFiles, psarcFiles.Length);

            if (archiveFiles.Length == 0)
            {
                OnExtractionFailed("未找到.psarc或.pak文件");
                return;
            }

            TotalFilesToExtract = archiveFiles.Length;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var archiveFilePath in archiveFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string archiveDir = Path.GetDirectoryName(archiveFilePath) ?? Directory.GetCurrentDirectory();
                        string archiveFileName = Path.GetFileNameWithoutExtension(archiveFilePath);

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-x \"{archiveFileName}\"",
                                WorkingDirectory = archiveDir,
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
                            OnExtractionFailed($"解包失败: {archiveFilePath} - 错误: {error}");
                            continue;
                        }

                        string expectedOutputDir = Path.Combine(archiveDir, archiveFileName);
                        if (Directory.Exists(expectedOutputDir))
                        {
                            OnFileExtracted(archiveFilePath);
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
