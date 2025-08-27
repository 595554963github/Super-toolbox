using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class PsarcRepacker : BaseExtractor
    {
        private static string _tempExePath;

        static PsarcRepacker()
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
            await RepackAsync(directoryPath, cancellationToken);
        }

        public async Task RepackAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            var directories = Directory.GetDirectories(directoryPath);
            if (directories.Length == 0)
            {
                OnExtractionFailed("未找到可打包的文件夹");
                return;
            }

            TotalFilesToExtract = directories.Length;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var folderPath in directories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string folderName = Path.GetFileName(folderPath);
                        string parentDir = Path.GetDirectoryName(folderPath) ?? Directory.GetCurrentDirectory();

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-c \"{folderName}\"",
                                WorkingDirectory = parentDir,
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
                            OnExtractionFailed($"打包失败: {folderName} - 错误: {error}");
                            continue;
                        }

                        string expectedPsarcFile = Path.Combine(parentDir, folderName + ".psarc");
                        if (File.Exists(expectedPsarcFile))
                        {
                            OnFileExtracted(folderPath);
                        }
                        else
                        {
                            OnExtractionFailed($"打包失败: 未找到输出文件 {expectedPsarcFile}");
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

        public void Repack(string directoryPath)
        {
            RepackAsync(directoryPath).Wait();
        }

        public override void Extract(string directoryPath)
        {
            Repack(directoryPath);
        }
    }
}