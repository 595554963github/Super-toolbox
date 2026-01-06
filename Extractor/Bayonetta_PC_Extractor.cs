using System.Text;

namespace super_toolbox
{
    public class Bayonetta_PC_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public Bayonetta_PC_Extractor() { }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                int totalFilesExtracted = 0;
                List<string> extractedFiles = new List<string>();

                string[] datFiles = Directory.GetFiles(directoryPath, "*.dat");
                foreach (string file in datFiles)
                {
                    totalFilesExtracted += await ExtractFile(file, directoryPath, cancellationToken);
                }

                string[] effFiles = Directory.GetFiles(directoryPath, "*.eff");
                foreach (string file in effFiles)
                {
                    totalFilesExtracted += await ExtractFile(file, directoryPath, cancellationToken);
                }

                string[] wmbFiles = Directory.GetFiles(directoryPath, "*.wmb");
                foreach (string file in wmbFiles)
                {
                    totalFilesExtracted += await ExtractFile(file, directoryPath, cancellationToken);
                }

                string[] wtbFiles = Directory.GetFiles(directoryPath, "*.wtb");
                foreach (string file in wtbFiles)
                {
                    totalFilesExtracted += await ExtractFile(file, directoryPath, cancellationToken);
                }

                string[] modFiles = Directory.GetFiles(directoryPath, "*.mod");
                foreach (string file in modFiles)
                {
                    totalFilesExtracted += await ExtractFile(file, directoryPath, cancellationToken);
                }

                if (totalFilesExtracted == 0)
                {
                    string errorMsg = $"在目录{directoryPath}中找不到支持的文件";
                    ExtractionError?.Invoke(this, errorMsg);
                    OnExtractionFailed(errorMsg);
                    return;
                }

