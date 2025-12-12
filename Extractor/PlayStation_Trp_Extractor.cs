using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class PlayStation_Trp_Extractor : BaseExtractor
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
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var trpFiles = Directory.EnumerateFiles(directoryPath, "*.trp", SearchOption.AllDirectories)
                .ToList();

            if (trpFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, $"未找到.trp文件");
                OnExtractionFailed($"未找到.trp文件");
                return;
            }

            TotalFilesToExtract = trpFiles.Count;
            ExtractionProgress?.Invoke(this, $"找到{trpFiles.Count}个.trp文件,开始提取...");

            int processedCount = 0;
            int totalExtractedFiles = 0;

            try
            {
                foreach (var filePath in trpFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    processedCount++;
                    string fileName = Path.GetFileName(filePath);
                    ExtractionProgress?.Invoke(this, $"正在处理文件({processedCount}/{trpFiles.Count}): {fileName}");

                    try
                    {
                        int extractedCount = await ExtractTrpFile(filePath, cancellationToken);
                        totalExtractedFiles += extractedCount;

                        ExtractionProgress?.Invoke(this, $"{fileName}提取完成,共提取{extractedCount}个文件");
                    }
                    catch (OperationCanceledException)
                    {
                        ExtractionError?.Invoke(this, "提取操作已取消");
                        OnExtractionFailed("提取操作已取消");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{fileName}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{fileName}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"所有.trp文件处理完成,总共提取{totalExtractedFiles}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取过程中出错:{ex.Message}");
                OnExtractionFailed($"提取过程中出错:{ex.Message}");
            }
        }

        private async Task<int> ExtractTrpFile(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
            {
                ExtractionError?.Invoke(this, $"文件不存在{filePath}");
                return 0;
            }

            string basename = Path.GetFileNameWithoutExtension(filePath);
            string outputDir = Path.Combine(Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory, basename);

            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
                await Task.Delay(300, cancellationToken);
            }

            Directory.CreateDirectory(outputDir);

            ExtractionProgress?.Invoke(this, $"正在解包:{Path.GetFileName(filePath)}");
            ExtractionProgress?.Invoke(this, $"输出到:{outputDir}");

            int extractedCount = 0;

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BigEndianBinaryReader(fs))
                {
                    Header header = ReadHeader(reader);

                    if (header.magic != 0x01000000004DA2DC)
                    {
                        ExtractionError?.Invoke(this, $"不是有效的TRP文件:{Path.GetFileName(filePath)}");
                        return 0;
                    }

                    int fileCount = header.AllFileCount;
                    ExtractionProgress?.Invoke(this, $"文件数量:{fileCount}");

                    var fileInfos = new FileOffsetInfo[fileCount];
                    for (int i = 0; i < fileCount; i++)
                    {
                        fileInfos[i] = ReadFileOffsetInfo(reader);
                    }

                    foreach (var fileInfo in fileInfos)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (fileInfo.FileSize == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"跳过文件{fileInfo.fileName}:大小为0");
                            continue;
                        }

                        if (fileInfo.Offset >= fs.Length)
                        {
                            ExtractionError?.Invoke(this, $"文件{fileInfo.fileName}偏移地址超出文件范围:{fileInfo.Offset}");
                            continue;
                        }

                        if (fileInfo.Offset + fileInfo.FileSize > fs.Length)
                        {
                            ExtractionError?.Invoke(this, $"文件{fileInfo.fileName}大小超出文件范围:偏移{fileInfo.Offset},大小{fileInfo.FileSize}");
                            continue;
                        }

                        fs.Seek(fileInfo.Offset, SeekOrigin.Begin);

                        byte[] data = reader.ReadBytes((int)fileInfo.FileSize);

                        if (data.Length == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"文件{fileInfo.fileName}:数据长度为0");
                            continue;
                        }

                        string safeFileName = SanitizeFileName(fileInfo.fileName);
                        string outputFile = Path.Combine(outputDir, safeFileName);

                        string? outputFileDir = Path.GetDirectoryName(outputFile);
                        if (!string.IsNullOrEmpty(outputFileDir) && !Directory.Exists(outputFileDir))
                        {
                            Directory.CreateDirectory(outputFileDir);
                        }

                        await File.WriteAllBytesAsync(outputFile, data, cancellationToken);

                        extractedCount++;
                        double sizeKB = fileInfo.FileSize / 1024.0;

                        ExtractionProgress?.Invoke(this, $"{safeFileName,-40}- {fileInfo.FileSize,9}字节({sizeKB,7:F1}KB)");
                        OnFileExtracted(outputFile);
                    }
                }

                ExtractionProgress?.Invoke(this, $"解包完成!提取了{extractedCount}个文件到'{outputDir}'目录");

                if (extractedCount == 0)
                {
                    ExtractionError?.Invoke(this, $"警告:没有提取到任何文件!可能原因:文件格式不正确或已损坏");
                }

                return extractedCount;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取TRP文件时出错:{ex.Message}");
                throw new Exception($"提取TRP文件时出错:{ex.Message}", ex);
            }
        }

        private Header ReadHeader(BigEndianBinaryReader reader)
        {
            int headerSize = Marshal.SizeOf(typeof(Header));
            byte[] headerBytes = reader.ReadBytes(headerSize);
            return Extensions.ToStruct<Header>(headerBytes);
        }

        private FileOffsetInfo ReadFileOffsetInfo(BigEndianBinaryReader reader)
        {
            int infoSize = Marshal.SizeOf(typeof(FileOffsetInfo));
            byte[] infoBytes = reader.ReadBytes(infoSize);
            return Extensions.ToStruct<FileOffsetInfo>(infoBytes);
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "unnamed.bin";

            fileName = fileName.TrimEnd('\0');

            if (string.IsNullOrEmpty(fileName))
                return "unnamed.bin";

            string invalidChars = new string(Path.GetInvalidFileNameChars());
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }

            return fileName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Header
        {
            public ulong magic;
            public ulong _fileSize;
            private int u1;
            public int AllFileCount
            {
                get
                {
                    return Extensions.ChangeEndian(u1);
                }
            }
            public int u2;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40, ArraySubType = UnmanagedType.I1)]
            public byte[] padding;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct FileOffsetInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string fileName;
            private long _offset;
            public long Offset
            {
                get
                {
                    return Extensions.ChangeEndian(_offset);
                }
            }
            private long _fileSize;
            public long FileSize
            {
                get
                {
                    return Extensions.ChangeEndian(_fileSize);
                }
            }
            public int u1;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12, ArraySubType = UnmanagedType.I1)]
            public byte[] padding;
        }
    }

    internal class BigEndianBinaryReader : BinaryReader
    {
        private byte[] a16 = new byte[2];
        private byte[] a32 = new byte[4];
        private byte[] a64 = new byte[8];

        public BigEndianBinaryReader(Stream stream) : base(stream) { }

        public override short ReadInt16()
        {
            a16 = base.ReadBytes(2);
            Array.Reverse(a16);
            return BitConverter.ToInt16(a16, 0);
        }

        public override ushort ReadUInt16()
        {
            a16 = base.ReadBytes(2);
            Array.Reverse(a16);
            return BitConverter.ToUInt16(a16, 0);
        }

        public override int ReadInt32()
        {
            a32 = base.ReadBytes(4);
            Array.Reverse(a32);
            return BitConverter.ToInt32(a32, 0);
        }

        public override uint ReadUInt32()
        {
            a32 = base.ReadBytes(4);
            Array.Reverse(a32);
            return BitConverter.ToUInt32(a32, 0);
        }

        public override long ReadInt64()
        {
            a64 = base.ReadBytes(8);
            Array.Reverse(a64);
            return BitConverter.ToInt64(a64, 0);
        }

        public override ulong ReadUInt64()
        {
            a64 = base.ReadBytes(8);
            Array.Reverse(a64);
            return BitConverter.ToUInt64(a64, 0);
        }

        public DateTime ReadPS3DateTime()
        {
            return new DateTime(ReadInt64() * 10);
        }
    }

    internal static class Extensions
    {
        public static T ToStruct<T>(this byte[] ptr) where T : struct
        {
            if (ptr == null || ptr.Length == 0)
                throw new ArgumentException("字节数组不能为null或空", nameof(ptr));

            if (ptr.Length < Marshal.SizeOf<T>())
                throw new ArgumentException($"字节数组长度不足，需要{Marshal.SizeOf<T>()}字节，实际{ptr.Length}字节", nameof(ptr));

            GCHandle handle = GCHandle.Alloc(ptr, GCHandleType.Pinned);
            try
            {
                T ret = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
                return ret;
            }
            finally
            {
                handle.Free();
            }
        }

        public static int ChangeEndian(this int val)
        {
            byte[] arr = BitConverter.GetBytes(val);
            Array.Reverse(arr);
            return BitConverter.ToInt32(arr, 0);
        }

        public static long ChangeEndian(this long val)
        {
            byte[] arr = BitConverter.GetBytes(val);
            Array.Reverse(arr);
            return BitConverter.ToInt64(arr, 0);
        }

        public static ulong ChangeEndian(this ulong val)
        {
            byte[] arr = BitConverter.GetBytes(val);
            Array.Reverse(arr);
            return BitConverter.ToUInt64(arr, 0);
        }
    }
}