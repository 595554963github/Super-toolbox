using System.IO.Compression;

namespace super_toolbox
{
    public class XenobladeLBIM_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] XBC1_SIGNATURE = { 0x78, 0x62, 0x63, 0x31 };
        private static readonly byte[] EFB0_SIGNATURE = { 0x65, 0x66, 0x62, 0x30 };
        private static readonly byte[] ZLIB_HEADER = { 0x78, 0x9C };
        private static readonly byte[] LBIM_SIGNATURE = { 0x4C, 0x42, 0x49, 0x4D };

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

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.Contains("_extracted", StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            ExtractionProgress?.Invoke(this, $"找到{TotalFilesToExtract}个源文件");

            List<string> extractedFiles = new List<string>();

            foreach (string filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);

                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");

                try
                {
                    string extension = Path.GetExtension(filePath).ToLower();

                    if (extension == ".wiefb")
                    {
                        await ProcessWiefbFileAsync(filePath, extractedFiles, cancellationToken);
                    }
                    else if (extension == ".wismhd")
                    {
                        await ProcessWismhdFileAsync(filePath, extractedFiles, cancellationToken);
                    }
                    else
                    {
                        await ProcessFileInStagesAsync(filePath, extractedFiles, cancellationToken);
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
                    ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                    OnExtractionFailed($"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                }
            }

            OnExtractionCompleted();
        }

        private async Task ProcessWiefbFileAsync(string filePath, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            ExtractionProgress?.Invoke(this, $"处理wiefb文件:{Path.GetFileName(filePath)}");

            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(filePath, cancellationToken);

                if (fileContent.Length <= 48)
                {
                    ExtractionError?.Invoke(this, $"wiefb文件{Path.GetFileName(filePath)}太小，至少需要49字节");
                    return;
                }

                bool isEfb0Format = false;
                if (fileContent.Length >= 4)
                {
                    isEfb0Format = fileContent[0] == EFB0_SIGNATURE[0] &&
                                   fileContent[1] == EFB0_SIGNATURE[1] &&
                                   fileContent[2] == EFB0_SIGNATURE[2] &&
                                   fileContent[3] == EFB0_SIGNATURE[3];
                }

                if (isEfb0Format)
                {
                    await ProcessEfb0WiefbFileAsync(fileContent, filePath, extractedFiles, cancellationToken);
                }
                else
                {
                    await ProcessXbc1WiefbFileAsync(fileContent, filePath, extractedFiles, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理wiefb文件失败:{ex.Message}");
            }
        }

        private async Task ProcessEfb0WiefbFileAsync(byte[] fileContent, string filePath, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            List<int> lbimPositions = FindSignaturePositionsEx(fileContent, LBIM_SIGNATURE);

            if (lbimPositions.Count == 0)
            {
                ExtractionError?.Invoke(this, $"在文件中未找到LBIM签名");
                return;
            }

            ExtractionProgress?.Invoke(this, $"找到{lbimPositions.Count}个LBIM签名");

            string baseFilename = Path.GetFileNameWithoutExtension(filePath);
            string fileDir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string outputDir = Path.Combine(fileDir, baseFilename);
            Directory.CreateDirectory(outputDir);

            for (int i = 0; i < lbimPositions.Count; i++)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int startPos = lbimPositions[i] + LBIM_SIGNATURE.Length;
                int endPos = (i < lbimPositions.Count - 1) ? lbimPositions[i + 1] + LBIM_SIGNATURE.Length : fileContent.Length;

                if (endPos <= startPos) continue;

                byte[] segmentData = new byte[endPos - startPos];
                Array.Copy(fileContent, startPos, segmentData, 0, segmentData.Length);

                string outputFileName = $"{baseFilename}_{i + 1:000}.lbim";
                string outputFilePath = Path.Combine(outputDir, outputFileName);

                if (File.Exists(outputFilePath))
                {
                    int duplicateCount = 1;
                    do
                    {
                        outputFileName = $"{baseFilename}_{i + 1:000}_dup{duplicateCount}.lbim";
                        outputFilePath = Path.Combine(outputDir, outputFileName);
                        duplicateCount++;
                    } while (File.Exists(outputFilePath));
                }

                await File.WriteAllBytesAsync(outputFilePath, segmentData, cancellationToken);
                extractedFiles.Add(outputFilePath);
                OnFileExtracted(outputFilePath);

                ExtractionProgress?.Invoke(this,
                    $"已提取:{outputFileName} (大小:{segmentData.Length}字节, 位置:0x{startPos:X}-0x{endPos:X})");
            }
        }

        private async Task ProcessXbc1WiefbFileAsync(byte[] fileContent, string filePath, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            byte[] dataWithoutHeader = new byte[fileContent.Length - 48];
            Array.Copy(fileContent, 48, dataWithoutHeader, 0, dataWithoutHeader.Length);

            if (dataWithoutHeader.Length >= 2 &&
                dataWithoutHeader[0] == ZLIB_HEADER[0] &&
                dataWithoutHeader[1] == ZLIB_HEADER[1])
            {
                try
                {
                    byte[] decompressedData;
                    using (MemoryStream compressedStream = new MemoryStream(dataWithoutHeader, 2, dataWithoutHeader.Length - 2))
                    using (MemoryStream decompressedStream = new MemoryStream())
                    {
                        using (DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
                        {
                            await deflateStream.CopyToAsync(decompressedStream, cancellationToken);
                        }
                        decompressedData = decompressedStream.ToArray();
                    }

                    ExtractionProgress?.Invoke(this, $"解压成功，数据大小:{decompressedData.Length}字节");
                    List<int> lbimPositions = FindSignaturePositionsEx(decompressedData, LBIM_SIGNATURE);

                    if (lbimPositions.Count == 0)
                    {
                        ExtractionError?.Invoke(this, $"在解压数据中未找到LBIM签名");
                        return;
                    }

                    ExtractionProgress?.Invoke(this, $"找到{lbimPositions.Count}个LBIM签名");
                    string baseFilename = Path.GetFileNameWithoutExtension(filePath);
                    string fileDir = Path.GetDirectoryName(filePath) ?? string.Empty;
                    string outputDir = Path.Combine(fileDir, baseFilename);
                    Directory.CreateDirectory(outputDir);

                    for (int i = 0; i < lbimPositions.Count; i++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        int startPos;
                        int endPos;

                        if (i == 0)
                        {
                            startPos = lbimPositions[i] + LBIM_SIGNATURE.Length;
                        }
                        else
                        {
                            startPos = lbimPositions[i - 1] + LBIM_SIGNATURE.Length;
                        }

                        if (i < lbimPositions.Count - 1)
                        {
                            endPos = lbimPositions[i + 1] + LBIM_SIGNATURE.Length;
                        }
                        else
                        {
                            endPos = decompressedData.Length;
                        }

                        if (endPos <= startPos) continue;

                        byte[] segmentData = new byte[endPos - startPos];
                        Array.Copy(decompressedData, startPos, segmentData, 0, segmentData.Length);
                        bool hasLbimAtEnd = CheckSegmentEndsWithLbim(segmentData);

                        if (!hasLbimAtEnd)
                        {
                            ExtractionProgress?.Invoke(this, $"警告:第{i + 1}段末尾没有LBIM签名");
                        }

                        string outputFileName = $"{baseFilename}_{i + 1:000}.lbim";
                        string outputFilePath = Path.Combine(outputDir, outputFileName);

                        if (File.Exists(outputFilePath))
                        {
                            int duplicateCount = 1;
                            do
                            {
                                outputFileName = $"{baseFilename}_{i + 1:000}_dup{duplicateCount}.lbim";
                                outputFilePath = Path.Combine(outputDir, outputFileName);
                                duplicateCount++;
                            } while (File.Exists(outputFilePath));
                        }

                        await File.WriteAllBytesAsync(outputFilePath, segmentData, cancellationToken);
                        extractedFiles.Add(outputFilePath);
                        OnFileExtracted(outputFilePath);

                        ExtractionProgress?.Invoke(this,
                            $"已提取:{outputFileName} (大小:{segmentData.Length}字节, 位置:0x{startPos:X}-0x{endPos:X}, 包含LBIM:{(hasLbimAtEnd ? "是" : "否")})");
                    }
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"zlib解压失败:{ex.Message}");
                }
            }
            else
            {
                ExtractionError?.Invoke(this, $"不是有效的zlib压缩数据");
            }
        }

        private async Task ProcessWismhdFileAsync(string filePath, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            ExtractionProgress?.Invoke(this, $"处理wismhd文件:{Path.GetFileName(filePath)}");

            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(filePath, cancellationToken);

                List<int> xbc1Positions = FindSignaturePositions(fileContent, XBC1_SIGNATURE);
                if (xbc1Positions.Count > 0)
                {
                    await ProcessFileInStagesAsync(filePath, extractedFiles, cancellationToken);
                    return;
                }

                List<int> lbimPositions = FindSignaturePositionsEx(fileContent, LBIM_SIGNATURE);
                if (lbimPositions.Count == 0)
                {
                    ExtractionError?.Invoke(this, $"在{Path.GetFileName(filePath)}中未找到LBIM签名");
                    return;
                }

                ExtractionProgress?.Invoke(this, $"找到{lbimPositions.Count}个LBIM签名");
                string baseFilename = Path.GetFileNameWithoutExtension(filePath);
                string fileDir = Path.GetDirectoryName(filePath) ?? string.Empty;
                string outputDir = Path.Combine(fileDir, baseFilename);
                Directory.CreateDirectory(outputDir);

                if (lbimPositions.Count % 2 != 0)
                {
                    ExtractionProgress?.Invoke(this, $"警告:找到奇数个({lbimPositions.Count})LBIM签名");
                }

                int fileCount = 0;
                int currentPos = 0;

                for (int i = 0; i < lbimPositions.Count; i++)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    int lbimPos = lbimPositions[i];
                    int nextLbimPos = (i < lbimPositions.Count - 1) ? lbimPositions[i + 1] : fileContent.Length;
                    int startPos = currentPos;
                    int endPos = lbimPos + 4;

                    if (endPos > fileContent.Length)
                    {
                        endPos = fileContent.Length;
                    }

                    if (endPos <= startPos) continue;

                    byte[] segmentData = new byte[endPos - startPos];
                    Array.Copy(fileContent, startPos, segmentData, 0, segmentData.Length);
                    bool endsWithLbim = CheckSegmentEndsWithLbim(segmentData);

                    if (!endsWithLbim)
                    {
                        ExtractionProgress?.Invoke(this, $"警告:第{fileCount + 1}段文件不以LBIM结尾，可能不是有效的LBIM文件");
                        endPos = lbimPos + 4;

                        if (endPos <= startPos || endPos > fileContent.Length)
                        {
                            ExtractionError?.Invoke(this, $"第{fileCount + 1}段数据无效，跳过");
                            currentPos = endPos;
                            continue;
                        }

                        segmentData = new byte[endPos - startPos];
                        Array.Copy(fileContent, startPos, segmentData, 0, segmentData.Length);
                    }

                    fileCount++;
                    string outputFileName = $"{baseFilename}_{fileCount:000}.lbim";
                    string outputFilePath = Path.Combine(outputDir, outputFileName);

                    if (File.Exists(outputFilePath))
                    {
                        int duplicateCount = 1;
                        do
                        {
                            outputFileName = $"{baseFilename}_{fileCount:000}_dup{duplicateCount}.lbim";
                            outputFilePath = Path.Combine(outputDir, outputFileName);
                            duplicateCount++;
                        } while (File.Exists(outputFilePath));
                    }

                    await File.WriteAllBytesAsync(outputFilePath, segmentData, cancellationToken);
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);

                    ExtractionProgress?.Invoke(this,
                        $"已提取:{outputFileName} (大小:{segmentData.Length}字节, 位置:0x{startPos:X}-0x{endPos:X}, 包含LBIM:{(endsWithLbim ? "是" : "否")})");

                    currentPos = endPos;
                    if (i + 1 < lbimPositions.Count && lbimPositions[i + 1] == currentPos)
                    {
                        ExtractionProgress?.Invoke(this, $"跳过重复的LBIM签名位置:0x{currentPos:X}");
                        i++;
                        currentPos += 4;
                    }
                }

                if (currentPos < fileContent.Length)
                {
                    ExtractionProgress?.Invoke(this, $"警告:有{fileContent.Length - currentPos}字节剩余数据未处理");
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理wismhd文件失败:{ex.Message}");
            }
        }

        private bool CheckSegmentEndsWithLbim(byte[] segmentData)
        {
            if (segmentData.Length < 4) return false;

            int endPos = segmentData.Length - 4;
            return segmentData[endPos] == LBIM_SIGNATURE[0] &&
                   segmentData[endPos + 1] == LBIM_SIGNATURE[1] &&
                   segmentData[endPos + 2] == LBIM_SIGNATURE[2] &&
                   segmentData[endPos + 3] == LBIM_SIGNATURE[3];
        }

        private List<int> FindSignaturePositionsEx(byte[] content, byte[] signature)
        {
            List<int> positions = new List<int>();
            int searchIndex = 0;

            while (searchIndex <= content.Length - signature.Length)
            {
                bool found = true;
                for (int i = 0; i < signature.Length; i++)
                {
                    if (content[searchIndex + i] != signature[i])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    positions.Add(searchIndex);
                    searchIndex += signature.Length;
                }
                else
                {
                    searchIndex++;
                }
            }

            return positions;
        }

        private async Task ProcessFileInStagesAsync(string filePath, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            byte[] fileContent = await File.ReadAllBytesAsync(filePath, cancellationToken);

            List<int> xbc1Positions = FindSignaturePositions(fileContent, XBC1_SIGNATURE);

            if (xbc1Positions.Count == 0)
            {
                List<int> lbimPositions = FindSignaturePositionsEx(fileContent, LBIM_SIGNATURE);
                if (lbimPositions.Count > 0)
                {
                    await ProcessWithoutXbc1SignatureAsync(fileContent, Path.GetFileName(filePath), Path.GetDirectoryName(filePath) ?? string.Empty, extractedFiles, cancellationToken);
                    return;
                }
                ExtractionProgress?.Invoke(this, $"在{Path.GetFileName(filePath)}中未找到xbc1签名或LBIM签名");
                return;
            }

            string baseFilename = Path.GetFileNameWithoutExtension(filePath);
            string fileDir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string outputDir = Path.Combine(fileDir, baseFilename);
            Directory.CreateDirectory(outputDir);

            for (int i = 0; i < xbc1Positions.Count; i++)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int startPos = xbc1Positions[i];
                int endPos = (i < xbc1Positions.Count - 1) ? xbc1Positions[i + 1] : fileContent.Length;

                if (endPos <= startPos) continue;

                byte[] segmentData = new byte[endPos - startPos];
                Array.Copy(fileContent, startPos, segmentData, 0, segmentData.Length);

                string filenamePrefix = $"{baseFilename}_{i + 1:000}";

                bool processed = await ProcessSingleFileStagesAsync(segmentData, filenamePrefix, outputDir, extractedFiles, cancellationToken);

                if (processed)
                {
                    OnFileExtracted($"{filenamePrefix}");
                }
            }
        }

        private async Task ProcessWithoutXbc1SignatureAsync(byte[] fileContent, string originalFilename, string fileDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            List<int> lbimPositions = FindSignaturePositionsEx(fileContent, LBIM_SIGNATURE);
            if (lbimPositions.Count == 0) return;

            ExtractionProgress?.Invoke(this, $"在{originalFilename}中找到{lbimPositions.Count}个LBIM签名");

            string baseFilename = Path.GetFileNameWithoutExtension(originalFilename);
            string outputDir = Path.Combine(fileDir, baseFilename);
            Directory.CreateDirectory(outputDir);

            int fileCount = 0;
            int currentPos = 0;

            for (int i = 0; i < lbimPositions.Count; i++)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int lbimPos = lbimPositions[i];
                int nextLbimPos = (i < lbimPositions.Count - 1) ? lbimPositions[i + 1] : fileContent.Length;

                int startPos = currentPos;
                int endPos = lbimPos + 4;

                if (endPos > fileContent.Length)
                {
                    endPos = fileContent.Length;
                }

                if (endPos <= startPos) continue;

                byte[] segmentData = new byte[endPos - startPos];
                Array.Copy(fileContent, startPos, segmentData, 0, segmentData.Length);

                bool endsWithLbim = CheckSegmentEndsWithLbim(segmentData);
                if (!endsWithLbim)
                {
                    ExtractionProgress?.Invoke(this, $"警告:第{fileCount + 1}段文件不以LBIM结尾");

                    endPos = lbimPos + 4;
                    if (endPos <= startPos || endPos > fileContent.Length)
                    {
                        ExtractionError?.Invoke(this, $"第{fileCount + 1}段数据无效，跳过");
                        currentPos = endPos;
                        continue;
                    }

                    segmentData = new byte[endPos - startPos];
                    Array.Copy(fileContent, startPos, segmentData, 0, segmentData.Length);
                }

                fileCount++;
                string outputFileName = $"{baseFilename}_{fileCount:000}.lbim";
                string outputFilePath = Path.Combine(outputDir, outputFileName);

                if (File.Exists(outputFilePath))
                {
                    int duplicateCount = 1;
                    do
                    {
                        outputFileName = $"{baseFilename}_{fileCount:000}_dup{duplicateCount}.lbim";
                        outputFilePath = Path.Combine(outputDir, outputFileName);
                        duplicateCount++;
                    } while (File.Exists(outputFilePath));
                }

                await File.WriteAllBytesAsync(outputFilePath, segmentData, cancellationToken);
                extractedFiles.Add(outputFilePath);
                OnFileExtracted(outputFilePath);

                ExtractionProgress?.Invoke(this,
                    $"已提取:{outputFileName} (大小:{segmentData.Length}字节, 位置:0x{startPos:X}-0x{endPos:X})");

                currentPos = endPos;

                if (i + 1 < lbimPositions.Count && lbimPositions[i + 1] == currentPos)
                {
                    ExtractionProgress?.Invoke(this, $"跳过重复的LBIM签名位置:0x{currentPos:X}");
                    i++;
                    currentPos += 4;
                }
            }

            if (currentPos < fileContent.Length)
            {
                ExtractionProgress?.Invoke(this, $"警告:有{fileContent.Length - currentPos}字节剩余数据未处理");
            }
        }

        private async Task<bool> ProcessSingleFileStagesAsync(byte[] originalData, string filenamePrefix, string outputDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            try
            {
                string xbc1File = Path.Combine(outputDir, $"{filenamePrefix}.xbc1");
                await File.WriteAllBytesAsync(xbc1File, originalData, cancellationToken);
                ExtractionProgress?.Invoke(this, $"保存xbc1文件:{Path.GetFileName(xbc1File)}");

                if (originalData.Length < 48)
                {
                    ExtractionError?.Invoke(this, $"{filenamePrefix}:数据长度不足48字节,跳过后续处理");
                    return false;
                }

                byte[] dataWithoutHeader = new byte[originalData.Length - 48];
                Array.Copy(originalData, 48, dataWithoutHeader, 0, dataWithoutHeader.Length);

                if (dataWithoutHeader.Length >= 2 && dataWithoutHeader[0] == ZLIB_HEADER[0] && dataWithoutHeader[1] == ZLIB_HEADER[1])
                {
                    try
                    {
                        byte[] decompressedData;
                        using (MemoryStream compressedStream = new MemoryStream(dataWithoutHeader, 2, dataWithoutHeader.Length - 2))
                        using (MemoryStream decompressedStream = new MemoryStream())
                        {
                            using (DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
                            {
                                await deflateStream.CopyToAsync(decompressedStream, cancellationToken);
                            }
                            decompressedData = decompressedStream.ToArray();
                        }

                        bool isLbim = CheckForLbimSignature(decompressedData);
                        string finalExtension = isLbim ? ".lbim" : ".bin";
                        string finalFile = Path.Combine(outputDir, $"{filenamePrefix}{finalExtension}");

                        await File.WriteAllBytesAsync(finalFile, decompressedData, cancellationToken);
                        extractedFiles.Add(finalFile);

                        string formatInfo = isLbim ? "LBIM格式" : "普通二进制格式";
                        ExtractionProgress?.Invoke(this, $"处理完成:{Path.GetFileName(finalFile)} ({formatInfo})");

                        try
                        {
                            File.Delete(xbc1File);
                            ExtractionProgress?.Invoke(this, $"已删除中间文件:{Path.GetFileName(xbc1File)}");
                        }
                        catch (Exception deleteEx)
                        {
                            ExtractionError?.Invoke(this, $"删除中间文件失败:{deleteEx.Message}");
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"{filenamePrefix}:zlib解压失败:{ex.Message}");
                    }
                }
                else
                {
                    ExtractionProgress?.Invoke(this, $"{filenamePrefix}:不是zlib压缩数据,保存原始数据");

                    string finalFile = Path.Combine(outputDir, $"{filenamePrefix}.bin");
                    await File.WriteAllBytesAsync(finalFile, dataWithoutHeader, cancellationToken);
                    extractedFiles.Add(finalFile);
                    try
                    {
                        File.Delete(xbc1File);
                        ExtractionProgress?.Invoke(this, $"已删除中间文件:{Path.GetFileName(xbc1File)}");
                    }
                    catch (Exception deleteEx)
                    {
                        ExtractionError?.Invoke(this, $"删除中间文件失败:{deleteEx.Message}");
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"{filenamePrefix}:处理失败:{ex.Message}");
            }

            return false;
        }

        private bool CheckForLbimSignature(byte[] data)
        {
            if (data.Length < 4) return false;

            for (int i = Math.Max(0, data.Length - 100); i <= data.Length - 4; i++)
            {
                if (data[i] == LBIM_SIGNATURE[0] &&
                    data[i + 1] == LBIM_SIGNATURE[1] &&
                    data[i + 2] == LBIM_SIGNATURE[2] &&
                    data[i + 3] == LBIM_SIGNATURE[3])
                {
                    return true;
                }
            }
            return false;
        }

        private List<int> FindSignaturePositions(byte[] content, byte[] signature)
        {
            List<int> positions = new List<int>();

            for (int i = 0; i <= content.Length - signature.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < signature.Length; j++)
                {
                    if (content[i + j] != signature[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    positions.Add(i);
                }
            }

            return positions.OrderBy(p => p).ToList();
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("提取操作已取消", cancellationToken);
            }
        }
    }
}
