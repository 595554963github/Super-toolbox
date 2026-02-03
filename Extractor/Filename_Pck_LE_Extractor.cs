using System.IO.Compression;
using System.Text;

namespace super_toolbox
{
    public class Filename_Pck_LE_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] ZLIB_HEADER = { 0x5A, 0x4C, 0x49, 0x42 };
        private static readonly byte[] FILENAME_HEADER = { 0x46, 0x69, 0x6C, 0x65, 0x6E, 0x61, 0x6D, 0x65 };
        private static readonly byte[] PACK_MARKER_4 = Encoding.ASCII.GetBytes("Pack    ");//超女神信仰诺瓦露(steam)、约会大作战凛绪轮回(psv)、传颂之物二人的白皇(psv)、传颂之物虚伪的假面(psv)、传颂之物致逝者的摇篮曲(psv)、黑蝶幻境(psv)
        private static readonly byte[] PACK_MARKER_8 = Encoding.ASCII.GetBytes("Pack        ");//超女神信仰诺瓦露(switch),传颂之物二人的白皇(steam)、传颂之物虚伪的假面(steam)、传颂之物致逝者的摇篮曲(steam)
        private static readonly byte[] PACK_MARKER_12 = Encoding.ASCII.GetBytes("Pack            ");//暂未发现
        private static readonly byte[] PACK_MARKER_16 = Encoding.ASCII.GetBytes("Pack                "); //约会大作战凛绪轮回(steam)、白色相簿_编缀的冬日回忆(steam)、黑蝶幻境(steam)

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            if (data == null || pattern == null || startIndex < 0 || startIndex > data.Length - pattern.Length)
                return -1;

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
                if (found) return i;
            }
            return -1;
        }

        private static bool IsFilenamePck(byte[] data)
        {
            if (data.Length < FILENAME_HEADER.Length) return false;
            for (int i = 0; i < FILENAME_HEADER.Length; i++)
            {
                if (data[i] != FILENAME_HEADER[i])
                    return false;
            }
            return true;
        }

        private static bool IsZlibPck(byte[] data)
        {
            if (data.Length < ZLIB_HEADER.Length) return false;
            for (int i = 0; i < ZLIB_HEADER.Length; i++)
            {
                if (data[i] != ZLIB_HEADER[i])
                    return false;
            }
            return true;
        }

        private static (int pos, int skip) FindPackMarker(byte[] data, int startIndex)
        {
            int pack16Pos = IndexOf(data, PACK_MARKER_16, startIndex);
            if (pack16Pos != -1)
            {
                return (pack16Pos, 20 + 8);
            }

            int pack12Pos = IndexOf(data, PACK_MARKER_12, startIndex);
            if (pack12Pos != -1)
            {
                return (pack12Pos, 16 + 8);
            }

            int pack8Pos = IndexOf(data, PACK_MARKER_8, startIndex);
            if (pack8Pos != -1)
            {
                return (pack8Pos, 12 + 8);
            }

            int pack4Pos = IndexOf(data, PACK_MARKER_4, startIndex);
            if (pack4Pos != -1)
            {
                return (pack4Pos, 8 + 8);
            }

            return (-1, 0);
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var pckFiles = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .ToList();
            TotalFilesToExtract = 0;

            int processedPckCount = 0;
            int totalPckFiles = pckFiles.Count;

            foreach (var pckFilePath in pckFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                processedPckCount++;
                string pckFileName = Path.GetFileName(pckFilePath);
                ExtractionProgress?.Invoke(this, $"正在处理:{pckFileName} ({processedPckCount}/{totalPckFiles})");

                try
                {
                    byte[] pckHeaderData = Array.Empty<byte>();
                    bool isZlibPck = false;
                    long fileLength = new FileInfo(pckFilePath).Length;
                    ExtractionProgress?.Invoke(this, $"{pckFileName}:文件总大小{fileLength / 1024.0 / 1024.0:F2}MB");

                    using (FileStream fileStream = new FileStream(pckFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
                    {
                        byte[] headerBuffer = new byte[ZLIB_HEADER.Length];
                        int readBytes = await fileStream.ReadAsync(headerBuffer, 0, headerBuffer.Length, cancellationToken);
                        if (readBytes == headerBuffer.Length)
                        {
                            isZlibPck = IsZlibPck(headerBuffer);
                        }

                        if (isZlibPck)
                        {
                            ExtractionProgress?.Invoke(this, $"{pckFileName}:检测到zlib压缩格式");
                            fileStream.Position = 14;

                            using (var decompressedStream = new MemoryStream())
                            {
                                byte[] buffer = new byte[65536];
                                int bytesRead;
                                using (var zlibStream = new DeflateStream(fileStream, CompressionMode.Decompress, true))
                                {
                                    while ((bytesRead = await zlibStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                                    {
                                        await decompressedStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                    }
                                }
                                pckHeaderData = decompressedStream.ToArray();
                                ExtractionProgress?.Invoke(this, $"{pckFileName}:zlib解压完成,大小:{pckHeaderData.Length / 1024.0 / 1024.0:F2}MB");
                            }
                        }
                        else
                        {
                            byte[] filenameHeaderBuffer = new byte[FILENAME_HEADER.Length];
                            fileStream.Position = 0;
                            readBytes = await fileStream.ReadAsync(filenameHeaderBuffer, 0, filenameHeaderBuffer.Length, cancellationToken);
                            bool isFilenamePck = readBytes == filenameHeaderBuffer.Length && IsFilenamePck(filenameHeaderBuffer);

                            long readSize = Math.Min(10 * 1024 * 1024, fileLength);
                            pckHeaderData = new byte[readSize];
                            fileStream.Position = 0;
                            await fileStream.ReadAsync(pckHeaderData, 0, (int)readSize, cancellationToken);
                        }
                    }

                    int packMarkerPos = -1;
                    int packMarkerTotalSkip = 0;

                    if (IsFilenamePck(pckHeaderData))
                    {
                        ExtractionProgress?.Invoke(this, $"{pckFileName}:检测到未压缩文件头");
                        (packMarkerPos, packMarkerTotalSkip) = FindPackMarker(pckHeaderData, 8);
                    }
                    else
                    {
                        (packMarkerPos, packMarkerTotalSkip) = FindPackMarker(pckHeaderData, 0);
                    }

                    if (packMarkerPos == -1)
                    {
                        ExtractionProgress?.Invoke(this, $"{pckFileName}:未找到Pack标记,跳过");
                        continue;
                    }

                    long entryTableStart = packMarkerPos + packMarkerTotalSkip;
                    ExtractionProgress?.Invoke(this, $"{pckFileName}:索引区起始偏移0x{entryTableStart:X}");

                    List<(long offset, long size)> fileEntries = new List<(long offset, long size)>();
                    using (var fs = new FileStream(pckFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.RandomAccess))
                    {
                        if (isZlibPck)
                        {
                            using (var ms = new MemoryStream(pckHeaderData))
                            {
                                fileEntries = ParseFileEntriesUniversalFixed(ms, entryTableStart, pckHeaderData.LongLength, pckFileName, cancellationToken);
                            }
                        }
                        else
                        {
                            fs.Position = entryTableStart;
                            fileEntries = ParseFileEntriesUniversalFixed(fs, entryTableStart, fileLength, pckFileName, cancellationToken);
                        }
                    }

                    if (fileEntries.Count == 0)
                    {
                        ExtractionProgress?.Invoke(this, $"{pckFileName}:未解析到有效文件条目");
                        continue;
                    }

                    ExtractionProgress?.Invoke(this, $"{pckFileName}:共解析到{fileEntries.Count}个有效文件");

                    string pckDir = Path.GetDirectoryName(pckFilePath) ?? directoryPath;
                    string pckNameWithoutExt = Path.GetFileNameWithoutExtension(pckFilePath);
                    string pckOutputDir = Path.Combine(pckDir, pckNameWithoutExt);
                    Directory.CreateDirectory(pckOutputDir);

                    List<string> fileNames = new List<string>();
                    if (IsFilenamePck(pckHeaderData))
                    {
                        int regionSize = packMarkerPos - 8;
                        fileNames = ExtractFileNamesFromBottom(pckHeaderData, 8, regionSize, fileEntries.Count);
                        if (fileNames.Count > 0)
                        {
                            ExtractionProgress?.Invoke(this, $"{pckFileName}:找到{fileNames.Count}个原始文件名");
                        }
                    }

                    await ExtractFileEntriesUniversalFixedAsync(pckFilePath, isZlibPck, pckHeaderData, fileEntries, fileNames, pckOutputDir, pckFileName, pckNameWithoutExt, extractedFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    string errorMsg = $"处理{pckFileName}出错:{ex.Message}\n{ex.StackTrace}";
                    ExtractionError?.Invoke(this, errorMsg);
                    OnExtractionFailed(errorMsg);
                }
            }

            TotalFilesToExtract = extractedFiles.Count;
            ExtractionProgress?.Invoke(this,
                extractedFiles.Count > 0
                    ? $"处理完成,共提取{extractedFiles.Count}个文件"
                    : "处理完成,未提取到任何文件");
            OnExtractionCompleted();
        }

        private List<(long offset, long size)> ParseFileEntriesUniversalFixed(Stream stream, long startOffset, long fileLength, string fileName, CancellationToken cancellationToken)
        {
            List<(long offset, long size)> entries = new List<(long offset, long size)>();
            stream.Position = startOffset;
            int entryIndex = 0;

            ExtractionProgress?.Invoke(this, $"{fileName}:开始解析索引区,起始位置:0x{startOffset:X}");

            try
            {
                long firstFileOffset = -1;
                byte[] firstOffsetBuffer = new byte[4];

                stream.Position = startOffset;
                int read = stream.Read(firstOffsetBuffer, 0, 4);
                if (read == 4)
                {
                    firstFileOffset = BitConverter.ToUInt32(firstOffsetBuffer, 0);
                    ExtractionProgress?.Invoke(this, $"{fileName}:第一个文件偏移(32位):0x{firstFileOffset:X}");
                }

                stream.Position = startOffset;

                if (firstFileOffset > 0 && firstFileOffset < fileLength)
                {
                    ExtractionProgress?.Invoke(this, $"{fileName}:使用第一个文件偏移作为索引区结束:0x{firstFileOffset:X}");

                    while (stream.Position < firstFileOffset)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        if (stream.Position + 8 > firstFileOffset)
                        {
                            break;
                        }

                        byte[] entryBuffer = new byte[8];
                        read = stream.Read(entryBuffer, 0, 8);
                        if (read != 8) break;

                        uint offset = BitConverter.ToUInt32(entryBuffer, 0);
                        uint size = BitConverter.ToUInt32(entryBuffer, 4);

                        if (offset == 0 && size == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"{fileName}:检测到(0,0)结束标记");
                            break;
                        }

                        if (offset >= fileLength || size == 0 || offset + size > fileLength)
                        {
                            ExtractionProgress?.Invoke(this, $"{fileName}:条目{entryIndex}无效,可能是64位格式或已到达数据区");

                            stream.Position -= 8;
                            break;
                        }

                        entries.Add((offset, size));
                        entryIndex++;

                        if (entryIndex % 500 == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"{fileName}:已解析{entryIndex}个32位条目,位置:0x{stream.Position:X}");
                        }
                    }
                }

                if (entries.Count == 0)
                {
                    ExtractionProgress?.Invoke(this, $"{fileName}:32位解析失败,尝试智能解析");

                    stream.Position = startOffset;
                    entryIndex = 0;

                    long lastValidOffset = 0;
                    int consecutiveInvalidCount = 0;
                    const int MAX_INVALID_CONSECUTIVE = 10;

                    while (stream.Position < fileLength && consecutiveInvalidCount < MAX_INVALID_CONSECUTIVE)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        if (stream.Position + 8 > fileLength)
                            break;

                        byte[] entryBuffer = new byte[8];
                        read = stream.Read(entryBuffer, 0, 8);
                        if (read != 8) break;

                        uint offset32 = BitConverter.ToUInt32(entryBuffer, 0);
                        uint size32 = BitConverter.ToUInt32(entryBuffer, 4);

                        bool isValid32 = (offset32 > 0 && offset32 < fileLength &&
                                         size32 > 0 && size32 < fileLength * 0.5 &&
                                         offset32 > stream.Position);

                        if (isValid32 && offset32 > lastValidOffset)
                        {
                            entries.Add((offset32, size32));
                            lastValidOffset = offset32;
                            entryIndex++;
                            consecutiveInvalidCount = 0;

                            if (entryIndex % 500 == 0)
                            {
                                ExtractionProgress?.Invoke(this, $"{fileName}:智能解析条目{entryIndex},偏移:0x{offset32:X}");
                            }
                        }
                        else
                        {
                            consecutiveInvalidCount++;

                            if (consecutiveInvalidCount == 1)
                            {
                                stream.Position -= 8;
                                if (stream.Position + 16 <= fileLength)
                                {
                                    byte[] buffer16 = new byte[16];
                                    read = stream.Read(buffer16, 0, 16);
                                    if (read == 16)
                                    {
                                        ulong offset64 = BitConverter.ToUInt64(buffer16, 0);
                                        ulong size64 = BitConverter.ToUInt64(buffer16, 8);

                                        bool isValid64 = (offset64 > 0 && offset64 < (ulong)fileLength &&
                                                         size64 > 0 && size64 < (ulong)fileLength * 0.5 &&
                                                         offset64 > (ulong)stream.Position - 16);

                                        if (isValid64 && (long)offset64 > lastValidOffset)
                                        {
                                            entries.Add(((long)offset64, (long)size64));
                                            lastValidOffset = (long)offset64;
                                            entryIndex++;
                                            consecutiveInvalidCount = 0;
                                            ExtractionProgress?.Invoke(this, $"{fileName}:发现64位条目,偏移:0x{offset64:X}");
                                        }
                                        else
                                        {
                                            stream.Position -= 16;
                                        }
                                    }
                                }
                            }

                            if (consecutiveInvalidCount >= 3)
                            {
                                ExtractionProgress?.Invoke(this, $"{fileName}:连续{consecutiveInvalidCount}个无效条目,停止解析");
                                break;
                            }
                        }
                    }
                }

                if (entries.Count > 0)
                {
                    entries = entries.OrderBy(e => e.offset).ToList();

                    entries = entries.Where(e =>
                        e.offset > 0 && e.size > 0 &&
                        e.offset < fileLength &&
                        e.offset + e.size <= fileLength).ToList();

                    for (int i = 0; i < entries.Count - 1; i++)
                    {
                        var current = entries[i];
                        var next = entries[i + 1];

                        if (current.offset + current.size > next.offset)
                        {
                            ExtractionProgress?.Invoke(this, $"{fileName}:警告！文件{i}和{i + 1}可能重叠");
                        }

                        if (current.size > fileLength * 0.5)
                        {
                            ExtractionError?.Invoke(this, $"{fileName}:文件{i}大小异常: {current.size}字节");
                        }
                    }

                    int lastFilesToCheck = Math.Min(10, entries.Count);
                    for (int i = entries.Count - lastFilesToCheck; i < entries.Count; i++)
                    {
                        if (entries[i].size > 100 * 1024 * 1024)
                        {
                            ExtractionError?.Invoke(this, $"{fileName}:文件{i}可能解析错误,大小: {entries[i].size / 1024.0 / 1024.0:F2}MB");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"{fileName}:解析索引区失败: {ex.Message}");
            }

            ExtractionProgress?.Invoke(this, $"{fileName}:解析完成,共找到{entries.Count}个有效文件条目");

            int showCount = Math.Min(10, entries.Count);
            for (int i = 0; i < showCount; i++)
            {
                ExtractionProgress?.Invoke(this, $"{fileName}:条目{i}: 偏移=0x{entries[i].offset:X8}, 大小={entries[i].size}");
            }

            if (entries.Count > 10)
            {
                for (int i = entries.Count - showCount; i < entries.Count; i++)
                {
                    ExtractionProgress?.Invoke(this, $"{fileName}:条目{i}: 偏移=0x{entries[i].offset:X8}, 大小={entries[i].size}");
                }
            }

            return entries;
        }

        private async Task ExtractFileEntriesUniversalFixedAsync(string pckFilePath, bool isZlibPck, byte[] decompressedData, List<(long offset, long size)> entries, List<string> fileNames,
                                              string outputDir, string fileName, string baseName,
                                              List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int extractedCount = 0;
            int failedCount = 0;

            if (isZlibPck)
            {
                ExtractionProgress?.Invoke(this, $"{fileName}:开始提取Zlib压缩包内文件,共{entries.Count}个");
                for (int i = 0; i < entries.Count; i++)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    var (fileOffset, fileSize) = entries[i];

                    try
                    {
                        string outputFilePath = GetOutputFilePath(i, fileNames, baseName, outputDir);
                        outputFilePath = GetUniqueFilePath(outputFilePath);

                        string directory = Path.GetDirectoryName(outputFilePath) ?? outputDir;
                        Directory.CreateDirectory(directory);

                        if (fileOffset < 0 || fileSize <= 0 || fileOffset + fileSize > (long)decompressedData.LongLength)
                        {
                            ExtractionError?.Invoke(this, $"{fileName}: 文件{i + 1}偏移0x{fileOffset:X16}或大小{fileSize}超出解压后数据范围({decompressedData.LongLength}字节),跳过");
                            failedCount++;
                            continue;
                        }

                        using (var outputStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                        {
                            long bytesToRead = fileSize;
                            long currentOffset = fileOffset;
                            const int BUFFER_SIZE = 65536;

                            while (bytesToRead > 0)
                            {
                                int bytesToReadNow = (int)Math.Min(BUFFER_SIZE, bytesToRead);

                                if (currentOffset + bytesToReadNow > decompressedData.LongLength)
                                {
                                    bytesToReadNow = (int)Math.Min(bytesToReadNow, decompressedData.LongLength - currentOffset);
                                }

                                if (bytesToReadNow <= 0)
                                {
                                    ExtractionError?.Invoke(this, $"{fileName}: 文件{i + 1}读取超出边界,终止读取");
                                    break;
                                }

                                await outputStream.WriteAsync(decompressedData, (int)currentOffset, bytesToReadNow, cancellationToken);

                                currentOffset += bytesToReadNow;
                                bytesToRead -= bytesToReadNow;

                                if (currentOffset % (100 * 1024 * 1024) < bytesToReadNow)
                                {
                                    long bytesReadTotal = currentOffset - fileOffset;
                                    ExtractionProgress?.Invoke(this,
                                        $"{fileName}:文件{i + 1}提取中... {bytesReadTotal / 1024.0 / 1024.0:F2}MB/{fileSize / 1024.0 / 1024.0:F2}MB " +
                                        $"({(bytesReadTotal * 100.0 / fileSize):F1}%)");
                                }
                            }

                            outputStream.Flush();
                            long actualSize = outputStream.Length;
                            if (actualSize != fileSize)
                            {
                                ExtractionError?.Invoke(this,
                                    $"{fileName}: 文件{i + 1}大小不匹配,预期{fileSize}字节,实际{actualSize}字节");
                                failedCount++;
                                File.Delete(outputFilePath);
                                continue;
                            }
                        }

                        extractedCount++;
                        extractedFiles.Add(outputFilePath);
                        OnFileExtracted(outputFilePath);

                        string nameInfo = (i < fileNames.Count && !string.IsNullOrEmpty(fileNames[i]))
                            ? $"(原文件名:{fileNames[i]})"
                            : "";
                        ExtractionProgress?.Invoke(this,
                            $"{fileName}: 已提取文件{i + 1}/{entries.Count} | 偏移:0x{fileOffset:X16} | " +
                            $"大小:{fileSize / 1024.0 / 1024.0:F2}MB | 保存为:{Path.GetFileName(outputFilePath)} {nameInfo}");
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"{fileName}: 提取文件{i + 1}失败: {ex.Message}\n{ex.StackTrace}");
                        failedCount++;
                    }
                }
            }
            else
            {
                ExtractionProgress?.Invoke(this, $"{fileName}:开始提取普通包内文件,共{entries.Count}个");
                using (var fs = new FileStream(pckFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.RandomAccess))
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        var (fileOffset, fileSize) = entries[i];

                        try
                        {
                            string outputFilePath = GetOutputFilePath(i, fileNames, baseName, outputDir);
                            outputFilePath = GetUniqueFilePath(outputFilePath);

                            string directory = Path.GetDirectoryName(outputFilePath) ?? outputDir;
                            Directory.CreateDirectory(directory);

                            if (fileOffset < 0 || fileSize <= 0 || fileOffset > fs.Length)
                            {
                                ExtractionError?.Invoke(this,
                                    $"{fileName}: 文件{i + 1}偏移0x{fileOffset:X16}或大小{fileSize}无效(文件总大小{fs.Length}字节),跳过");
                                failedCount++;
                                continue;
                            }

                            const int BUFFER_SIZE = 256 * 1024;
                            fs.Position = fileOffset;

                            using (var outputStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BUFFER_SIZE, true))
                            {
                                long bytesToRead = fileSize;
                                long bytesReadTotal = 0;
                                byte[] buffer = new byte[BUFFER_SIZE];

                                while (bytesToRead > 0 && fs.Position < fs.Length)
                                {
                                    int bytesToReadNow = (int)Math.Min(BUFFER_SIZE, bytesToRead);
                                    int bytesRead = await fs.ReadAsync(buffer, 0, bytesToReadNow, cancellationToken);

                                    if (bytesRead == 0) break;

                                    await outputStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                    bytesReadTotal += bytesRead;
                                    bytesToRead -= bytesRead;

                                    if (bytesReadTotal % (100 * 1024 * 1024) < bytesRead)
                                    {
                                        ExtractionProgress?.Invoke(this,
                                            $"{fileName}:文件{i + 1}提取中... {bytesReadTotal / 1024.0 / 1024.0:F2}MB/{fileSize / 1024.0 / 1024.0:F2}MB " +
                                            $"({(bytesReadTotal * 100.0 / fileSize):F1}%)");
                                    }
                                }

                                if (bytesReadTotal != fileSize)
                                {
                                    ExtractionError?.Invoke(this,
                                        $"{fileName}: 文件{i + 1}读取不完整,预期{fileSize}字节,实际{bytesReadTotal}字节");
                                    failedCount++;
                                    File.Delete(outputFilePath);
                                    continue;
                                }
                            }

                            extractedCount++;
                            extractedFiles.Add(outputFilePath);
                            OnFileExtracted(outputFilePath);

                            string nameInfo = (i < fileNames.Count && !string.IsNullOrEmpty(fileNames[i]))
                                ? $"(原文件名:{fileNames[i]})"
                                : "";
                            ExtractionProgress?.Invoke(this,
                                $"{fileName}: 已提取文件{i + 1}/{entries.Count} | 偏移:0x{fileOffset:X16} | " +
                                $"大小:{fileSize / 1024.0 / 1024.0:F2}MB | 保存为:{Path.GetFileName(outputFilePath)} {nameInfo}");
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"{fileName}: 提取文件{i + 1}失败: {ex.Message}\n{ex.StackTrace}");
                            failedCount++;
                        }
                    }
                }
            }

            ExtractionProgress?.Invoke(this,
                $"{fileName}:提取完成 | 成功:{extractedCount} | 失败:{failedCount} | 总计:{entries.Count}");

            if (fileNames.Count > 0)
            {
                ExtractionProgress?.Invoke(this,
                    $"{fileName}:成功还原了{Math.Min(fileNames.Count, entries.Count)}个文件的原始文件名");
            }
        }

        private string GetOutputFilePath(int index, List<string> fileNames, string baseName, string outputDir)
        {
            if (index < fileNames.Count && !string.IsNullOrEmpty(fileNames[index]))
            {
                string originalPath = fileNames[index];

                if (originalPath.Contains('/') || originalPath.Contains('\\'))
                {
                    string sanitizedPath = SanitizePath(originalPath);
                    return Path.Combine(outputDir, sanitizedPath);
                }
                else
                {
                    string cleanName = SanitizeFileName(originalPath);
                    if (!string.IsNullOrEmpty(cleanName))
                    {
                        return Path.Combine(outputDir, cleanName);
                    }
                }
            }
            return Path.Combine(outputDir, $"file_{index + 1:0000}");
        }

        private List<string> ExtractFileNamesFromBottom(byte[] content, int startOffset, int regionSize, int expectedFileCount)
        {
            List<string> fileNames = new List<string>();

            if (regionSize <= 0 || expectedFileCount <= 0)
                return fileNames;

            int endOffset = startOffset + regionSize;
            List<string> allFileNames = new List<string>();

            int pos = startOffset;
            while (pos < endOffset)
            {
                string fileName = ReadNullTerminatedAscii(content, pos, endOffset);
                if (!string.IsNullOrEmpty(fileName))
                {
                    allFileNames.Add(fileName);
                    pos += fileName.Length + 1;
                }
                else
                {
                    pos++;
                }
            }

            if (allFileNames.Count >= expectedFileCount)
            {
                int takeFromIndex = Math.Max(0, allFileNames.Count - expectedFileCount);
                fileNames = allFileNames.Skip(takeFromIndex).Take(expectedFileCount).ToList();
            }
            else if (allFileNames.Count > 0)
            {
                fileNames = allFileNames;
            }

            return fileNames;
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "unnamed_file";

            char[] invalidChars = Path.GetInvalidFileNameChars();

            List<char> allInvalidChars = new List<char>(invalidChars);
            if (!allInvalidChars.Contains('\''))
            {
                allInvalidChars.Add('\'');
            }

            StringBuilder sb = new StringBuilder();
            foreach (char c in fileName)
            {
                if (!allInvalidChars.Contains(c))
                {
                    sb.Append(c);
                }
            }

            string result = sb.ToString();

            if (string.IsNullOrWhiteSpace(result))
            {
                result = "unnamed_file";
            }

            if (result.Length > 200)
            {
                string extension = Path.GetExtension(result);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(result);

                if (nameWithoutExt.Length > 200 - extension.Length)
                {
                    nameWithoutExt = nameWithoutExt.Substring(0, 200 - extension.Length);
                }
                result = nameWithoutExt + extension;
            }

            return result;
        }

        private string SanitizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            path = path.Replace('\\', '/');
            string[] parts = path.Split('/');

            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = SanitizeFileName(parts[i]);
                if (string.IsNullOrEmpty(parts[i]))
                {
                    parts[i] = $"part{i}";
                }
            }

            return Path.Combine(parts);
        }

        private string ReadNullTerminatedAscii(byte[] content, int start, int maxPos)
        {
            StringBuilder sb = new StringBuilder();
            int pos = start;

            while (pos < maxPos)
            {
                byte b = content[pos];
                if (b == 0x00)
                {
                    break;
                }
                else if (b >= 0x20 && b <= 0x7E)
                {
                    sb.Append((char)b);
                    pos++;
                }
                else
                {
                    return sb.ToString();
                }
            }

            return sb.ToString();
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return filePath;
            }

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string fileExtension = Path.GetExtension(filePath);
            int duplicateCount = 1;
            string newFilePath;

            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_dup{duplicateCount}{fileExtension}");
                duplicateCount++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("提取操作已取消", cancellationToken);
            }
        }

        protected new virtual void OnExtractionFailed(string message) => ExtractionError?.Invoke(this, message);
        protected new virtual void OnExtractionCompleted() => ExtractionProgress?.Invoke(this, "所有文件提取完成");
        protected new virtual void OnFileExtracted(string filePath)
        {
            base.OnFileExtracted(filePath);
        }
    }
}
