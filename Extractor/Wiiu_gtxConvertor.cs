using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Wiiu_gtxConvertor : BaseExtractor
    {
        private static string _tempExePath;
        private static string _tempGfdDllPath;
        private static string _tempTexUtilsDllPath;

        static Wiiu_gtxConvertor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.TexConv2.exe", "TexConv2.exe");
            _tempGfdDllPath = Path.Combine(TempDllDirectory, "gfd.dll");
            _tempTexUtilsDllPath = Path.Combine(TempDllDirectory, "texUtils.dll");

            if (!File.Exists(_tempGfdDllPath))
            {
                LoadEmbeddedDll("embedded.gfd.dll", "gfd.dll");
            }

            if (!File.Exists(_tempTexUtilsDllPath))
            {
                LoadEmbeddedDll("embedded.texUtils.dll", "texUtils.dll");
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            var gtxFiles = Directory.GetFiles(directoryPath, "*.gtx", SearchOption.AllDirectories);
            if (gtxFiles.Length == 0)
            {
                OnExtractionFailed("未找到.gtx文件");
                return;
            }

            TotalFilesToExtract = gtxFiles.Length;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var gtxFilePath in gtxFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        try
                        {
                            var directoryName = Path.GetDirectoryName(gtxFilePath);
                            if (string.IsNullOrEmpty(directoryName))
                            {
                                OnExtractionFailed($"无法获取文件目录: {gtxFilePath}");
                                continue;
                            }

                            string outputDirectory = Path.Combine(directoryName, "Extracted");
                            Directory.CreateDirectory(outputDirectory);

                            string outputPath = Path.Combine(outputDirectory,
                                Path.GetFileNameWithoutExtension(gtxFilePath) + ".dds");

                            var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = _tempExePath,
                                    Arguments = $"-i \"{gtxFilePath}\" -o \"{outputPath}\"",
                                    WorkingDirectory = directoryName,
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
                                throw new Exception($"转换文件 {Path.GetFileName(gtxFilePath)} 失败: {error}");
                            }

                            OnFileExtracted($"成功转换: {Path.GetFileName(gtxFilePath)} -> {Path.GetFileName(outputPath)}");
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"文件 {Path.GetFileName(gtxFilePath)} 转换错误: {ex.Message}");
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("转换操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"转换失败: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}