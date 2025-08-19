using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class IdeaFactory_PacRepacker : BaseExtractor
    {
        private static string _tempExePath;
        private static string _tempDllPath;

        static IdeaFactory_PacRepacker()
        {
            _tempExePath = LoadEmbeddedExe("embedded.pac_repack.exe", "pac_repack.exe");
            _tempDllPath = Path.Combine(TempDllDirectory, "libpac.dll");

            if (!File.Exists(_tempDllPath))
            {
                LoadEmbeddedDll("embedded.libpac.dll", "libpac.dll");
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            var filesToPack = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            if (filesToPack.Length == 0)
            {
                OnExtractionFailed("未找到需要打包的文件");
                return;
            }

            TotalFilesToExtract = filesToPack.Length; 

            try
            {
                await Task.Run(() =>
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    try
                    {
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

                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                OnFileExtracted(e.Data);
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            string error = process.StandardError.ReadToEnd();
                            throw new Exception($"打包失败: {error}");
                        }

                        OnExtractionCompleted();
                    }
                    catch (Exception ex)
                    {
                        OnExtractionFailed($"打包过程中发生错误: {ex.Message}");
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("打包操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"打包失败: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}