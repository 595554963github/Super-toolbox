using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class MagesMpkExtractor : BaseExtractor
    {
        private static string _tempExePath;

        static MagesMpkExtractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "mpk.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.mpk.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的MPK工具资源未找到");

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

            var mpkFiles = Directory.GetFiles(directoryPath, "*.mpk");
            if (mpkFiles.Length == 0)
            {
                OnExtractionFailed("未找到.mpk文件");
                return;
            }

            TotalFilesToExtract = mpkFiles.Length;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var mpkFilePath in mpkFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string mpkFileDirectory = Path.GetDirectoryName(mpkFilePath) ?? string.Empty;

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-e \"{mpkFilePath}\"",
                                WorkingDirectory = mpkFileDirectory,
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
                            OnExtractionFailed($"MPK解包失败: {Path.GetFileName(mpkFilePath)} - 错误: {error}");
                            continue;
                        }

                        string extractedFolder = Path.Combine(mpkFileDirectory,
                            Path.GetFileNameWithoutExtension(mpkFilePath));

                        if (Directory.Exists(extractedFolder))
                        {
                            var extractedFiles = Directory.GetFiles(extractedFolder, "*", SearchOption.AllDirectories);
                            foreach (var file in extractedFiles)
                            {
                                OnFileExtracted(file);
                            }
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
                OnExtractionFailed($"MPK解包失败: {ex.Message}");
            }
        }


        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}