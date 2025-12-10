using System.IO.Compression;

namespace super_toolbox
{
    public class Xenoblade_Pcbeb_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] XBC1_HEADER = { 0x78, 0x62, 0x63, 0x31 };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string baseDir = directoryPath;
            if (!Directory.Exists(baseDir))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            string outputDir = Path.Combine(baseDir, "Extracted");
            Directory.CreateDirectory(outputDir);
            var pcbebFiles = Directory.GetFiles(baseDir, "*.pcbeb", SearchOption.AllDirectories)
                .Where(file => !file.Contains("\\Extracted\\"))
                .ToList();
            if (pcbebFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, $"在目录中未找到.pcbeb文件");
                OnExtractionFailed($"在目录中未找到.pcbeb文件");
                return;
            }
            TotalFilesToExtract = pcbebFiles.Count;
            int processedFiles = 0;
            int totalDatFilesExtracted = 0;
            try
            {
                foreach (var filePath in pcbebFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;
                    string fileName = Path.GetFileName(filePath);
                    ExtractionProgress?.Invoke(this, $"正在处理文件:{fileName} ({processedFiles}/{TotalFilesToExtract})");
                    try
                    {
                        string fileDir = Path.GetDirectoryName(filePath) ?? baseDir;
                        string xbc1Dir = Path.Combine(fileDir, $"xbc1_{Path.GetFileNameWithoutExtension(fileName)}");
                        string zlibDir = Path.Combine(fileDir, $"zlib_{Path.GetFileNameWithoutExtension(fileName)}");
                        Directory.CreateDirectory(xbc1Dir);
                        Directory.CreateDirectory(zlibDir);
                        int extractedCount = await ProcessPcbebFileAsync(filePath, xbc1Dir, zlibDir, outputDir, cancellationToken);
                        totalDatFilesExtracted += extractedCount;
                        CleanupTempFolder(xbc1Dir);
                        CleanupTempFolder(zlibDir);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{fileName}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{fileName}时出错:{ex.Message}");
                    }
                }
                ExtractionProgress?.Invoke(this, $"处理完成，共处理{pcbebFiles.Count}个pcbeb文件，提取出来了{totalDatFilesExtracted}个dat文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task<int> ProcessPcbebFileAsync(string filePath, string xbc1Dir, string zlibDir, string outputDir, CancellationToken cancellationToken)
        {
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            ExtractionProgress?.Invoke(this, $"{baseFileName}:开始处理...");
            int successCount = 0;
            try
            {
                List<string> xbc1Files = await ExtractXbc1ToFolderAsync(filePath, baseFileName, xbc1Dir, cancellationToken);
                if (xbc1Files.Count == 0)
                {
                    ExtractionProgress?.Invoke(this, $"{baseFileName}:未找到XBC1文件");
                    return 0;
                }
                ExtractionProgress?.Invoke(this, $"{baseFileName}:找到{xbc1Files.Count}个XBC1文件");
                List<string> firstZlibFiles = new List<string>();
                foreach (var xbc1File in xbc1Files)
                {
                    string zlibFile = await ProcessSingleXbc1FileAsync(xbc1File, zlibDir, cancellationToken);
                    if (!string.IsNullOrEmpty(zlibFile))
                    {
                        firstZlibFiles.Add(zlibFile);
                    }
                }
                if (firstZlibFiles.Count == 0)
                {
                    ExtractionProgress?.Invoke(this, $"{baseFileName}:没有成功处理的XBC1文件");
                    return 0;
                }
                successCount = 0;
                foreach (var zlibFile in firstZlibFiles)
                {
                    if (await ProcessZlibFileAsync(zlibFile, outputDir, cancellationToken))
                    {
                        successCount++;
                    }
                }
                ExtractionProgress?.Invoke(this, $"{baseFileName}:处理完成，成功解压{successCount}个文件");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"{baseFileName}:处理失败:{ex.Message}");
                throw;
            }
            return successCount;
        }

        private async Task<List<string>> ExtractXbc1ToFolderAsync(string filePath, string baseFileName, string xbc1Dir, CancellationToken cancellationToken)
        {
            List<string> xbc1Files = new List<string>();
            try
            {
                byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                int count = 0;
                int index = 0;
                while (index < content.Length)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    int headerStartIndex = IndexOf(content, XBC1_HEADER, index);
                    if (headerStartIndex == -1)
                    {
                        break;
                    }
                    int nextHeaderIndex = IndexOf(content, XBC1_HEADER, headerStartIndex + XBC1_HEADER.Length);
                    int endIndex = nextHeaderIndex == -1 ? content.Length : nextHeaderIndex;
                    byte[] extractedData = new byte[endIndex - headerStartIndex];
                    Array.Copy(content, headerStartIndex, extractedData, 0, extractedData.Length);
                    count++;
                    string outputFileName = $"{baseFileName}_{count}.xbc1";
                    string outputFilePath = Path.Combine(xbc1Dir, outputFileName);
                    outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);
                    await File.WriteAllBytesAsync(outputFilePath, extractedData, cancellationToken);
                    xbc1Files.Add(outputFilePath);
                    index = headerStartIndex + 1;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取XBC1文件失败:{ex.Message}");
            }
            return xbc1Files;
        }

        private async Task<string> ProcessSingleXbc1FileAsync(string xbc1FilePath, string zlibDir, CancellationToken cancellationToken)
        {
            try
            {
                ThrowIfCancellationRequested(cancellationToken);
                string baseFileName = Path.GetFileNameWithoutExtension(xbc1FilePath);
                byte[] xbc1Data = await File.ReadAllBytesAsync(xbc1FilePath, cancellationToken);
                if (xbc1Data.Length < 48)
                {
                    ExtractionError?.Invoke(this, $"{baseFileName}:数据长度不足48字节");
                    return string.Empty;
                }
                byte[] dataWithoutHeader = new byte[xbc1Data.Length - 48];
                Array.Copy(xbc1Data, 48, dataWithoutHeader, 0, dataWithoutHeader.Length);
                byte[] decompressedData = await ZlibDecompressFirstAsync(dataWithoutHeader, cancellationToken);
                if (decompressedData == null || decompressedData.Length == 0)
                {
                    return string.Empty;
                }
                string zlibFileName = $"{baseFileName}.zlib";
                string zlibFilePath = Path.Combine(zlibDir, zlibFileName);
                zlibFilePath = await GenerateUniqueFilePathAsync(zlibFilePath, cancellationToken);
                await File.WriteAllBytesAsync(zlibFilePath, decompressedData, cancellationToken);
                return zlibFilePath;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"{Path.GetFileName(xbc1FilePath)}:处理失败:{ex.Message}");
                return string.Empty;
            }
        }

        private async Task<bool> ProcessZlibFileAsync(string zlibFilePath, string outputDir, CancellationToken cancellationToken)
        {
            try
            {
                ThrowIfCancellationRequested(cancellationToken);
                string baseFileName = Path.GetFileNameWithoutExtension(zlibFilePath);
                byte[] zlibData = await File.ReadAllBytesAsync(zlibFilePath, cancellationToken);
                if (zlibData.Length == 0)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(zlibFilePath)}:文件为空");
                    return false;
                }
                string datFileName = $"{baseFileName}.dat";
                string datFilePath = Path.Combine(outputDir, datFileName);
                datFilePath = await GenerateUniqueFilePathAsync(datFilePath, cancellationToken);
                await File.WriteAllBytesAsync(datFilePath, zlibData, cancellationToken);
                OnFileExtracted(datFilePath);
                return true;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"{Path.GetFileName(zlibFilePath)}:处理失败:{ex.Message}");
                return false;
            }
        }

        private async Task<byte[]> ZlibDecompressFirstAsync(byte[] compressedData, CancellationToken cancellationToken)
        {
            try
            {
                using (MemoryStream inputStream = new MemoryStream(compressedData))
                using (MemoryStream outputStream = new MemoryStream())
                {
                    using (ZLibStream decompressionStream = new ZLibStream(inputStream, CompressionMode.Decompress))
                    {
                        await decompressionStream.CopyToAsync(outputStream, cancellationToken);
                    }
                    return outputStream.Length > 0 ? outputStream.ToArray() : null!;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"Zlib解压失败:{ex.Message}");
                return null!;
            }
        }

        private async Task<string> GenerateUniqueFilePathAsync(string filePath, CancellationToken cancellationToken)
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
                ThrowIfCancellationRequested(cancellationToken);
            }
            while (File.Exists(newPath));
            return newPath;
        }

        private void CleanupTempFolder(string tempDir)
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch { }
        }

        private new static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            for (int i = startIndex; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