                TotalFilesToExtract = totalFilesExtracted;
                ExtractionProgress?.Invoke(this, $"完成!成功提取{totalFilesExtracted}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                string errorMsg = $"提取失败:{ex.Message}";
                ExtractionError?.Invoke(this, errorMsg);
                OnExtractionFailed(errorMsg);
                throw;
            }
        }

        private async Task<int> ExtractFile(string filePath, string baseDirectory, CancellationToken cancellationToken)
        {
            ExtractionProgress?.Invoke(this, $"开始处理{Path.GetFileName(filePath)}...");

            byte[] fileData = await File.ReadAllBytesAsync(filePath, cancellationToken);

            if (fileData.Length < 4)
            {
                ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}文件过小,无法解析");
                return 0;
            }

            string fileId = Encoding.ASCII.GetString(fileData, 0, 4);

            if (fileId.StartsWith("DAT"))
            {
                return await ExtractDATFile(filePath, fileData, baseDirectory, cancellationToken);
            }
            else if (fileId.StartsWith("EF2"))
            {
                return await ExtractEFFFile(filePath, fileData, baseDirectory, cancellationToken);
            }
            else if (fileId.StartsWith("WMB"))
            {
                return await ExtractWMBFile(filePath, fileData, baseDirectory, cancellationToken);
            }
            else if (fileId.StartsWith("WTB"))
            {
                return await ExtractWTBFile(filePath, fileData, baseDirectory, cancellationToken);
            }
            else if (fileId.StartsWith("MOD"))
            {
                return await ExtractMODFile(filePath, fileData, baseDirectory, cancellationToken);
            }
            else
            {
                ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}不支持的文件格式");
                return 0;
            }
        }

        #region DAT文件提取
        private async Task<int> ExtractDATFile(string filePath, byte[] fileData, string baseDirectory, CancellationToken cancellationToken)
        {
            try
            {
                if (fileData.Length < 0x20)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}文件过小,无法解析");
                    return 0;
                }

                int entryCount = BitConverter.ToInt32(fileData, 0x04);
                if (entryCount <= 0)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}条目数量无效");
                    return 0;
                }

                int fileStartsOffset = BitConverter.ToInt32(fileData, 0x08);
                int extensionsOffset = BitConverter.ToInt32(fileData, 0x0C);
                int filenamesOffset = BitConverter.ToInt32(fileData, 0x10);
                int fileSizesOffset = BitConverter.ToInt32(fileData, 0x14);

                string[] fileNames = new string[entryCount];
                if (filenamesOffset < fileData.Length && filenamesOffset > 0)
                {
                    int filenameLength = BitConverter.ToInt32(fileData, filenamesOffset);
                    int filenameTableOffset = filenamesOffset + 4;

                    for (int i = 0; i < entryCount; i++)
                    {
                        if (filenameTableOffset + filenameLength > fileData.Length)
                            break;

                        fileNames[i] = Encoding.ASCII.GetString(fileData, filenameTableOffset, filenameLength).TrimEnd('\0');
                        filenameTableOffset += filenameLength;
                    }
                }

                uint[] fileStarts = new uint[entryCount];
                if (fileStartsOffset < fileData.Length && fileStartsOffset > 0)
                {
                    for (int i = 0; i < entryCount; i++)
                    {
                        int offsetPos = fileStartsOffset + i * 4;
                        if (offsetPos + 4 > fileData.Length)
                            break;

                        fileStarts[i] = BitConverter.ToUInt32(fileData, offsetPos);
                    }
                }

                uint[] fileSizes = new uint[entryCount];
                if (fileSizesOffset < fileData.Length && fileSizesOffset > 0)
                {
                    for (int i = 0; i < entryCount; i++)
                    {
                        int sizePos = fileSizesOffset + i * 4;
                        if (sizePos + 4 > fileData.Length)
                            break;

                        fileSizes[i] = BitConverter.ToUInt32(fileData, sizePos);
                    }
                }

                string outputDir = Path.Combine(baseDirectory, Path.GetFileNameWithoutExtension(filePath));
                Directory.CreateDirectory(outputDir);

                int extractedCount = 0;
                for (int i = 0; i < entryCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint startOffset = fileStarts[i];
                    uint fileSize = fileSizes[i];

                    if (startOffset >= fileData.Length)
                    {
                        ExtractionError?.Invoke(this, $"条目{i}起始偏移超出文件范围");
                        continue;
                    }

                    if (startOffset + fileSize > fileData.Length)
                    {
                        ExtractionError?.Invoke(this, $"条目{i}文件大小超出文件范围");
                        fileSize = (uint)(fileData.Length - startOffset);
                    }

                    if (fileSize == 0)
                    {
                        ExtractionError?.Invoke(this, $"条目{i}文件大小为0");
                        continue;
                    }

                    byte[] entryData = new byte[fileSize];
                    Array.Copy(fileData, (int)startOffset, entryData, 0, (int)fileSize);

                    string outputPath;
                    if (i < fileNames.Length && !string.IsNullOrEmpty(fileNames[i]))
                    {
                        string fullPath = fileNames[i].TrimStart('/', '\\');
                        outputPath = Path.Combine(outputDir, fullPath);
                        string? fileDir = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                        {
                            Directory.CreateDirectory(fileDir);
                        }
                    }
                    else
                    {
                        string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputFileName = $"{baseFileName}_{i + 1}.bin";
                        outputPath = Path.Combine(outputDir, outputFileName);
                    }

                    outputPath = GetUniqueFileName(outputPath);
                    await File.WriteAllBytesAsync(outputPath, entryData, cancellationToken);
                    OnFileExtracted(outputPath);
                    extractedCount++;

                    ExtractionProgress?.Invoke(this, $"提取DAT条目{i + 1}/{entryCount}: {Path.GetFileName(outputPath)}");
                }

                return extractedCount;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取DAT文件失败:{ex.Message}");
                return 0;
            }
        }
        #endregion

        #region EFF文件提取
        private async Task<int> ExtractEFFFile(string filePath, byte[] fileData, string baseDirectory, CancellationToken cancellationToken)
        {
            try
            {
                if (fileData.Length < 12)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}文件过小,无法解析");
                    return 0;
                }

                int entryCount = BitConverter.ToInt32(fileData, 0x04);
                ExtractionProgress?.Invoke(this, $"发现{entryCount}个文件");

                if (entryCount <= 0)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}条目数量无效");
                    return 0;
                }

                List<uint> startOffsets = new List<uint>();
                List<int> indices = new List<int>();
                int currentPos = 0x08;

                for (int i = 0; i < entryCount; i++)
                {
                    if (currentPos + 8 > fileData.Length)
                        break;

                    int index = BitConverter.ToInt32(fileData, currentPos);
                    indices.Add(index);
                    currentPos += 4;

                    uint startOffset = BitConverter.ToUInt32(fileData, currentPos);
                    startOffsets.Add(startOffset);
                    currentPos += 4;
                }

                startOffsets.Add((uint)fileData.Length);

                string outputDir = Path.Combine(baseDirectory, Path.GetFileNameWithoutExtension(filePath));
                Directory.CreateDirectory(outputDir);

                int extractedCount = 0;
                for (int i = 0; i < entryCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint startOffset = startOffsets[i];
                    uint endOffset = startOffsets[i + 1];

                    if (startOffset >= fileData.Length)
                    {
                        ExtractionError?.Invoke(this, $"条目{i}起始偏移超出文件范围");
                        continue;
                    }

                    if (endOffset > fileData.Length)
                        endOffset = (uint)fileData.Length;

                    uint fileSize = endOffset - startOffset;

                    if (fileSize == 0)
                    {
                        ExtractionError?.Invoke(this, $"条目{i}文件大小为0");
                        continue;
                    }

                    byte[] entryData = new byte[fileSize];
                    Array.Copy(fileData, (int)startOffset, entryData, 0, (int)fileSize);

                    string fileType = "bin";
                    if (fileSize >= 4)
                    {
                        string typeHeader = Encoding.ASCII.GetString(entryData, 0, 4).TrimEnd('\0');
                        if (typeHeader.Length > 0)
                            fileType = typeHeader;
                    }

                    string baseName = Path.GetFileNameWithoutExtension(filePath);
                    string outputFileName = $"{baseName}.{i:0000}.{fileType}";
                    string outputPath = Path.Combine(outputDir, outputFileName);

                    outputPath = GetUniqueFileName(outputPath);
                    await File.WriteAllBytesAsync(outputPath, entryData, cancellationToken);
                    OnFileExtracted(outputPath);
                    extractedCount++;

                    ExtractionProgress?.Invoke(this, $"提取EFF条目{i + 1}/{entryCount}: {outputFileName}");
                }

                return extractedCount;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取EFF文件失败: {ex.Message}");
                return 0;
            }
        }
        #endregion

        #region WMB文件提取
        private async Task<int> ExtractWMBFile(string filePath, byte[] fileData, string baseDirectory, CancellationToken cancellationToken)
        {
            try
            {
                if (fileData.Length < 0x60)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}文件过小,无法解析");
                    return 0;
                }

                int numMaterials = BitConverter.ToInt32(fileData, 0x38);
                ExtractionProgress?.Invoke(this, $"发现{numMaterials}个材质");

                if (numMaterials <= 0)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}材质数量无效");
                    return 0;
                }

                int ofsMaterialsOfs = BitConverter.ToInt32(fileData, 0x3C);
                int ofsMaterials = BitConverter.ToInt32(fileData, 0x40);
                int ofsMeshesOfs = BitConverter.ToInt32(fileData, 0x50);

                if (ofsMaterialsOfs >= fileData.Length || ofsMaterials >= fileData.Length)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}偏移表超出范围");
                    return 0;
                }

                List<uint> materialStarts = new List<uint>();
                for (int i = 0; i < numMaterials; i++)
                {
                    int offsetPos = ofsMaterialsOfs + i * 4;
                    if (offsetPos + 4 > fileData.Length)
                        break;

                    int matOffset = BitConverter.ToInt32(fileData, offsetPos);
                    if (matOffset > 0)
                    {
                        materialStarts.Add((uint)(matOffset + ofsMaterials));
                    }
                }

                uint lastMaterialEnd = (ofsMeshesOfs > 0 && ofsMeshesOfs < fileData.Length) ? (uint)ofsMeshesOfs : (uint)fileData.Length;

                materialStarts.Sort();
                materialStarts.Add(lastMaterialEnd);

                string outputDir = Path.Combine(baseDirectory, Path.GetFileNameWithoutExtension(filePath));
                Directory.CreateDirectory(outputDir);

                int extractedCount = 0;
                for (int i = 0; i < materialStarts.Count - 1; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint startOffset = materialStarts[i];
                    uint endOffset = materialStarts[i + 1];

                    if (startOffset >= fileData.Length)
                        continue;

                    if (endOffset > fileData.Length)
                        endOffset = (uint)fileData.Length;

                    uint fileSize = endOffset - startOffset;

                    if (fileSize == 0)
                        continue;

                    byte[] entryData = new byte[fileSize];
                    Array.Copy(fileData, (int)startOffset, entryData, 0, (int)fileSize);

                    string baseName = Path.GetFileNameWithoutExtension(filePath);
                    string outputFileName = $"{baseName}.{i:0000}.mat";
                    string outputPath = Path.Combine(outputDir, outputFileName);

                    outputPath = GetUniqueFileName(outputPath);
                    await File.WriteAllBytesAsync(outputPath, entryData, cancellationToken);
                    OnFileExtracted(outputPath);
                    extractedCount++;

                    ExtractionProgress?.Invoke(this, $"提取WMB材质{i + 1}/{materialStarts.Count - 1}: {outputFileName}");
                }

                return extractedCount;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取WMB文件失败:{ex.Message}");
                return 0;
            }
        }
        #endregion

        #region WTB文件提取
        private async Task<int> ExtractWTBFile(string filePath, byte[] fileData, string baseDirectory, CancellationToken cancellationToken)
        {
            try
            {
                if (fileData.Length < 0x18)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}文件过小,无法解析");
                    return 0;
                }

                int dummy = BitConverter.ToInt32(fileData, 0x04);
                int entryCount = BitConverter.ToInt32(fileData, 0x08);
                ExtractionProgress?.Invoke(this, $"发现{entryCount}个纹理");

                if (entryCount <= 0)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}条目数量无效");
                    return 0;
                }

                int fileStartsOffset = BitConverter.ToInt32(fileData, 0x0C);
                int fileSizesOffset = BitConverter.ToInt32(fileData, 0x10);
                int unknownOffset = BitConverter.ToInt32(fileData, 0x14);

                if (fileStartsOffset >= fileData.Length || fileSizesOffset >= fileData.Length)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}偏移表超出范围");
                    return 0;
                }

                uint[] fileStarts = new uint[entryCount];
                for (int i = 0; i < entryCount; i++)
                {
                    int offsetPos = fileStartsOffset + i * 4;
                    if (offsetPos + 4 > fileData.Length)
                        break;

                    fileStarts[i] = BitConverter.ToUInt32(fileData, offsetPos);
                }

                uint[] fileSizes = new uint[entryCount];
                for (int i = 0; i < entryCount; i++)
                {
                    int sizePos = fileSizesOffset + i * 4;
                    if (sizePos + 4 > fileData.Length)
                        break;

                    fileSizes[i] = BitConverter.ToUInt32(fileData, sizePos);
                }

                string outputDir = Path.Combine(baseDirectory, Path.GetFileNameWithoutExtension(filePath));
                Directory.CreateDirectory(outputDir);

                int extractedCount = 0;
                for (int i = 0; i < entryCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint startOffset = fileStarts[i];
                    uint fileSize = fileSizes[i];

                    if (startOffset >= fileData.Length)
                    {
                        ExtractionError?.Invoke(this, $"纹理{i}起始偏移超出文件范围");
                        continue;
                    }

                    if (startOffset + fileSize > fileData.Length)
                    {
                        ExtractionError?.Invoke(this, $"纹理{i}文件大小超出文件范围");
                        fileSize = (uint)(fileData.Length - startOffset);
                    }

                    if (fileSize == 0)
                    {
                        ExtractionError?.Invoke(this, $"纹理{i}文件大小为0");
                        continue;
                    }

                    byte[] entryData = new byte[fileSize];
                    Array.Copy(fileData, (int)startOffset, entryData, 0, (int)fileSize);

                    string fileType = "dds";
                    if (fileSize >= 4)
                    {
                        string typeHeader = Encoding.ASCII.GetString(entryData, 0, 4).TrimEnd('\0');
                        if (typeHeader.Length > 0)
                            fileType = typeHeader;
                    }

                    string baseName = Path.GetFileNameWithoutExtension(filePath);
                    string outputFileName = $"{baseName}.{i:0000}.{fileType}";
                    string outputPath = Path.Combine(outputDir, outputFileName);

                    outputPath = GetUniqueFileName(outputPath);
                    await File.WriteAllBytesAsync(outputPath, entryData, cancellationToken);
                    OnFileExtracted(outputPath);
                    extractedCount++;

                    ExtractionProgress?.Invoke(this, $"提取WTB纹理{i + 1}/{entryCount}: {outputFileName}");
                }

                return extractedCount;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取WTB文件失败:{ex.Message}");
                return 0;
            }
        }
        #endregion

        #region MOD文件提取
        private async Task<int> ExtractMODFile(string filePath, byte[] fileData, string baseDirectory, CancellationToken cancellationToken)
        {
            try
            {
                if (fileData.Length < 12)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}文件过小,无法解析");
                    return 0;
                }

                int entryCount = BitConverter.ToInt32(fileData, 0x04);
                ExtractionProgress?.Invoke(this, $"发现{entryCount}个文件");

                if (entryCount <= 0)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}条目数量无效");
                    return 0;
                }

                List<uint> startOffsets = new List<uint>();
                List<int> indices = new List<int>();
                int currentPos = 0x08;

                for (int i = 0; i < entryCount; i++)
                {
                    if (currentPos + 8 > fileData.Length)
                        break;

                    int index = BitConverter.ToInt32(fileData, currentPos);
                    indices.Add(index);
                    currentPos += 4;

                    uint startOffset = BitConverter.ToUInt32(fileData, currentPos);
                    startOffsets.Add(startOffset);
                    currentPos += 4;
                }

                startOffsets.Add((uint)fileData.Length);

                string outputDir = Path.Combine(baseDirectory, Path.GetFileNameWithoutExtension(filePath));
                Directory.CreateDirectory(outputDir);

                int extractedCount = 0;
                for (int i = 0; i < entryCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint startOffset = startOffsets[i];
                    uint endOffset = startOffsets[i + 1];

                    if (startOffset >= fileData.Length)
                    {
                        ExtractionError?.Invoke(this, $"条目{i}起始偏移超出文件范围");
                        continue;
                    }

                    if (endOffset > fileData.Length)
                        endOffset = (uint)fileData.Length;

                    uint fileSize = endOffset - startOffset;

                    if (fileSize == 0)
                    {
                        ExtractionError?.Invoke(this, $"条目{i}文件大小为0");
                        continue;
                    }

                    byte[] entryData = new byte[fileSize];
                    Array.Copy(fileData, (int)startOffset, entryData, 0, (int)fileSize);

                    string fileType = "bin";
                    if (fileSize >= 4)
                    {
                        string typeHeader = Encoding.ASCII.GetString(entryData, 0, 4).TrimEnd('\0');
                        if (typeHeader.Length > 0)
                            fileType = typeHeader;
                    }

                    string baseName = Path.GetFileNameWithoutExtension(filePath);
                    string outputFileName = $"{baseName}.{i:0000}.{fileType}";
                    string outputPath = Path.Combine(outputDir, outputFileName);

                    outputPath = GetUniqueFileName(outputPath);
                    await File.WriteAllBytesAsync(outputPath, entryData, cancellationToken);
                    OnFileExtracted(outputPath);
                    extractedCount++;

                    ExtractionProgress?.Invoke(this, $"提取MOD条目{i + 1}/{entryCount}: {outputFileName}");
                }

                return extractedCount;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取MOD文件失败: {ex.Message}");
                return 0;
            }
        }
        #endregion

        #region
        private string GetUniqueFileName(string originalPath)
        {
            if (!File.Exists(originalPath))
                return originalPath;

            string directory = Path.GetDirectoryName(originalPath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);

            int duplicate = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExtension}_dup{duplicate}{extension}");
                duplicate++;
            } while (File.Exists(newPath));

            return newPath;
        }
        #endregion
    }
}