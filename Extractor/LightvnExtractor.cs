using ICSharpCode.SharpZipLib.Zip;
using System.Text;

namespace super_toolbox
{
    public class LightvnExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly Dictionary<string, byte[][]> FileTypeSignatures = new Dictionary<string, byte[][]>
        {
            { ".ogg", new[] { new byte[] { 0x4F, 0x67, 0x67, 0x53 } } },
            { ".png", new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } } },
            { ".jpg", new[] { new byte[] { 0xFF, 0xD8, 0xFF }, new byte[] { 0xFF, 0xD9 } } },
            { ".mpg", new[] { new byte[] { 0x00, 0x00, 0x01, 0xBA } } },
            { ".webp", new[] {
                new byte[] { 0x52, 0x49, 0x46, 0x46 }, // RIFF header
                new byte[] { 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38 } // VP8 chunk
            } },
            { ".wav", new[] {
                new byte[] { 0x52, 0x49, 0x46, 0x46 }, // RIFF header
                new byte[] { 0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74 } // WAVEfmt chunk (at offset 8)
            } },
            { ".txt", new[] { new byte[] { 0x0D, 0x0A }, new byte[] { 0x0D, 0x0A } } },
            { ".mp4", new[] {
                new byte[] { 0x66, 0x74, 0x79, 0x70 }, // ftyp box
                new byte[] { 0x6D, 0x76, 0x68, 0x64 }  // mvhd brand (common in MP4 files)
            } },
        };
        private static readonly byte[] PKZIP = { 0x50, 0x4B, 0x03, 0x04 };
        private static readonly byte[] KEY = { 0x64, 0x36, 0x63, 0x35, 0x66, 0x4B, 0x49, 0x33, 0x47, 0x67, 0x42, 0x57, 0x70, 0x5A, 0x46, 0x33, 0x54, 0x7A, 0x36, 0x69, 0x61, 0x33, 0x6B, 0x46, 0x30 };
        private static readonly byte[] REVERSED_KEY = { 0x30, 0x46, 0x6B, 0x33, 0x61, 0x69, 0x36, 0x7A, 0x54, 0x33, 0x46, 0x5A, 0x70, 0x57, 0x42, 0x67, 0x47, 0x33, 0x49, 0x4B, 0x66, 0x35, 0x63, 0x36, 0x64 };
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();
            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;
            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;
                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");
                    try
                    {
                        if (IsVndat(filePath))
                        {
                            await UnpackVndatAsync(filePath, extractedDir, cancellationToken, extractedFiles);
                        }
                        else if (Path.GetExtension(filePath).Contains("mcdat"))
                        {
                            string outputFileName = $"{Path.GetFileNameWithoutExtension(filePath)}.dec";
                            string outputFilePath = Path.Combine(extractedDir, outputFileName);
                            outputFilePath = GenerateUniquePath(outputFilePath);

                            ExtractionProgress?.Invoke(this, $"解密文件中:{Path.GetFileName(filePath)}");
                            await XORAsync(filePath, outputFilePath, cancellationToken);

                            extractedFiles.Add(outputFilePath);
                            OnFileExtracted(outputFilePath);
                            ExtractionProgress?.Invoke(this, $"已解密:{Path.GetFileName(outputFilePath)}");
                        }
                        else if (Path.GetExtension(filePath).Contains("dec"))
                        {
                            string outputFileName = $"{Path.GetFileNameWithoutExtension(filePath)}.enc";
                            string outputFilePath = Path.Combine(extractedDir, outputFileName);
                            outputFilePath = GenerateUniquePath(outputFilePath);

                            ExtractionProgress?.Invoke(this, $"加密文件中:{Path.GetFileName(filePath)}");
                            await XORAsync(filePath, outputFilePath, cancellationToken);

                            extractedFiles.Add(outputFilePath);
                            OnFileExtracted(outputFilePath);
                            ExtractionProgress?.Invoke(this, $"已加密:{Path.GetFileName(outputFilePath)}");
                        }
                        else
                        {
                            ExtractionProgress?.Invoke(this, $"不支持的文件类型:{Path.GetFileName(filePath)}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{filePath}时出错:{ex.Message}");
                    }
                }
                if (extractedFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, "开始扫描并重命名文件...");
                    await ScanAndRenameFilesAsync(extractedDir, cancellationToken);
                    ExtractionProgress?.Invoke(this, "开始按文件类型分类...");
                    await ClassifyFilesByExtensionAsync(extractedDir, cancellationToken);
                    ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，未找到可提取的文件");
                }
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }
        private async Task UnpackVndatAsync(string vndatFile, string outputFolder, CancellationToken cancellationToken, List<string> extractedFiles)
        {
            string zipPassword = Encoding.UTF8.GetString(KEY);
            bool usePassword = IsPasswordProtectedZip(vndatFile);
            using var zipFile = new ZipFile(vndatFile);
            if (usePassword)
            {
                ExtractionProgress?.Invoke(this, $"{Path.GetFileName(vndatFile)} 受密码保护，使用密码解密");
                zipFile.Password = zipPassword;
            }
            if (zipFile.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"正在解压{Path.GetFileName(vndatFile)}...");
                foreach (ZipEntry entry in zipFile)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    if (!entry.IsDirectory)
                    {
                        try
                        {
                            ExtractionProgress?.Invoke(this, $"正在提取:{entry.Name}");
                            using Stream inputStream = zipFile.GetInputStream(entry);
                            using MemoryStream ms = new MemoryStream();
                            await inputStream.CopyToAsync(ms, cancellationToken);
                            byte[] fileData = ms.ToArray();
                            string fileName = Path.GetFileNameWithoutExtension(entry.Name);
                            string tempPath = GenerateUniquePath(Path.Combine(outputFolder, $"{fileName}.temp"));
                            await File.WriteAllBytesAsync(tempPath, fileData, cancellationToken);
                            extractedFiles.Add(tempPath);
                            OnFileExtracted(tempPath);
                            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(tempPath)}");
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"提取{entry.Name}时失败:{ex.Message}");
                            OnExtractionFailed($"提取{entry.Name}时失败:{ex.Message}");
                        }
                    }
                }
                if (!usePassword)
                {
                    var tempFiles = Directory.GetFiles(outputFolder, "*.temp", SearchOption.AllDirectories);
                    foreach (string tempFile in tempFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        ExtractionProgress?.Invoke(this, $"正在XOR处理:{Path.GetFileName(tempFile)}");
                        await XORAsync(tempFile, null, cancellationToken);
                    }
                }
            }
        }
        private async Task ScanAndRenameFilesAsync(string directory, CancellationToken cancellationToken)
        {
            try
            {
                var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                int totalFiles = files.Length;
                int processed = 0;
                ExtractionProgress?.Invoke(this, $"找到{totalFiles}个文件需要扫描...");
                foreach (string filePath in files)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    try
                    {
                        byte[] fileData = await File.ReadAllBytesAsync(filePath, cancellationToken);
                        string detectedExtension = DetectFileExtension(fileData);
                        string currentExtension = Path.GetExtension(filePath);
                        if (currentExtension != detectedExtension)
                        {
                            string newPath = Path.ChangeExtension(filePath, detectedExtension);
                            newPath = GenerateUniquePath(newPath);
                            File.Move(filePath, newPath);
                            ExtractionProgress?.Invoke(this, $"重命名:{Path.GetFileName(filePath)} -> {Path.GetFileName(newPath)}");
                        }
                        processed++;
                        if (processed % 10 == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"已扫描:{processed}/{totalFiles}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                    }
                }
                ExtractionProgress?.Invoke(this, $"扫描完成:共处理{processed}个文件");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"扫描目录时出错:{ex.Message}");
            }
        }
        private async Task ClassifyFilesByExtensionAsync(string directory, CancellationToken cancellationToken)
        {
            try
            {
                var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                int totalFiles = files.Length;
                int processed = 0;
                ExtractionProgress?.Invoke(this, $"找到{totalFiles}个文件需要分类...");
                foreach (string filePath in files)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    try
                    {
                        string extension = Path.GetExtension(filePath).TrimStart('.').ToLower();
                        if (string.IsNullOrEmpty(extension))
                            extension = "unknown";
                        string subFolder = Path.Combine(directory, extension);
                        Directory.CreateDirectory(subFolder);
                        string fileName = Path.GetFileName(filePath);
                        string destinationPath = Path.Combine(subFolder, fileName);
                        destinationPath = GenerateUniquePath(destinationPath);
                        File.Move(filePath, destinationPath);
                        ExtractionProgress?.Invoke(this, $"移动文件:{Path.GetFileName(filePath)} -> {extension}/{fileName}");
                        processed++;
                        if (processed % 10 == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"已分类:{processed}/{totalFiles}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                    }
                }
                ExtractionProgress?.Invoke(this, $"文件分类完成:共处理{processed}个文件");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"分类文件时出错:{ex.Message}");
            }
        }
        private string GenerateUniquePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;
            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{extension}");
                counter++;
            }
            while (File.Exists(newPath));
            return newPath;
        }
        private string DetectFileExtension(byte[] fileData)
        {
            foreach (var entry in FileTypeSignatures)
            {
                byte[][] signatures = entry.Value;
                bool allSignaturesMatch = true;
                for (int i = 0; i < signatures.Length; i++)
                {
                    byte[] signature = signatures[i];
                    int offset = 0;
                    if (entry.Key == ".wav" && i == 1)
                        offset = 8;
                    if (entry.Key == ".mp4" && i == 0)
                        offset = 4;
                    if (entry.Key == ".mp4" && i > 0)
                        continue;
                    if (fileData.Length < offset + signature.Length ||
                        !ByteArrayStartsWith(fileData, signature, offset))
                    {
                        allSignaturesMatch = false;
                        break;
                    }
                }
                if (allSignaturesMatch)
                    return entry.Key;
            }
            return ".ttf";
        }
        private bool ByteArrayStartsWith(byte[] data, byte[] prefix, int offset = 0)
        {
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[offset + i] != prefix[i])
                    return false;
            }
            return true;
        }
        private bool IsVndat(string filePath)
        {
            try
            {
                byte[] fileSignature = new byte[4];

                using FileStream file = File.OpenRead(filePath);
                int bytesRead = file.Read(fileSignature, 0, fileSignature.Length);
                if (bytesRead != fileSignature.Length)
                {
                    return false;
                }
                for (int i = 0; i < fileSignature.Length; i++)
                {
                    if (fileSignature[i] != PKZIP[i])
                        return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"读取{Path.GetFileName(filePath)}时出错:{ex.Message}");
                return false;
            }
        }
        private bool IsPasswordProtectedZip(string filePath)
        {
            try
            {
                using FileStream fileStream = new(filePath, FileMode.Open, FileAccess.Read);
                using ZipInputStream zipStream = new(fileStream);
                ZipEntry entry;
                while ((entry = zipStream.GetNextEntry()) != null)
                {
                    if (entry.IsCrypted)
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        private byte[] XOR(byte[] buffer)
        {
            if (buffer.Length < 100)
            {
                if (buffer.Length <= 0)
                    return buffer;
                for (int i = 0; i < buffer.Length; i++)
                    buffer[i] ^= REVERSED_KEY[i % KEY.Length];
            }
            else
            {
                for (int i = 0; i < 100; i++)
                    buffer[i] ^= KEY[i % KEY.Length];
                for (int i = 0; i < 99; i++)
                    buffer[buffer.Length - 99 + i] ^= REVERSED_KEY[i % KEY.Length];
            }
            return buffer;
        }
        private async Task XORAsync(string filePath, string? outputFilePath = null, CancellationToken cancellationToken = default)
        {
            try
            {
                byte[] buffer = await File.ReadAllBytesAsync(filePath, cancellationToken);
                buffer = XOR(buffer);
                await File.WriteAllBytesAsync(outputFilePath ?? filePath, buffer, cancellationToken);
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"XOR处理文件时出错:{ex.Message}");
                throw;
            }
        }
    }
}