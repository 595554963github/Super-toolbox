using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;

namespace super_toolbox
{
    public class Lz4c_Decompressor : BaseExtractor
    {
        private string _lz4cPath;

        public Lz4c_Decompressor()
        {
            _lz4cPath = ExtractLz4cToTemp();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsLz4File).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        OnExtractionFailed("未找到有效的LZ4压缩文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);

                    TotalFilesToExtract = filesToProcess.Length;

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (DecompressLz4cFile(filePath, decompressedDir))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            string originalExtension = GetOriginalExtension(filePath);
                            string outputPath = Path.Combine(decompressedDir, fileName + originalExtension);
                            OnFileExtracted(outputPath);
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"解压失败: {ex.Message}");
            }
        }

        private bool IsLz4File(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".lz4";
        }

        private string GetOriginalExtension(string compressedFilePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(compressedFilePath);
            if (fileName.Contains('.'))
            {
                return Path.GetExtension(fileName);
            }
            return "";
        }

        private bool DecompressLz4cFile(string inputPath, string outputDir)
        {
            try
            {
                string workingDirectory = Path.GetDirectoryName(inputPath) ?? string.Empty;
                string fileName = Path.GetFileName(inputPath);

                if (string.IsNullOrEmpty(workingDirectory))
                {
                    OnExtractionFailed("无法确定工作目录");
                    return false;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _lz4cPath,
                    Arguments = $"uncompress \"{fileName}\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        OnExtractionFailed($"LZ4C解压错误: {error}");
                        return false;
                    }

                    string decompressedFileName = Path.GetFileNameWithoutExtension(fileName);
                    string decompressedFile = Path.Combine(workingDirectory, decompressedFileName);

                    if (File.Exists(decompressedFile))
                    {
                        string finalOutputPath = Path.Combine(outputDir, Path.GetFileName(decompressedFile));
                        File.Move(decompressedFile, finalOutputPath);
                        return true;
                    }

                    string decompressedFileWithExt = Path.Combine(workingDirectory, decompressedFileName + GetOriginalExtension(inputPath));
                    if (File.Exists(decompressedFileWithExt))
                    {
                        string finalOutputPath = Path.Combine(outputDir, Path.GetFileName(decompressedFileWithExt));
                        File.Move(decompressedFileWithExt, finalOutputPath);
                        return true;
                    }

                    OnExtractionFailed($"找不到解压后的文件: {decompressedFileName}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"LZ4C解压错误: {ex.Message}");
                return false;
            }
        }

        private string ExtractLz4cToTemp()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "super_toolbox_lz4c.exe");

            if (File.Exists(tempPath))
            {
                return tempPath;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("lz4c.exe"));

            if (string.IsNullOrEmpty(resourceName))
            {
                throw new FileNotFoundException("找不到嵌入的lz4c.exe资源");
            }

            using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                    throw new FileNotFoundException("无法读取嵌入的lz4c.exe资源");

                using (var fileStream = File.Create(tempPath))
                {
                    resourceStream.CopyTo(fileStream);
                }
            }

            return tempPath;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}