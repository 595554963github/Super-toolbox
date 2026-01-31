using System.Diagnostics;

namespace super_toolbox
{
    public class DtsExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static string _tempExePath;

        static DtsExtractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.SRPG_Unpacker.exe", "SRPG_Unpacker.exe");
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var dtsFiles = Directory.GetFiles(directoryPath, "*.dts", SearchOption.AllDirectories);

            if (dtsFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, $"在目录{directoryPath}中未找到.dts文件");
                OnExtractionFailed($"在目录{directoryPath}中未找到.dts文件");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            TotalFilesToExtract = dtsFiles.Length;

            int processedCount = 0;
            int successCount = 0;
            int totalExtractedFiles = 0;

            foreach (var dtsFile in dtsFiles)
            {
                try
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    processedCount++;
                    ExtractionProgress?.Invoke(this, $"正在处理文件({processedCount}/{dtsFiles.Length}): {Path.GetFileName(dtsFile)}");

                    int extractedCount = await UnpackDtsFile(dtsFile, cancellationToken);
                    if (extractedCount > 0)
                    {
                        successCount++;
                        totalExtractedFiles += extractedCount;
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(dtsFile)}时出错:{ex.Message}");
                    OnExtractionFailed($"处理文件{Path.GetFileName(dtsFile)}时出错:{ex.Message}");
                }
            }

            ExtractionProgress?.Invoke(this, $"处理完成,成功解包{successCount}/{dtsFiles.Length}个.dts文件,共提取出{totalExtractedFiles}个文件");
            OnExtractionCompleted();
        }

        private async Task<int> UnpackDtsFile(string dtsFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ExtractionProgress?.Invoke(this, $"正在解包:{Path.GetFileName(dtsFilePath)}");

                string fileDirectory = Path.GetDirectoryName(dtsFilePath) ?? string.Empty;

                var filesBefore = Directory.GetFiles(fileDirectory, "*", SearchOption.AllDirectories).ToHashSet();

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"\"{dtsFilePath}\"",
                    WorkingDirectory = fileDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        ExtractionError?.Invoke(this, $"无法启动解包进程:{Path.GetFileName(dtsFilePath)}");
                        return 0;
                    }
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            ExtractionProgress?.Invoke(this, e.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            string errorMessage = e.Data;
                            bool shouldIgnore = errorMessage.Contains("IMPLEMENT ME") ||
                                               errorMessage.Contains("EDITDATA::init") ||
                                               errorMessage.Contains("struct UNITFUSION");

                            if (!shouldIgnore)
                            {
                                ExtractionError?.Invoke(this, $"错误:{errorMessage}");
                            }
                        }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(cancellationToken);

                    var filesAfter = Directory.GetFiles(fileDirectory, "*", SearchOption.AllDirectories).ToHashSet();
                    var newFiles = filesAfter.Except(filesBefore).ToList();

                    if (newFiles.Count == 0)
                    {
                        if (process.ExitCode != 0)
                        {
                            ExtractionError?.Invoke(this, $"{Path.GetFileName(dtsFilePath)}解包失败,错误代码:{process.ExitCode}");
                        }
                        else
                        {
                            ExtractionError?.Invoke(this, $"{Path.GetFileName(dtsFilePath)}处理完成但未生成新文件");
                        }
                        return 0;
                    }

                    foreach (var newFile in newFiles)
                    {
                        string relativePath = Path.GetRelativePath(fileDirectory, newFile);
                        OnFileExtracted(newFile);
                        ExtractionProgress?.Invoke(this, $"已提取:{relativePath}");
                    }

                    ExtractionProgress?.Invoke(this, $"{Path.GetFileName(dtsFilePath)} 解包成功,提取出{newFiles.Count}个文件");
                    return newFiles.Count;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解包文件{Path.GetFileName(dtsFilePath)}时出错:{ex.Message}");
                return 0;
            }
        }
    }
}
