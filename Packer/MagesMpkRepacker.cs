using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class MagesMpkRepacker : BaseExtractor
    {
        private static string _tempExePath;

        static MagesMpkRepacker()
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

                        string? folderParentDir = Path.GetDirectoryName(folderPath);
                        if (string.IsNullOrEmpty(folderParentDir))
                        {
                            OnExtractionFailed($"无法获取文件夹的父目录: {folderPath}");
                            continue;
                        }

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-c \"{folderPath}\"",
                                WorkingDirectory = folderParentDir,
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
                            OnExtractionFailed($"MPK打包失败: {Path.GetFileName(folderPath)} - 错误: {error}");
                            continue;
                        }

                        string expectedMpkFile = Path.Combine(folderParentDir,
                            Path.GetFileName(folderPath) + ".mpk");

                        if (File.Exists(expectedMpkFile))
                        {
                            OnFileExtracted(expectedMpkFile);
                        }
                        else
                        {
                            OnExtractionFailed($"MPK打包失败: 未找到输出文件 {expectedMpkFile}");
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
                OnExtractionFailed($"MPK处理失败: {ex.Message}");
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