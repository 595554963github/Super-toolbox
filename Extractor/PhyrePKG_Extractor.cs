using System.Text;

namespace super_toolbox
{
    public class PhyrePKG_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }
            string extractedRootDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedRootDir);
            var pkgFiles = Directory.GetFiles(directoryPath, "*.pkg", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedRootDir, StringComparison.OrdinalIgnoreCase))
                .ToList();
            TotalFilesToExtract = pkgFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            ExtractionProgress?.Invoke(this, $"发现{TotalFilesToExtract}个PKG文件");
            int processedCount = 0;
            int successCount = 0;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var pkgFilePath in pkgFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            processedCount++;

                            string fileName = Path.GetFileName(pkgFilePath);
                            ExtractionProgress?.Invoke(this, $"正在处理({processedCount}/{TotalFilesToExtract}): {fileName}");

                            if (ProcessPkgFile(pkgFilePath, extractedRootDir, cancellationToken))
                            {
                                successCount++;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{Path.GetFileName(pkgFilePath)}时出错:{ex.Message}");
                            OnExtractionFailed($"处理{Path.GetFileName(pkgFilePath)}时出错:{ex.Message}");
                        }
                    }
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
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
                throw;
            }
            ExtractionProgress?.Invoke(this, $"完成!成功提取{successCount}个PKG文件");
            OnExtractionCompleted();
        }
        private bool ProcessPkgFile(string pkgFilePath, string outputRootDir, CancellationToken cancellationToken)
        {
            try
            {
                string pkgFileName = Path.GetFileNameWithoutExtension(pkgFilePath);
                string pkgExtractDir = Path.Combine(outputRootDir, pkgFileName);
                Directory.CreateDirectory(pkgExtractDir);

                string? directoryName = Path.GetDirectoryName(pkgFilePath);
                string commonPkgPath = !string.IsNullOrEmpty(directoryName) ?
                    Path.Combine(directoryName, "common.pkg") :
                    string.Empty;
                string commonPkgPathToUse = File.Exists(commonPkgPath) ? commonPkgPath : string.Empty;
                int extractedFileCount = UnpackPKG(pkgFilePath, pkgExtractDir, commonPkgPathToUse, cancellationToken);
                ExtractionProgress?.Invoke(this, $"从{pkgFileName}.pkg中提取出{extractedFileCount}个文件");
                return extractedFileCount > 0;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理PKG文件{Path.GetFileName(pkgFilePath)}时出错:{ex.Message}");
                return false;
            }
        }
        private int UnpackPKG(string pkgPath, string outputDir, string commonPkgPath, CancellationToken cancellationToken)
        {
            int extractedCount = 0;
            using (var fs = new FileStream(pkgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                fs.Seek(4, SeekOrigin.Current);
                uint totalFileEntries = br.ReadUInt32();
                var packageFileEntries = new Dictionary<string, FileEntry>();

                for (int i = 0; i < totalFileEntries; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] nameBytes = br.ReadBytes(64);
                    string fileName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                    uint uncompressedSize = br.ReadUInt32();
                    uint compressedSize = br.ReadUInt32();
                    uint offset = br.ReadUInt32();
                    uint flags = br.ReadUInt32();
                    packageFileEntries[fileName] = new FileEntry
                    {
                        Offset = offset,
                        CompressedSize = compressedSize,
                        UncompressedSize = uncompressedSize,
                        Flags = flags
                    };
                }
                var commonPkgFileEntries = packageFileEntries
                    .Where(kvp => (kvp.Value.Flags & 1U) != 0 && (kvp.Value.Flags & 8U) != 0 && kvp.Value.Offset == 0 && kvp.Value.CompressedSize == 0)
                    .Select(kvp => kvp.Key)
                    .ToList();
                if (commonPkgFileEntries.Count > 0 && !string.IsNullOrEmpty(commonPkgPath))
                {
                    extractedCount += UnpackPKGWithFilter(commonPkgPath, outputDir, cancellationToken, packageFileEntries, commonPkgFileEntries);
                }
                foreach (var kvp in packageFileEntries.OrderBy(x => x.Key))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string fileName = kvp.Key;
                    var entry = kvp.Value;
                    if (commonPkgFileEntries.Contains(fileName) && !string.IsNullOrEmpty(commonPkgPath))
                        continue;
                    if ((entry.Flags & 1U) != 0 && (entry.Flags & 8U) != 0 && entry.Offset == 0 && entry.CompressedSize == 0)
                    {
                        if (string.IsNullOrEmpty(commonPkgPath))
                        {
                            ExtractionError?.Invoke(this, $"文件{fileName}需要common.pkg,但是未找到");
                        }
                        continue;
                    }
                    fs.Seek(entry.Offset, SeekOrigin.Begin);
                    byte[]? outputData = null;

                    if ((entry.Flags & 2U) != 0)
                    {
                        fs.Seek(4, SeekOrigin.Current);
                    }

                    if ((entry.Flags & 4U) != 0)
                    {
                        outputData = UncompressLZ4(fs, entry.UncompressedSize, entry.CompressedSize);
                    }
                    else if ((entry.Flags & 8U) != 0 || (entry.Flags & 16U) != 0)
                    {
                        outputData = UncompressLZ4(fs, entry.UncompressedSize, entry.CompressedSize);
                    }
                    else if ((entry.Flags & 1U) != 0)
                    {
                        bool isLZ4 = true;
                        if (entry.CompressedSize >= 8)
                        {
                            long pos = fs.Position;
                            fs.Seek(4, SeekOrigin.Current);
                            uint cms = br.ReadUInt32();
                            fs.Seek(pos, SeekOrigin.Begin);

                            isLZ4 = (cms != entry.CompressedSize) &&
                                   ((entry.CompressedSize - cms) != 4) &&
                                   !(entry.UncompressedSize == 451019 && entry.CompressedSize == 176128 && cms == 176796);
                        }
                        if (isLZ4)
                        {
                            outputData = UncompressLZ4(fs, entry.UncompressedSize, entry.CompressedSize);
                        }
                        else
                        {
                            outputData = UncompressNISLZSS(fs, entry.UncompressedSize, entry.CompressedSize);
                        }
                    }
                    else
                    {
                        outputData = br.ReadBytes((int)entry.UncompressedSize);
                    }
                    if (outputData != null)
                    {
                        string outputPath = Path.Combine(outputDir, fileName);
                        string? outputDirectory = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                        }
                        File.WriteAllBytes(outputPath, outputData);
                        OnFileExtracted(outputPath);
                        extractedCount++;
                    }
                }
            }
            return extractedCount;
        }
        private int UnpackPKGWithFilter(string pkgPath, string outputDir, CancellationToken cancellationToken,
                             Dictionary<string, FileEntry> originalEntries, List<string> filesToExtract)
        {
            int extractedCount = 0;
            using (var fs = new FileStream(pkgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                fs.Seek(4, SeekOrigin.Current);
                uint totalFileEntries = br.ReadUInt32();
                var packageFileEntries = new Dictionary<string, FileEntry>();
                for (int i = 0; i < totalFileEntries; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] nameBytes = br.ReadBytes(64);
                    string fileName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                    uint uncompressedSize = br.ReadUInt32();
                    uint compressedSize = br.ReadUInt32();
                    uint offset = br.ReadUInt32();
                    uint flags = br.ReadUInt32();
                    if (filesToExtract.Contains(fileName))
                    {
                        packageFileEntries[fileName] = new FileEntry
                        {
                            Offset = offset,
                            CompressedSize = compressedSize,
                            UncompressedSize = uncompressedSize,
                            Flags = flags
                        };
                    }
                }
                foreach (var kvp in packageFileEntries.OrderBy(x => x.Key))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string fileName = kvp.Key;
                    var entry = kvp.Value;
                    fs.Seek(entry.Offset, SeekOrigin.Begin);
                    byte[]? outputData = null;

                    if ((entry.Flags & 2U) != 0)
                    {
                        fs.Seek(4, SeekOrigin.Current);
                    }
                    if ((entry.Flags & 4U) != 0)
                    {
                        outputData = UncompressLZ4(fs, entry.UncompressedSize, entry.CompressedSize);
                    }
                    else if ((entry.Flags & 1U) != 0)
                    {
                        bool isLZ4 = true;
                        if (entry.CompressedSize >= 8)
                        {
                            long pos = fs.Position;
                            fs.Seek(4, SeekOrigin.Current);
                            uint cms = br.ReadUInt32();
                            fs.Seek(pos, SeekOrigin.Begin);

                            isLZ4 = (cms != entry.CompressedSize) &&
                                   ((entry.CompressedSize - cms) != 4) &&
                                   !(entry.UncompressedSize == 451019 && entry.CompressedSize == 176128 && cms == 176796);
                        }
                        if (isLZ4)
                        {
                            outputData = UncompressLZ4(fs, entry.UncompressedSize, entry.CompressedSize);
                        }
                        else
                        {
                            outputData = UncompressNISLZSS(fs, entry.UncompressedSize, entry.CompressedSize);
                        }
                    }
                    else
                    {
                        outputData = br.ReadBytes((int)entry.UncompressedSize);
                    }

                    if (outputData != null)
                    {
                        string outputPath = Path.Combine(outputDir, fileName);
                        string? outputDirectory = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                        }

                        File.WriteAllBytes(outputPath, outputData);
                        OnFileExtracted(outputPath);
                        extractedCount++;
                    }
                }
            }
            return extractedCount;
        }
        private byte[] UncompressNISLZSS(Stream src, uint decompressedSize, uint compressedSize)
        {
            uint des = ReadUInt32LittleEndian(src);
            if (des != decompressedSize)
            {
                des = des > decompressedSize ? des : decompressedSize;
            }
            uint cms = ReadUInt32LittleEndian(src);
            if ((cms != compressedSize) && ((compressedSize - cms) != 4) && !(decompressedSize == 451019 && compressedSize == 176128 && cms == 176796))
            {
                throw new Exception("标头和流中的压缩大小不匹配");
            }
            uint num3 = ReadUInt32LittleEndian(src);
            long fin = src.Position + cms - 13;
            byte[] cd = new byte[des];
            int num4 = 0;

            while (src.Position <= fin)
            {
                int b = src.ReadByte();
                if (b == num3)
                {
                    int b2 = src.ReadByte();
                    if (b2 != num3)
                    {
                        if (b2 >= num3)
                        {
                            b2 -= 1;
                        }
                        int b3 = src.ReadByte();
                        if (b2 < b3)
                        {
                            for (int i = 0; i < b3; i++)
                            {
                                cd[num4] = cd[num4 - b2];
                                num4++;
                            }
                        }
                        else
                        {
                            Buffer.BlockCopy(cd, num4 - b2, cd, num4, b3);
                            num4 += b3;
                        }
                    }
                    else
                    {
                        cd[num4] = (byte)b2;
                        num4++;
                    }
                }
                else
                {
                    cd[num4] = (byte)b;
                    num4++;
                }
            }
            return cd;
        }
        private byte[] UncompressLZ4(Stream src, uint decompressedSize, uint compressedSize)
        {
            byte[] dst = new byte[decompressedSize];
            const int minMatchLen = 4;
            int num4 = 0;
            long fin = src.Position + compressedSize;
            while (src.Position <= fin)
            {
                int token = src.ReadByte();
                if (token == -1)
                {
                    throw new Exception("读取文字长度时遇到文件结束符");
                }
                int literalLen = GetLength(src, (token >> 4) & 0x0f);
                byte[] readBuf = new byte[literalLen];
                int bytesRead = src.Read(readBuf, 0, literalLen);
                if (bytesRead != literalLen)
                {
                    throw new Exception("非字面数据");
                }
                Buffer.BlockCopy(readBuf, 0, dst, num4, literalLen);
                num4 += literalLen;
                if (src.Position > fin)
                {
                    if ((token & 0x0f) != 0)
                    {
                        throw new Exception($"EOF,但匹配长度大于0: {token & 0x0f}");
                    }
                    break;
                }
                byte[] offsetBytes = new byte[2];
                bytesRead = src.Read(offsetBytes, 0, 2);
                if (bytesRead != 2)
                {
                    throw new Exception("过早的文件结束符");
                }
                int offset = offsetBytes[0] | (offsetBytes[1] << 8);

                if (offset == 0)
                {
                    throw new Exception("偏移量不能为0");
                }
                int matchLen = GetLength(src, token & 0x0f);
                matchLen += minMatchLen;
                if (offset < matchLen)
                {
                    for (int i = 0; i < matchLen; i++)
                    {
                        dst[num4] = dst[num4 - offset];
                        num4++;
                    }
                }
                else
                {
                    Buffer.BlockCopy(dst, num4 - offset, dst, num4, matchLen);
                    num4 += matchLen;
                }
            }
            return dst;
        }
        private int GetLength(Stream src, int length)
        {
            if (length != 0x0f)
            {
                return length;
            }
            while (true)
            {
                int lenPart = src.ReadByte();
                if (lenPart == -1)
                {
                    throw new Exception("读取到文件末尾的长度");
                }
                length += lenPart;

                if (lenPart != 0xff)
                {
                    break;
                }
            }
            return length;
        }
        private uint ReadUInt32LittleEndian(Stream stream)
        {
            byte[] bytes = new byte[4];
            stream.Read(bytes, 0, 4);
            return BitConverter.ToUInt32(bytes, 0);
        }
        private struct FileEntry
        {
            public uint Offset;
            public uint CompressedSize;
            public uint UncompressedSize;
            public uint Flags;
        }
    }
}