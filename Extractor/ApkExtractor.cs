using System.IO.Compression;
using System.Text;

namespace super_toolbox
{
    public class ApkExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private string _existsMode = "skip";
        private bool _isDebug = false;

        public ApkExtractor(string existsMode = "skip", bool isDebug = false)
        {
            _existsMode = existsMode;
            _isDebug = isDebug;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                var apkFiles = Directory.EnumerateFiles(directoryPath, "*.apk", SearchOption.AllDirectories);
                var validApkFiles = new List<string>();

                foreach (string apkFile in apkFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    if (IsEndiltleApk(apkFile))
                    {
                        validApkFiles.Add(apkFile);
                        ExtractionProgress?.Invoke(this, $"找到有效APK文件:{Path.GetFileName(apkFile)}");
                    }
                }

                if (validApkFiles.Count == 0)
                {
                    ExtractionError?.Invoke(this, "未找到任何有效的APK文件");
                    OnExtractionFailed("未找到任何有效的APK文件");
                    return;
                }

                TotalFilesToExtract = validApkFiles.Count;
                ExtractionProgress?.Invoke(this, $"找到{validApkFiles.Count}个有效APK文件，开始提取...");

                int totalExtractedFiles = 0;
                foreach (string apkFile in validApkFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    try
                    {
                        ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(apkFile)}");
                        int extractedCount = await ProcessApkFile(apkFile, directoryPath, cancellationToken);
                        totalExtractedFiles += extractedCount;
                        ExtractionProgress?.Invoke(this, $"完成处理:{Path.GetFileName(apkFile)} -> 提取{extractedCount}个文件");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理{Path.GetFileName(apkFile)}时出错:{ex.Message}");
                        OnExtractionFailed($"处理{Path.GetFileName(apkFile)}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"APK提取完成，共提取{totalExtractedFiles}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "APK提取操作已取消");
                OnExtractionFailed("APK提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"APK提取失败:{ex.Message}");
                OnExtractionFailed($"APK提取失败:{ex.Message}");
            }
        }

        private async Task<int> ProcessApkFile(string apkFilePath, string baseDirectory, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                int fileCount = 0;
                string apkName = Path.GetFileNameWithoutExtension(apkFilePath);
                string outputDir = Path.Combine(baseDirectory, "Extracted", apkName ?? "Unnamed");
                Directory.CreateDirectory(outputDir);

                try
                {
                    using var stream = new FileStream(apkFilePath, FileMode.Open, FileAccess.Read);
                    using var reader = new System.IO.BinaryReader(stream);

                    var unpacker = new UnpackApk(reader, outputDir, _existsMode, _isDebug);

                    unpacker.FileExtracted += (sender, filePath) =>
                    {
                        fileCount++;
                        OnFileExtracted(filePath);
                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(filePath)}");
                    };

                    unpacker.ErrorOccurred += (sender, errorMessage) =>
                    {
                        ExtractionError?.Invoke(this, errorMessage);
                    };

                    unpacker.Extract();

                    return fileCount;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理APK文件失败:{Path.GetFileName(apkFilePath)} - {ex.Message}");
                    return 0;
                }
            }, cancellationToken);
        }

        private bool IsEndiltleApk(string filePath)
        {
            try
            {
                byte[] header = new byte[8];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Read(header, 0, 8) != 8)
                        return false;
                }
                return Encoding.ASCII.GetString(header) == "ENDILTLE";
            }
            catch
            {
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }

    public class UnpackApk
    {
        private System.IO.BinaryReader _reader;
        private string OutputDirPath { get; }
        private string FileExists { get; }
        private bool IsDebug { get; }
        private int _fileCount = 0;

        public event EventHandler<string> FileExtracted;
        public event EventHandler<string> ProcessStarted;
        public event EventHandler<string> ProcessCompleted;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler<int> ProcessCompletedWithCount;

        public UnpackApk(System.IO.BinaryReader reader, string outputPath, string fileExists, bool debug)
        {
            _reader = reader;
            OutputDirPath = outputPath;
            FileExists = fileExists;
            IsDebug = debug;

            FileExtracted = delegate { };
            ProcessStarted = delegate { };
            ProcessCompleted = delegate { };
            ErrorOccurred = delegate { };
            ProcessCompletedWithCount = delegate { };
        }

        public void Extract()
        {
            _fileCount = 0;
            string processStartedMessage = _reader.BaseStream?.ToString() ?? "Unknown Stream";
            ProcessStarted?.Invoke(this, processStartedMessage);
            Directory.CreateDirectory(OutputDirPath);

            try
            {
                _reader!.BaseStream!.Seek(0, SeekOrigin.Begin);
                var dump = new Dictionary<string, object>
                {
                    ["PACKTOC"] = new Dictionary<string, object>(),
                    ["PACKFSLS"] = new Dictionary<string, object>(),
                    ["GENESTRT"] = new Dictionary<string, object>(),
                    ["FILE_AREA"] = new Dictionary<string, object>()
                };
                var fileList = new Dictionary<string, List<Dictionary<string, object>>>();

                try
                {
                    string endianness = ReadStringBytes(8);
                    byte[] zero = _reader.ReadBytes(8);

                    string packhedr = ReadStringBytes(8);
                    ulong headerSize = ReadU64();
                    byte[] unknown1 = _reader.ReadBytes(8);
                    uint fileListOffset = ReadU32();
                    byte[] unknown2 = _reader.ReadBytes(4);
                    byte[] unknown3 = _reader.ReadBytes(16);

                    string packtoc = ReadStringBytes(8);
                    headerSize = ReadU64();
                    int packtocStartOffset = (int)_reader.BaseStream.Position;
                    uint tocSegSize = ReadU32();
                    uint tocSegCount = ReadU32();
                    byte[] unknown4 = _reader.ReadBytes(4);
                    zero = _reader.ReadBytes(4);

                    var tocSegmentList = new List<Dictionary<string, ByteSegment>>();
                    ((Dictionary<string, object>)dump["PACKTOC"])["TOC_SEGMENT_LIST"] = tocSegmentList;

                    for (int i = 0; i < tocSegCount; i++)
                    {
                        uint identifier = ReadU32();
                        uint nameIdx = ReadU32();
                        byte[] unknown5 = _reader.ReadBytes(8);
                        ulong fileOffset = ReadU64();
                        ulong size = ReadU64();
                        ulong zsize = ReadU64();

                        tocSegmentList.Add(new Dictionary<string, ByteSegment>
                        {
                            ["IDENTIFIER"] = new ByteSegment("int", identifier),
                            ["NAME_IDX"] = new ByteSegment("int", nameIdx),
                            ["FILE_OFFSET"] = new ByteSegment("offset", fileOffset),
                            ["SIZE"] = new ByteSegment("int", size),
                            ["ZSIZE"] = new ByteSegment("int", zsize)
                        });
                    }

                    int padCnt = (int)(packtocStartOffset + (int)headerSize) - (int)_reader.BaseStream.Position;
                    byte[] padding = _reader.ReadBytes(padCnt);

                    string packfsls = ReadStringBytes(8);
                    headerSize = ReadU64();
                    int packfslsStartOffset = (int)_reader.BaseStream.Position;
                    uint archiveCount = ReadU32();
                    uint archiveSegSize = ReadU32();
                    byte[] unknown6 = _reader.ReadBytes(4);
                    byte[] unknown7 = _reader.ReadBytes(4);

                    var archiveSegmentList = new List<Dictionary<string, ByteSegment>>();
                    ((Dictionary<string, object>)dump["PACKFSLS"])["ARCHIVE_SEGMENT_LIST"] = archiveSegmentList;

                    for (int i = 0; i < archiveCount; i++)
                    {
                        uint nameIdx = ReadU32();
                        byte[] unknown8 = _reader.ReadBytes(4);
                        ulong archiveOffset = ReadU64();
                        ulong size = ReadU64();
                        byte[] dummy = _reader.ReadBytes(16);

                        archiveSegmentList.Add(new Dictionary<string, ByteSegment>
                        {
                            ["NAME_IDX"] = new ByteSegment("int", nameIdx),
                            ["ARCHIVE_OFFSET"] = new ByteSegment("offset", archiveOffset),
                            ["SIZE"] = new ByteSegment("int", size)
                        });
                    }

                    padCnt = (int)(packfslsStartOffset + (int)headerSize) - (int)_reader.BaseStream.Position;
                    padding = _reader.ReadBytes(padCnt);

                    string genestrt = ReadStringBytes(8);
                    ulong genestrtSize = ReadU64();
                    int genestrtStartOffset = (int)_reader.BaseStream.Position;
                    uint strOffsetCount = ReadU32();
                    byte[] unknown9 = _reader.ReadBytes(4);
                    uint genestrtSize2 = ReadU32();
                    ((Dictionary<string, object>)dump["GENESTRT"])["HEADER_SIZE+STR_OFFSET_LIST_SIZE"] = new ByteSegment("int", genestrtSize2);
                    genestrtSize2 = ReadU32();

                    var strOffsetList = new List<ByteSegment>();
                    ((Dictionary<string, object>)dump["GENESTRT"])["STR_OFFSET_LIST"] = strOffsetList;

                    for (int i = 0; i < strOffsetCount; i++)
                    {
                        strOffsetList.Add(new ByteSegment("int", ReadU32()));
                    }

                    padCnt = (int)(genestrtStartOffset + (int)((ByteSegment)((Dictionary<string, object>)dump["GENESTRT"])["HEADER_SIZE+STR_OFFSET_LIST_SIZE"]).GetInt()) - (int)_reader.BaseStream.Position;
                    ((Dictionary<string, object>)dump["GENESTRT"])["PAD"] = new ByteSegment("raw", _reader.ReadBytes(padCnt));

                    var stringList = new List<ByteSegment>();
                    ((Dictionary<string, object>)dump["GENESTRT"])["STRING_LIST"] = stringList;

                    for (int i = 0; i < strOffsetCount; i++)
                    {
                        try
                        {
                            string str = ReadStringUtf8();
                            stringList.Add(new ByteSegment("str", str));
                        }
                        catch (Exception e)
                        {
                            string errorMessage = $"字符串解码错误:{e.Message}";
                            ErrorOccurred?.Invoke(this, errorMessage);
                            stringList.Add(new ByteSegment("str", string.Empty));
                        }
                    }

                    padCnt = (int)(genestrtStartOffset + (int)genestrtSize) - (int)_reader.BaseStream.Position;
                    ((Dictionary<string, object>)dump["GENESTRT"])["TABLE_PADDING"] = new ByteSegment("raw", _reader.ReadBytes(padCnt));

                    string geneeof = ReadStringBytes(8);
                    byte[] zeroPadding = _reader.ReadBytes(8);
                    byte[] tablePadding = _reader.ReadBytes((int)fileListOffset - (int)_reader.BaseStream.Position);

                    ((Dictionary<string, object>)dump["FILE_AREA"])["ROOT_ARCHIVE"] = new Dictionary<string, object>();

                    foreach (var tocSeg in tocSegmentList)
                    {
                        string fname = stringList[(int)tocSeg["NAME_IDX"].GetInt()].StringValue.TrimEnd('\0');
                        uint identifier = (uint)tocSeg["IDENTIFIER"].GetInt();
                        ulong fileOffset = tocSeg["FILE_OFFSET"].GetInt();
                        ulong size = tocSeg["SIZE"].GetInt();
                        ulong zsize = tocSeg["ZSIZE"].GetInt();

                        _reader.BaseStream.Seek((long)fileOffset, SeekOrigin.Begin);
                        ulong realSize = zsize == 0 ? size : zsize;
                        if (identifier == 1 || realSize == 0)
                        {
                            continue;
                        }

                        byte[] file = _reader.ReadBytes((int)realSize);
                        string outPath = Path.Combine(OutputDirPath, fname);

                        if (!fileList.ContainsKey(fname))
                        {
                            fileList[fname] = new List<Dictionary<string, object>>();
                        }

                        fileList[fname].Add(new Dictionary<string, object>
                        {
                            ["out_path"] = outPath,
                            ["file"] = file,
                            ["offset"] = fileOffset,
                            ["zsize"] = zsize,
                            ["fname"] = fname
                        });
                    }

                    for (int i = 0; i < archiveCount; i++)
                    {
                        string key = $"ARCHIVE #{i}";
                        var archiveDict = new Dictionary<string, object>
                        {
                            ["PACKFSHD"] = new Dictionary<string, object>(),
                            ["GENESTRT"] = new Dictionary<string, object>()
                        };
                        ((Dictionary<string, object>)dump["FILE_AREA"])[key] = archiveDict;

                        uint nameIdx = (uint)archiveSegmentList[i]["NAME_IDX"].GetInt();
                        ulong archiveOffset = archiveSegmentList[i]["ARCHIVE_OFFSET"].GetInt();
                        ulong size = archiveSegmentList[i]["SIZE"].GetInt();
                        string archiveName = stringList[(int)nameIdx].StringValue.TrimEnd('\0');

                        _reader.BaseStream.Seek((long)archiveOffset, SeekOrigin.Begin);
                        string endiannessArchive = ReadStringBytes(8);
                        byte[] zeroArchive = _reader.ReadBytes(8);

                        string packfshd = ReadStringBytes(8);
                        ulong headerSizeArchive = ReadU64();
                        byte[] dummy1 = _reader.ReadBytes(4);
                        uint fileSegSize = ReadU32();
                        uint fileSegCount = ReadU32();
                        uint segCount = ReadU32();
                        byte[] dummy2 = _reader.ReadBytes(16);

                        var fileSegList = new List<Dictionary<string, ByteSegment>>();
                        ((Dictionary<string, object>)archiveDict["PACKFSHD"])["FILE_SEG_LIST"] = fileSegList;

                        for (int j = 0; j < fileSegCount; j++)
                        {
                            uint segNameIdx = ReadU32();
                            uint zip = ReadU32();
                            ulong offset = ReadU64();
                            ulong segSize = ReadU64();
                            ulong segZsize = ReadU64();

                            fileSegList.Add(new Dictionary<string, ByteSegment>
                            {
                                ["NAME_IDX"] = new ByteSegment("int", segNameIdx),
                                ["ZIP"] = new ByteSegment("int", zip),
                                ["OFFSET"] = new ByteSegment("offset", offset),
                                ["SIZE"] = new ByteSegment("int", segSize),
                                ["ZSIZE"] = new ByteSegment("int", segZsize)
                            });
                        }

                        string genestrtArchive = ReadStringBytes(8);
                        ulong genestrtSizeArchive = ReadU64();
                        int genestrtStartOffsetArchive = (int)_reader.BaseStream.Position;
                        uint strOffsetCountArchive = ReadU32();
                        byte[] unknown10 = _reader.ReadBytes(4);
                        uint genestrtSize2Archive = ReadU32();
                        ((Dictionary<string, object>)archiveDict["GENESTRT"])["HEADER_SIZE+STR_OFFSET_LIST_SIZE"] = new ByteSegment("int", genestrtSize2Archive);
                        genestrtSize2Archive = ReadU32();
                        ((Dictionary<string, object>)archiveDict["GENESTRT"])["GENESTRT_SIZE_2"] = new ByteSegment("int", genestrtSize2Archive);

                        var archiveStrOffsetList = new List<ByteSegment>();
                        ((Dictionary<string, object>)archiveDict["GENESTRT"])["STR_OFFSET_LIST"] = archiveStrOffsetList;

                        for (int j = 0; j < strOffsetCountArchive; j++)
                        {
                            archiveStrOffsetList.Add(new ByteSegment("int", ReadU32()));
                        }

                        padCnt = (int)(genestrtStartOffsetArchive + (int)((ByteSegment)((Dictionary<string, object>)archiveDict["GENESTRT"])["HEADER_SIZE+STR_OFFSET_LIST_SIZE"]).GetInt()) - (int)_reader.BaseStream.Position;
                        byte[] archivePadding = _reader.ReadBytes(padCnt);

                        var archiveStringList = new List<ByteSegment>();
                        ((Dictionary<string, object>)archiveDict["GENESTRT"])["STRING_LIST"] = archiveStringList;

                        for (int j = 0; j < strOffsetCountArchive; j++)
                        {
                            try
                            {
                                string str = ReadStringUtf8();
                                archiveStringList.Add(new ByteSegment("str", str));
                            }
                            catch (Exception e)
                            {
                                string errorMessage = $"字符串解码错误:{e.Message}";
                                ErrorOccurred?.Invoke(this, errorMessage);
                                archiveStringList.Add(new ByteSegment("str", string.Empty));
                            }
                        }

                        padCnt = (int)(genestrtStartOffsetArchive + (int)genestrtSizeArchive) - (int)_reader.BaseStream.Position;
                        ((Dictionary<string, object>)archiveDict["GENESTRT"])["TABLE_PADDING"] = new ByteSegment("raw", _reader.ReadBytes(padCnt));

                        ((Dictionary<string, object>)archiveDict)["FILE_AREA"] = new Dictionary<string, object>();

                        foreach (var fileSeg in fileSegList)
                        {
                            ulong offset = fileSeg["OFFSET"].GetInt();
                            ulong zsize = fileSeg["ZSIZE"].GetInt();
                            ulong segSize = fileSeg["SIZE"].GetInt();
                            uint segNameIdx = (uint)fileSeg["NAME_IDX"].GetInt();
                            string fname = archiveStringList[(int)segNameIdx].StringValue.TrimEnd('\0');

                            _reader.BaseStream.Seek((long)(archiveOffset + offset), SeekOrigin.Begin);
                            ulong realSize = zsize == 0 ? segSize : zsize;
                            if (realSize == 0)
                            {
                                continue;
                            }

                            byte[] file = _reader.ReadBytes((int)realSize);
                            string outPath = Path.Combine(OutputDirPath, archiveName, fname);
                            string fullName = $"{archiveName}/{fname}";

                            if (!fileList.ContainsKey(fullName))
                            {
                                fileList[fullName] = new List<Dictionary<string, object>>();
                            }

                            fileList[fullName].Add(new Dictionary<string, object>
                            {
                                ["out_path"] = outPath,
                                ["file"] = file,
                                ["offset"] = archiveOffset + offset,
                                ["zsize"] = zsize,
                                ["fname"] = fullName
                            });
                        }
                    }

                    foreach (var kvp in fileList)
                    {
                        bool isSameName = kvp.Value.Count > 1;
                        foreach (var obj in kvp.Value)
                        {
                            string outPath = (string)obj["out_path"];
                            byte[] file = (byte[])obj["file"];
                            ulong offset = (ulong)obj["offset"];
                            string fname = (string)obj["fname"];

                            if (ExtractFile(outPath, file, offset, isSameName, (ulong)obj["zsize"] != 0))
                            {
                                _fileCount++;
                            }
                        }
                    }

                    string processCompletedMessage = _reader.BaseStream?.ToString() ?? "Unknown Stream";
                    ProcessCompleted?.Invoke(this, processCompletedMessage);
                    ProcessCompletedWithCount?.Invoke(this, _fileCount);
                }
                catch (Exception e)
                {
                    string errorMessage = $"解析错误:{e.Message}";
                    ErrorOccurred?.Invoke(this, errorMessage);
                }
            }
            catch (Exception e)
            {
                string errorMessage = $"提取时出错:{e.Message}";
                ErrorOccurred?.Invoke(this, errorMessage);
            }
        }

        private bool ExtractFile(string outPath, byte[] file, ulong offset, bool isSameName, bool isZip)
        {
            if (isZip)
            {
                try
                {
                    using (var compressedStream = new MemoryStream(file, 2, file.Length - 2))
                    using (var decompressedStream = new MemoryStream())
                    using (var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
                    {
                        deflateStream.CopyTo(decompressedStream);
                        file = decompressedStream.ToArray();
                    }
                }
                catch (Exception e)
                {
                    string errorMessage = $"解压错误:{e.Message}";
                    ErrorOccurred?.Invoke(this, errorMessage);
                    return false;
                }
            }

            string? directory = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (isSameName)
            {
                string basename = Path.GetFileNameWithoutExtension(outPath);
                string extension = Path.GetExtension(outPath);
                directory = directory ?? Directory.GetCurrentDirectory();

                outPath = Path.Combine(directory, $"{basename}__OFS_{offset}{extension}");
            }

            if (File.Exists(outPath))
            {
                if (FileExists == "skip")
                {
                    return false;
                }
            }

            try
            {
                File.WriteAllBytes(outPath, file);
                FileExtracted?.Invoke(this, outPath);
                return true;
            }
            catch (Exception e)
            {
                string errorMessage = $"写入错误:{e.Message}";
                ErrorOccurred?.Invoke(this, errorMessage);
                return false;
            }
        }

        private string ReadStringBytes(int count)
        {
            byte[] buffer = _reader.ReadBytes(count);
            return Encoding.ASCII.GetString(buffer);
        }

        private uint ReadU32()
        {
            return _reader.ReadUInt32();
        }

        private ulong ReadU64()
        {
            return _reader.ReadUInt64();
        }

        private string ReadStringUtf8()
        {
            List<byte> bytes = new List<byte>();
            byte b;
            while ((b = _reader.ReadByte()) != 0)
            {
                bytes.Add(b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }

    public class ByteSegment
    {
        public string Type { get; set; }
        public byte[] Raw { get; set; }
        public string StringValue { get; set; }
        public ulong IntValue { get; set; }
        public string OffsetValue { get; set; }

        public ByteSegment(string type, object value)
        {
            Type = type;
            StringValue = string.Empty;
            OffsetValue = string.Empty;
            Raw = Array.Empty<byte>();

            switch (type)
            {
                case "raw":
                    Raw = (byte[])value;
                    break;
                case "str":
                    StringValue = (string)value;
                    Raw = Encoding.UTF8.GetBytes(StringValue);
                    break;
                case "int":
                    IntValue = Convert.ToUInt64(value);
                    Raw = BitConverter.GetBytes(IntValue);
                    break;
                case "offset":
                    IntValue = Convert.ToUInt64(value);
                    OffsetValue = "0x" + IntValue.ToString("X8");
                    Raw = BitConverter.GetBytes(IntValue);
                    break;
            }
        }

        public ulong GetInt()
        {
            return IntValue;
        }
    }
}
