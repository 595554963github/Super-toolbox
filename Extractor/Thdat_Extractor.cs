using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Thdat_Extractor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static Thdat_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.thdat.exe", "thdat.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误:选择的目录不存在");
                OnExtractionFailed("错误:选择的目录不存在");
                return;
            }

            var datFiles = Directory.EnumerateFiles(directoryPath, "*.dat", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).ToLower().EndsWith("bgm.dat"))
                .ToList();

            if (datFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.dat文件");
                OnExtractionFailed("未找到任何.dat文件");
                return;
            }

            TotalFilesToExtract = datFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            uint detectedVersion = DetectVersion(directoryPath, datFiles);
            if (detectedVersion == 0)
            {
                ExtractionError?.Invoke(this, "无法识别版本号");
                OnExtractionFailed("无法识别版本号");
                return;
            }

            ExtractionProgress?.Invoke(this, $"找到{datFiles.Count}个.dat文件,检测到版本:{detectedVersion},开始解包...");

            int extractedCount = 0;
            int totalExtractedFiles = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var datFilePath in datFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string fileDirectory = Path.GetDirectoryName(datFilePath) ?? string.Empty;
                        string fileName = Path.GetFileName(datFilePath);
                        string datFileNameWithoutExt = Path.GetFileNameWithoutExtension(datFilePath);
                        string extractDir = Path.Combine(fileDirectory, datFileNameWithoutExt);

                        ExtractionProgress?.Invoke(this, $"正在处理:{fileName}");

                        try
                        {
                            var processStartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"\"{datFilePath}\" {detectedVersion}",
                                WorkingDirectory = fileDirectory,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                StandardOutputEncoding = Encoding.UTF8,
                                StandardErrorEncoding = Encoding.UTF8
                            };

                            using (var process = Process.Start(processStartInfo))
                            {
                                if (process == null)
                                {
                                    ExtractionError?.Invoke(this, $"无法启动解包进程:{fileName}");
                                    OnExtractionFailed($"无法启动解包进程:{fileName}");
                                    continue;
                                }

                                StringBuilder outputBuilder = new StringBuilder();
                                StringBuilder errorBuilder = new StringBuilder();

                                process.OutputDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        string decodedData = e.Data;
                                        ExtractionProgress?.Invoke(this, decodedData);
                                        outputBuilder.AppendLine(decodedData);
                                    }
                                };

                                process.ErrorDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        string decodedData = e.Data;
                                        ExtractionError?.Invoke(this, $"错误:{decodedData}");
                                        errorBuilder.AppendLine(decodedData);
                                    }
                                };

                                process.BeginOutputReadLine();
                                process.BeginErrorReadLine();
                                process.WaitForExit();

                                if (process.ExitCode != 0)
                                {
                                    ExtractionError?.Invoke(this, $"{fileName}处理失败,错误代码:{process.ExitCode}");
                                    OnExtractionFailed($"{fileName}处理失败,错误代码:{process.ExitCode}");

                                    if (errorBuilder.Length > 0)
                                    {
                                        ExtractionError?.Invoke(this, $"详细错误:{errorBuilder.ToString()}");
                                    }
                                }
                                else
                                {
                                    ExtractionProgress?.Invoke(this, $"处理成功:{fileName}");
                                    extractedCount++;

                                    if (Directory.Exists(extractDir))
                                    {
                                        var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
                                        totalExtractedFiles += extractedFiles.Length;
                                        foreach (var extractedFile in extractedFiles)
                                        {
                                            string relativePath = Path.GetRelativePath(extractDir, extractedFile);
                                            ExtractionProgress?.Invoke(this, $"已提取:{relativePath}");
                                            OnFileExtracted(extractedFile);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理异常:{ex.Message}");
                            OnExtractionFailed($"{fileName} 处理错误:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"处理完成,成功处理{extractedCount}/{datFiles.Count}个.dat文件,共提取{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnExtractionFailed($"严重错误:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private uint DetectVersionFromExe(string directoryPath)
        {
            var exeFiles = Directory.EnumerateFiles(directoryPath, "*.exe", SearchOption.TopDirectoryOnly);
            foreach (var exe in exeFiles)
            {
                string name = Path.GetFileNameWithoutExtension(exe).ToLower();
                if (name.StartsWith("th"))
                {
                    string numPart = name.Substring(2);
                    int underscorePos = numPart.IndexOf('_');
                    if (underscorePos > 0)
                    {
                        numPart = numPart.Substring(0, underscorePos);
                    }

                    if (uint.TryParse(numPart, out uint version))
                    {
                        return version;
                    }
                }
            }
            return 0;
        }

        private uint DetectVersionFromDatFiles(List<string> datFiles)
        {
            foreach (var datFile in datFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(datFile).ToLower();

                Match match = Regex.Match(fileName, @"th(\d+)");
                if (match.Success && uint.TryParse(match.Groups[1].Value, out uint version))
                {
                    if (version >= 95)
                    {
                        return version;
                    }
                }

                match = Regex.Match(fileName, @"th(\d+)([a-z])");
                if (match.Success && uint.TryParse(match.Groups[1].Value, out version))
                {
                    if (version >= 95)
                    {
                        return version;
                    }
                }

                match = Regex.Match(fileName, @"th(\d+)_");
                if (match.Success && uint.TryParse(match.Groups[1].Value, out version))
                {
                    if (version >= 95)
                    {
                        return version;
                    }
                }
            }
            return 0;
        }

        private uint DetectVersion(string directoryPath, List<string> datFiles)
        {
            uint version = DetectVersionFromExe(directoryPath);
            if (version != 0)
            {
                return version;
            }

            version = DetectVersionFromDatFiles(datFiles);
            if (version != 0)
            {
                return version;
            }

            return 0;
        }
    }
}