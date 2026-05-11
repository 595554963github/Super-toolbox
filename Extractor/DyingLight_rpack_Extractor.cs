using System.Text;
using zlib;

namespace super_toolbox
{
    public class DyingLight_rpack_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

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

            var rpackFiles = Directory.EnumerateFiles(directoryPath, "*.rpack", SearchOption.AllDirectories)
                .ToList();

            if (rpackFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, $"未找到.rpack文件");
                OnExtractionFailed($"未找到.rpack文件");
                return;
            }

            TotalFilesToExtract = rpackFiles.Count;
            ExtractionProgress?.Invoke(this, $"找到{rpackFiles.Count}个.rpack文件,开始提取...");

            int processedCount = 0;
            int totalExtractedFiles = 0;

            try
            {
                foreach (var filePath in rpackFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    processedCount++;
                    string fileName = Path.GetFileName(filePath);
                    ExtractionProgress?.Invoke(this, $"正在处理文件({processedCount}/{rpackFiles.Count}): {fileName}");

                    try
                    {
                        int extractedCount = await ExtractRP6LWithAccurateCounting(filePath, cancellationToken);
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

                ExtractionProgress?.Invoke(this, $"所有.rpack文件处理完成,总共提取{totalExtractedFiles}个文件");
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

        private async Task<int> ExtractRP6LWithAccurateCounting(string filePath, CancellationToken cancellationToken)
        {
            string extractFolder = Path.Combine(
                Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(filePath));

            if (Directory.Exists(extractFolder))
            {
                Directory.Delete(extractFolder, true);
                await Task.Delay(300, cancellationToken);
            }

            var extractedFiles = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
            int lastReportedCount = 0;

            Directory.CreateDirectory(extractFolder);

            using (var fileWatcher = new FileSystemWatcher())
            {
                fileWatcher.Path = extractFolder;
                fileWatcher.Filter = "*.*";
                fileWatcher.IncludeSubdirectories = true;
                fileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime;
                fileWatcher.InternalBufferSize = 65536;

                var extractCompleted = new TaskCompletionSource<bool>();

                void OnFileCreated(object sender, FileSystemEventArgs e)
                {
                    if (File.Exists(e.FullPath))
                    {
                        if (extractedFiles.TryAdd(e.FullPath, true))
                        {
                            base.OnFileExtracted(e.FullPath);
                            UpdateProgressDisplay();

                            if (extractedFiles.Count % 10 == 0 || extractedFiles.Count <= 5)
                            {
                                string relativePath = GetRelativePath(e.FullPath, extractFolder);
                                ExtractionProgress?.Invoke(this, $"提取文件:{relativePath}");
                            }
                        }
                    }
                }

                void OnFileChanged(object sender, FileSystemEventArgs e)
                {
                    if (File.Exists(e.FullPath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(e.FullPath);
                            if (fileInfo.Length > 0 && extractedFiles.TryAdd(e.FullPath, true))
                            {
                                base.OnFileExtracted(e.FullPath);
                                UpdateProgressDisplay();
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                void UpdateProgressDisplay()
                {
                    int currentCount = extractedFiles.Count;
                    if (currentCount != lastReportedCount)
                    {
                        lastReportedCount = currentCount;
                        ExtractionProgress?.Invoke(this, $"已提取文件:{currentCount}个");
                    }
                }

                async Task StartPolling()
                {
                    int consecutiveNoChangeCount = 0;

                    while (!extractCompleted.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(2000, cancellationToken);

                        try
                        {
                            if (Directory.Exists(extractFolder))
                            {
                                var allFiles = Directory.GetFiles(extractFolder, "*.*", SearchOption.AllDirectories);
                                int previousCount = extractedFiles.Count;

                                foreach (var file in allFiles)
                                {
                                    if (File.Exists(file) && extractedFiles.TryAdd(file, true))
                                    {
                                        base.OnFileExtracted(file);
                                    }
                                }

                                int newFilesCount = extractedFiles.Count - previousCount;

                                if (newFilesCount > 0)
                                {
                                    consecutiveNoChangeCount = 0;
                                    int totalCount = extractedFiles.Count;
                                    ExtractionProgress?.Invoke(this, $"轮询发现{newFilesCount}个新文件,总计:{totalCount}个文件");
                                }
                                else
                                {
                                    consecutiveNoChangeCount++;
                                    if (consecutiveNoChangeCount >= 3)
                                    {
                                        ExtractionProgress?.Invoke(this, $"文件数量稳定:{extractedFiles.Count}个文件");
                                    }
                                }

                                UpdateProgressDisplay();
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"轮询错误:{ex.Message}");
                        }
                    }
                }

                fileWatcher.Created += OnFileCreated;
                fileWatcher.Changed += OnFileChanged;
                fileWatcher.EnableRaisingEvents = true;

                try
                {
                    var pollingTask = StartPolling();

                    var extractTask = Task.Run(() =>
                    {
                        return ExtractRP6L(filePath);
                    }, cancellationToken);

                    var timeoutTask = Task.Delay(600000, cancellationToken);
                    var completedTask = await Task.WhenAny(extractTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        throw new TimeoutException($"文件{Path.GetFileName(filePath)}提取超时");
                    }

                    bool success = await extractTask;
                    if (!success)
                    {
                        throw new Exception($"文件{Path.GetFileName(filePath)}提取失败");
                    }

                    await Task.Delay(3000, cancellationToken);

                    if (Directory.Exists(extractFolder))
                    {
                        var finalFiles = Directory.GetFiles(extractFolder, "*.*", SearchOption.AllDirectories);
                        int finalNewCount = 0;

                        foreach (var file in finalFiles)
                        {
                            if (File.Exists(file) && extractedFiles.TryAdd(file, true))
                            {
                                finalNewCount++;
                                base.OnFileExtracted(file);
                            }
                        }

                        if (finalNewCount > 0)
                        {
                            ExtractionProgress?.Invoke(this, $"最终扫描发现{finalNewCount}个文件");
                        }
                    }

                    extractCompleted.TrySetResult(true);
                    await pollingTask;

                    int finalCount = extractedFiles.Count;
                    ExtractionProgress?.Invoke(this, $"{Path.GetFileName(filePath)}提取完成,最终数量:{finalCount}个文件");

                    return finalCount;
                }
                catch
                {
                    extractCompleted.TrySetResult(false);
                    throw;
                }
                finally
                {
                    fileWatcher.EnableRaisingEvents = false;
                    fileWatcher.Created -= OnFileCreated;
                    fileWatcher.Changed -= OnFileChanged;
                }
            }
        }

        private string GetRelativePath(string fullPath, string basePath)
        {
            if (string.IsNullOrEmpty(basePath))
                return Path.GetFileName(fullPath);

            try
            {
                Uri fullUri = new Uri(fullPath);
                Uri baseUri = new Uri(basePath + Path.DirectorySeparatorChar);

                string relativePath = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString());
                return relativePath.Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                try
                {
                    string full = Path.GetFullPath(fullPath);
                    string baseFull = Path.GetFullPath(basePath);

                    if (full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                    {
                        return full.Substring(baseFull.Length).TrimStart(Path.DirectorySeparatorChar);
                    }
                    return Path.GetFileName(fullPath);
                }
                catch
                {
                    return Path.GetFileName(fullPath);
                }
            }
        }

        #region 核心提取代码

        public class RP6LHeader
        {
            public string Signature = "";
            public uint Version;
            public uint CompressionMethod;
            public uint PartAmount;
            public uint SectionAmount;
            public uint FileAmount;
            public uint FilenameChunkLength;
            public uint FilenameAmount;
            public uint BlockSize;
        }

        public class RP6LSectionInfo
        {
            public byte Filetype;
            public byte Unknown1;
            public byte Unknown2;
            public byte Unknown3;
            public uint Offset;
            public uint UnpackedSize;
            public uint PackedSize;
            public uint Unknown4;
        }

        public class RP6LPartInfo
        {
            public byte SectionIndex;
            public byte Unknown1;
            public ushort FileIndex;
            public uint Offset;
            public uint Size;
            public uint Unknown2;
        }

        public class RP6LFileInfo
        {
            public byte PartAmount;
            public byte Unknown1;
            public byte Filetype;
            public byte Unknown2;
            public uint FileIndex;
            public uint FirstPart;
        }

        public static Dictionary<byte, string> ResourceTypeLookup = new Dictionary<byte, string>
        {
            {0x10, "mesh"}, {0x12, "skin"}, {0x20, "texture"}, {0x30, "material"},
            {0x40, "animation"}, {0x41, "animation_id"}, {0x42, "animation_scr"},
            {0x50, "fx"}, {0x60, "lightmap"}, {0x61, "flash"}, {0x65, "sound"},
            {0x66, "sound_music"}, {0x67, "sound_speech"}, {0x68, "sound_stream"},
            {0x69, "sound_local"}, {0x70, "density_map"}, {0x80, "height_map"},
            {0x90, "mimic"}, {0xA0, "pathmap"}, {0xB0, "phonemes"}, {0xC0, "static_geometry"},
            {0xD0, "text"}, {0xE0, "binary"}, {0xF8, "tiny_objects"}, {0xFF, "resource_list"}
        };

        public static HashSet<byte> DesiredTypes = new HashSet<byte> { 0x10, 0x20, 0x40 };

        public static bool ExtractRP6L(string path)
        {
            try
            {
                using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
                using BinaryReader reader = new BinaryReader(fileStream);

                byte[] signatureBytes = reader.ReadBytes(4);
                string signature = Encoding.ASCII.GetString(signatureBytes);

                if (signature != "RP6L")
                {
                    Console.WriteLine($"无效文件:{path}");
                    return false;
                }

                RP6LHeader header = new RP6LHeader
                {
                    Signature = signature,
                    Version = reader.ReadUInt32(),
                    CompressionMethod = reader.ReadUInt32(),
                    PartAmount = reader.ReadUInt32(),
                    SectionAmount = reader.ReadUInt32(),
                    FileAmount = reader.ReadUInt32(),
                    FilenameChunkLength = reader.ReadUInt32(),
                    FilenameAmount = reader.ReadUInt32(),
                    BlockSize = reader.ReadUInt32()
                };

                Console.WriteLine($"解包:{path}");

                uint offsetMultiplier = header.Version == 4 ? 16u : 1u;

                List<RP6LSectionInfo> sectionInfos = new List<RP6LSectionInfo>();
                for (int i = 0; i < header.SectionAmount; i++)
                {
                    RP6LSectionInfo sectionInfo = new RP6LSectionInfo
                    {
                        Filetype = reader.ReadByte(),
                        Unknown1 = reader.ReadByte(),
                        Unknown2 = reader.ReadByte(),
                        Unknown3 = reader.ReadByte(),
                        Offset = reader.ReadUInt32(),
                        UnpackedSize = reader.ReadUInt32(),
                        PackedSize = reader.ReadUInt32(),
                        Unknown4 = reader.ReadUInt32()
                    };
                    sectionInfos.Add(sectionInfo);
                }

                List<RP6LPartInfo> partInfos = new List<RP6LPartInfo>();
                for (int i = 0; i < header.PartAmount; i++)
                {
                    RP6LPartInfo partInfo = new RP6LPartInfo
                    {
                        SectionIndex = reader.ReadByte(),
                        Unknown1 = reader.ReadByte(),
                        FileIndex = reader.ReadUInt16(),
                        Offset = reader.ReadUInt32(),
                        Size = reader.ReadUInt32(),
                        Unknown2 = reader.ReadUInt32()
                    };
                    partInfos.Add(partInfo);
                }

                List<RP6LFileInfo> fileInfos = new List<RP6LFileInfo>();
                for (int i = 0; i < header.FileAmount; i++)
                {
                    RP6LFileInfo fileInfo = new RP6LFileInfo
                    {
                        PartAmount = reader.ReadByte(),
                        Unknown1 = reader.ReadByte(),
                        Filetype = reader.ReadByte(),
                        Unknown2 = reader.ReadByte(),
                        FileIndex = reader.ReadUInt32(),
                        FirstPart = reader.ReadUInt32()
                    };
                    fileInfos.Add(fileInfo);
                }

                List<uint> filenameOffsets = new List<uint>();
                for (int i = 0; i < header.FileAmount; i++)
                {
                    filenameOffsets.Add(reader.ReadUInt32());
                }

                byte[] filenameChunk = reader.ReadBytes((int)header.FilenameChunkLength);
                string filenameChunkStr = Encoding.ASCII.GetString(filenameChunk);

                string? directoryName = Path.GetDirectoryName(path);
                string extractFolder = Path.Combine(directoryName ?? Environment.CurrentDirectory, Path.GetFileNameWithoutExtension(path));
                Directory.CreateDirectory(extractFolder);

                Dictionary<int, byte[]> sectionFiles = new Dictionary<int, byte[]>();

                for (int i = 0; i < sectionInfos.Count; i++)
                {
                    RP6LSectionInfo sectionInfo = sectionInfos[i];
                    if (sectionInfo.PackedSize > 0)
                    {
                        fileStream.Seek(sectionInfo.Offset * offsetMultiplier, SeekOrigin.Begin);
                        byte[] compressedData = reader.ReadBytes((int)sectionInfo.PackedSize);
                        byte[] uncompressedData = InflateData(compressedData);
                        sectionFiles[i] = uncompressedData;
                    }
                }

                int extractedCount = 0;

                for (int i = 0; i < fileInfos.Count; i++)
                {
                    RP6LFileInfo fileInfo = fileInfos[i];
                    if (!DesiredTypes.Contains(fileInfo.Filetype))
                        continue;

                    string resourceType = ResourceTypeLookup.ContainsKey(fileInfo.Filetype) ? ResourceTypeLookup[fileInfo.Filetype] : "unknown";

                    uint filenameOffset = filenameOffsets[i];
                    int filenameEnd = filenameChunkStr.IndexOf('\0', (int)filenameOffset);
                    string filename = filenameEnd == -1
                        ? filenameChunkStr.Substring((int)filenameOffset)
                        : filenameChunkStr.Substring((int)filenameOffset, filenameEnd - (int)filenameOffset);

                    string typeFolder = Path.Combine(extractFolder, resourceType);
                    Directory.CreateDirectory(typeFolder);

                    List<byte> fileData = new List<byte>();
                    uint currentPart = fileInfo.FirstPart;

                    for (int partIndex = 0; partIndex < fileInfo.PartAmount; partIndex++)
                    {
                        if (currentPart >= partInfos.Count)
                            break;

                        RP6LPartInfo partInfo = partInfos[(int)currentPart];
                        int sectionIndex = partInfo.SectionIndex;
                        uint dataLength = partInfo.Size;

                        byte[] partData;
                        if (sectionFiles.ContainsKey(sectionIndex))
                        {
                            byte[] sectionData = sectionFiles[sectionIndex];
                            uint dataOffset = partInfo.Offset;
                            if (dataOffset + dataLength <= sectionData.Length)
                            {
                                partData = new byte[dataLength];
                                Array.Copy(sectionData, dataOffset, partData, 0, dataLength);
                            }
                            else
                            {
                                partData = Array.Empty<byte>();
                            }
                        }
                        else
                        {
                            RP6LSectionInfo sectionInfoObj = sectionInfos[sectionIndex];
                            long textureDataOffset = (sectionInfoObj.Offset + partInfo.Offset) * offsetMultiplier;
                            fileStream.Seek(textureDataOffset, SeekOrigin.Begin);
                            partData = reader.ReadBytes((int)dataLength);
                        }

                        fileData.AddRange(partData);
                        currentPart++;
                    }

                    byte[] rawData = fileData.ToArray();
                    string targetPath;

                    if (fileInfo.Filetype == 0x20)
                    {
                        filename += ".dds";
                        targetPath = Path.Combine(typeFolder, filename);
                        CreateDDSFileLegacy(rawData, targetPath);
                    }
                    else if (fileInfo.Filetype == 0x10)
                    {
                        filename += ".msh";
                        targetPath = Path.Combine(typeFolder, filename);
                        File.WriteAllBytes(targetPath, rawData);
                    }
                    else if (fileInfo.Filetype == 0x40)
                    {
                        filename += ".anm";
                        targetPath = Path.Combine(typeFolder, filename);
                        File.WriteAllBytes(targetPath, rawData);
                    }
                    else
                    {
                        targetPath = Path.Combine(typeFolder, filename);
                        File.WriteAllBytes(targetPath, rawData);
                    }

                    Console.WriteLine($"提取:{resourceType}/{Path.GetFileName(targetPath)}");
                    extractedCount++;
                }

                Console.WriteLine($"完成:{extractFolder}(提取了{extractedCount}个文件)");
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"失败{path}:{e}");
                return false;
            }
        }

        private static void CreateDDSFileLegacy(byte[] textureData, string outputPath)
        {
            try
            {
                using MemoryStream textureStream = new MemoryStream(textureData);
                using BinaryReader textureReader = new BinaryReader(textureStream);
                using FileStream fileStream2 = new FileStream(outputPath, FileMode.Create);
                using BinaryWriter binaryWriter = new BinaryWriter(fileStream2);

                int width = textureReader.ReadInt16();
                int height = textureReader.ReadInt16();
                textureReader.ReadInt16();
                textureReader.ReadInt16();
                textureReader.ReadInt16();
                textureReader.ReadInt16();
                int format = textureReader.ReadInt32();
                textureReader.ReadInt32();
                textureReader.ReadInt16();
                textureReader.ReadByte();
                textureReader.ReadInt32();
                int dataSize = textureReader.ReadInt32();

                int ddsFormat = 808540228;

                if (format == 2)
                {
                    format = 28;
                }
                else if (format == 14)
                {
                    format = 61;
                }
                else if (format == 17)
                {
                    ddsFormat = 827611204;
                }
                else if (format == 18)
                {
                    ddsFormat = 861165636;
                }
                else if (format == 19)
                {
                    ddsFormat = 894720068;
                }
                else if (format == 33)
                {
                    format = 10;
                }
                else
                {
                    Console.WriteLine("未知的纹理格式" + format);
                }

                binaryWriter.Write(533118272580L);
                binaryWriter.Write(4103);
                binaryWriter.Write(height);
                binaryWriter.Write(width);
                binaryWriter.Write(dataSize);
                binaryWriter.Write(0);
                binaryWriter.Write(1);
                fileStream2.Seek(44L, SeekOrigin.Current);
                binaryWriter.Write(32);
                binaryWriter.Write(4);
                binaryWriter.Write(ddsFormat);
                fileStream2.Seek(40L, SeekOrigin.Current);
                if (ddsFormat == 808540228)
                {
                    binaryWriter.Write(format);
                    binaryWriter.Write(3);
                    binaryWriter.Write(0);
                    binaryWriter.Write(1);
                    binaryWriter.Write(0);
                }

                byte[] textureRawData = new byte[dataSize];
                Array.Copy(textureData, textureData.Length - dataSize, textureRawData, 0, dataSize);
                fileStream2.Write(textureRawData, 0, dataSize);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建DDS文件失败{outputPath}:{ex.Message}");
                File.WriteAllBytes(outputPath, textureData);
            }
        }

        public static byte[] InflateData(byte[] compressedData)
        {
            using MemoryStream output = new MemoryStream();
            using ZOutputStream zoutput = new ZOutputStream(output);
            zoutput.Write(compressedData, 0, compressedData.Length);
            zoutput.Flush();
            return output.ToArray();
        }

        #endregion
    }

    #region 辅助类

    namespace Applib
    {
        public class Vector3D
        {
            public float X;
            public float Y;
            public float Z;

            public static readonly Vector3D UnitX = new Vector3D(1f, 0f, 0f);
            public static readonly Vector3D UnitY = new Vector3D(0f, 1f, 0f);
            public static readonly Vector3D UnitZ = new Vector3D(0f, 0f, 1f);
            public static readonly Vector3D Zero = new Vector3D(0f, 0f, 0f);
            public static readonly Vector3D One = new Vector3D(1f, 1f, 1f);

            public Vector3D()
            {
                X = 0f;
                Y = 0f;
                Z = 0f;
            }

            public Vector3D(float xx, float yy, float zz)
            {
                X = xx;
                Y = yy;
                Z = zz;
            }

            public Vector3D(Vector3D Vec)
            {
                X = Vec.X;
                Y = Vec.Y;
                Z = Vec.Z;
            }

            public void SetVector(float xx, float yy, float zz)
            {
                X = xx;
                Y = yy;
                Z = zz;
            }

            public float DotProduct(Vector3D Vec)
            {
                return X * Vec.X + Y * Vec.Y + Z * Vec.Z;
            }

            public float Length()
            {
                return (float)Math.Sqrt(X * X + Y * Y + Z * Z);
            }

            public float AngleTo(Vector3D Vec)
            {
                float num = DotProduct(Vec);
                float num2 = Length() * Vec.Length();
                if (num2 == 0f)
                {
                    return 0f;
                }
                return (float)Math.Acos(num / num2);
            }

            public Vector3D UnitVector()
            {
                Vector3D vector3D = new Vector3D();
                float num = Length();
                if (num == 0f)
                {
                    vector3D.SetVector(0f, 0f, 0f);
                    return vector3D;
                }
                vector3D.X = X / num;
                vector3D.Y = Y / num;
                vector3D.Z = Z / num;
                return vector3D;
            }

            public bool IsCodirectionalTo(Vector3D Vec)
            {
                Vector3D vector3D = UnitVector();
                Vector3D vector3D2 = Vec.UnitVector();
                return vector3D.X == vector3D2.X && vector3D.Y == vector3D2.Y && vector3D.Z == vector3D2.Z;
            }

            public bool IsEqualTo(Vector3D? Vec)
            {
                if (Vec is null) return false;
                return X == Vec.X && Y == Vec.Y && Z == Vec.Z;
            }

            public bool IsParallelTo(Vector3D Vec)
            {
                Vector3D vector3D = UnitVector();
                Vector3D vector3D2 = Vec.UnitVector();
                return (vector3D.X == vector3D2.X && vector3D.Y == vector3D2.Y && vector3D.Z == vector3D2.Z) |
                       (vector3D.X == -vector3D2.X && vector3D.Y == -vector3D2.Y && vector3D.Z == vector3D2.Z);
            }

            public bool IsPerpendicularTo(Vector3D Vec)
            {
                double num = AngleTo(Vec);
                return num == 1.5707963267948966;
            }

            public bool IsXAxis()
            {
                return X != 0f && Y == 0f && Z == 0f;
            }

            public bool IsYAxis()
            {
                return X == 0f && Y != 0f && Z == 0f;
            }

            public bool IsZAxis()
            {
                return X == 0f && Y == 0f && Z != 0f;
            }

            public void Negate()
            {
                X = -X;
                Y = -Y;
                Z = -Z;
            }

            public Vector3D Add(Vector3D Vec)
            {
                return new Vector3D(X + Vec.X, Y + Vec.Y, Z + Vec.Z);
            }

            public Vector3D Subtract(Vector3D Vec)
            {
                return new Vector3D(X - Vec.X, Y - Vec.Y, Z - Vec.Z);
            }

            public static bool operator ==(Vector3D? a, Vector3D? b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a is null || b is null) return false;
                return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
            }

            public static bool operator !=(Vector3D? a, Vector3D? b)
            {
                return !(a == b);
            }

            public static Vector3D operator +(Vector3D a, Vector3D b)
            {
                return new Vector3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            }

            public static Vector3D operator -(Vector3D left, Vector3D right)
            {
                return new Vector3D(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
            }

            public static Vector3D Multiply(Vector3D vector, float scale)
            {
                return new Vector3D(vector.X * scale, vector.Y * scale, vector.Z * scale);
            }

            public static Vector3D Multiply(Vector3D vector, int scale)
            {
                return new Vector3D(vector.X * scale, vector.Y * scale, vector.Z * scale);
            }

            public static Vector3D Multiply(Vector3D vector, Vector3D scale)
            {
                return new Vector3D(vector.X * scale.X, vector.Y * scale.Y, vector.Z * scale.Z);
            }

            public static Vector3D Multiply(Vector3D vec, Quaternion3D q)
            {
                float num = 2f * (q.i * vec.X + q.j * vec.Y + q.k * vec.Z);
                float num2 = 2f * q.real;
                float num3 = num2 * q.real - 1f;
                float num4 = num3 * vec.X + num * q.i + num2 * (q.k * vec.Z - q.k * vec.Y);
                float num5 = num3 * vec.Y + num * q.j + num2 * (q.k * vec.X - q.i * vec.Z);
                float num6 = num3 * vec.Z + num * q.k + num2 * (q.i * vec.Y - q.j * vec.X);
                return new Vector3D(num4, num5, num6);
            }

            public static Vector3D operator *(Vector3D left, float right)
            {
                return Multiply(left, right);
            }

            public static Vector3D operator *(Vector3D left, int right)
            {
                return Multiply(left, right);
            }

            public static Vector3D operator *(float left, Vector3D right)
            {
                return Multiply(right, left);
            }

            public static Vector3D operator *(Vector3D left, Vector3D right)
            {
                return Multiply(left, right);
            }

            public static Vector3D operator *(Vector3D left, Quaternion3D right)
            {
                return Multiply(left, right);
            }

            public static Vector3D operator /(Vector3D vec, float scale)
            {
                float num = 1f / scale;
                return new Vector3D(vec.X * num, vec.Y * num, vec.Z * num);
            }

            public static Vector3D Cross(Vector3D left, Vector3D right)
            {
                return new Vector3D(
                    left.Y * right.Z - left.Z * right.Y,
                    left.Z * right.X - left.X * right.Z,
                    left.X * right.Y - left.Y * right.X
                );
            }

            public static float Dot(Vector3D left, Vector3D right)
            {
                return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
            }

            public Vector3D Normalized()
            {
                Vector3D result = new Vector3D(this);
                result.Normalize();
                return result;
            }

            public void Normalize()
            {
                float num = 1f / Length();
                X *= num;
                Y *= num;
                Z *= num;
            }

            public static Vector3D Normalize(Vector3D vec)
            {
                Vector3D result = new Vector3D(vec);
                result.Normalize();
                return result;
            }

            public float this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0: return X;
                        case 1: return Y;
                        case 2: return Z;
                        default: throw new IndexOutOfRangeException("你试图访问该索引处的向量:" + index);
                    }
                }
                set
                {
                    switch (index)
                    {
                        case 0: X = value; break;
                        case 1: Y = value; break;
                        case 2: Z = value; break;
                        default: throw new IndexOutOfRangeException("你尝试在索引位置设置该向量:" + index);
                    }
                }
            }

            public float LengthSquared
            {
                get { return X * X + Y * Y + Z * Z; }
            }

            public string WriteString()
            {
                return string.Format("{0} {1} {2}", X, Y, Z);
            }

            public override bool Equals(object? obj)
            {
                if (obj == null) return false;
                Vector3D? vector3D = obj as Vector3D;
                return vector3D != null && IsEqualTo(vector3D);
            }

            public override int GetHashCode()
            {
                int num = 17;
                num = num * 23 + X.GetHashCode();
                num = num * 23 + Y.GetHashCode();
                return num * 23 + Z.GetHashCode();
            }
        }

        public class Quaternion3D
        {
            public float real;
            public float i;
            public float j;
            public float k;

            public Vector3D xyz
            {
                get { return new Vector3D(i, j, k); }
                set
                {
                    i = value.X;
                    j = value.Y;
                    k = value.Z;
                }
            }

            public Quaternion3D()
            {
                real = 0f;
                i = 0f;
                j = 0f;
                k = 0f;
            }

            public Quaternion3D(float _real, float _i, float _j, float _k)
            {
                real = _real;
                i = _i;
                j = _j;
                k = _k;
            }

            public Quaternion3D(Vector3D vecXYZ, float _real)
            {
                real = _real;
                i = vecXYZ.X;
                j = vecXYZ.Y;
                k = vecXYZ.Z;
            }

            public Quaternion3D(Quaternion3D q)
            {
                real = q.real;
                i = q.i;
                j = q.j;
                k = q.k;
            }

            public Vector3D ToVec()
            {
                return new Vector3D(xyz);
            }

            public float Length
            {
                get { return Convert.ToSingle(Math.Sqrt(real * real + xyz.LengthSquared)); }
            }

            public void Normalize()
            {
                float num = 1f / Length;
                xyz *= num;
                real *= num;
            }

            public static Quaternion3D Invert(Quaternion3D q)
            {
                float lengthSquared = q.LengthSquared;
                if (lengthSquared != 0f)
                {
                    float num = 1f / lengthSquared;
                    return new Quaternion3D(q.xyz * -num, q.real * num);
                }
                return q;
            }

            public float LengthSquared
            {
                get { return real * real + xyz.LengthSquared; }
            }

            public static Quaternion3D Multiply(Quaternion3D left, Quaternion3D right)
            {
                return new Quaternion3D(
                    right.real * left.xyz + left.real * right.xyz + Vector3D.Cross(left.xyz, right.xyz),
                    left.real * right.real - Vector3D.Dot(left.xyz, right.xyz)
                );
            }

            public static Quaternion3D operator *(Quaternion3D left, Quaternion3D right)
            {
                return Multiply(left, right);
            }
        }

        public static class C3D
        {
            private const double FLT_EPSILON = 1E-05;

            public static float FlipFloat(float inputF)
            {
                return -inputF;
            }

            public static double deg2rad(double deg)
            {
                return deg * 0.017453292519943295;
            }

            public static double rad2deg(double rad)
            {
                return rad * 57.29577951308232;
            }

            public static float NanSafe(float val)
            {
                if (float.IsNaN(val)) return 0f;
                if (val == 0f) return 0f;
                return val;
            }

            public static Vector3D Quat2Euler_UBISOFT(Quaternion3D quat)
            {
                Vector3D vector3D = new Vector3D();
                double num = 1.5707963267948966;
                float num2 = quat.i * quat.j + quat.k * quat.real;

                if (num2 > 0.499f)
                {
                    vector3D.X = 2f * (float)Math.Atan2(quat.i, quat.real);
                    vector3D.Y = (float)num;
                    vector3D.Z = 0f;
                }
                else if (num2 < -0.499f)
                {
                    vector3D.X = -2f * (float)Math.Atan2(quat.i, quat.real);
                    vector3D.Y = (float)(-num);
                    vector3D.Z = 0f;
                }
                else
                {
                    float num3 = quat.i * quat.i;
                    float num4 = quat.j * quat.j;
                    float num5 = quat.k * quat.k;
                    vector3D.X = (float)Math.Atan2(2f * quat.j * quat.real - 2f * quat.i * quat.k, 1f - 2f * num4 - 2f * num5);
                    vector3D.Y = (float)Math.Asin(2f * num2);
                    vector3D.Z = (float)Math.Atan2(2f * quat.i * quat.real - 2f * quat.j * quat.k, 1f - 2f * num3 - 2f * num5);
                }

                if (float.IsNaN(vector3D.X)) vector3D.X = 0f;
                if (float.IsNaN(vector3D.Y)) vector3D.Y = 0f;
                if (float.IsNaN(vector3D.Z)) vector3D.Z = 0f;

                return vector3D;
            }

            public static Vector3D QuaternionToEuler(Quaternion3D quat)
            {
                Vector3D vector3D = new Vector3D();
                float num = quat.real * quat.real;
                float num2 = quat.i * quat.i;
                float num3 = quat.j * quat.j;
                float num4 = quat.k * quat.k;

                vector3D.Z = (float)rad2deg(Math.Atan2(2.0 * (quat.j * quat.k + quat.i * quat.real), -num2 - num3 + num4 + num));
                vector3D.X = (float)rad2deg(Math.Asin(-2.0 * (quat.i * quat.k - quat.j * quat.real)));
                vector3D.Y = (float)rad2deg(Math.Atan2(2.0 * (quat.i * quat.j + quat.k * quat.real), num2 - num3 - num4 + num));

                if (float.IsNaN(vector3D.X)) vector3D.X = 0f;
                if (float.IsNaN(vector3D.Y)) vector3D.Y = 0f;
                if (float.IsNaN(vector3D.Z)) vector3D.Z = 0f;

                return vector3D;
            }

            public static Vector3D QuaternionToEulerRAD(Quaternion3D quat)
            {
                Vector3D vector3D = new Vector3D();
                float num = quat.real * quat.real;
                float num2 = quat.i * quat.i;
                float num3 = quat.j * quat.j;
                float num4 = quat.k * quat.k;

                vector3D.Z = (float)Math.Atan2(2.0 * (quat.j * quat.k + quat.i * quat.real), -num2 - num3 + num4 + num);
                vector3D.X = (float)Math.Asin(-2.0 * (quat.i * quat.k - quat.j * quat.real));
                vector3D.Y = (float)Math.Atan2(2.0 * (quat.i * quat.j + quat.k * quat.real), num2 - num3 - num4 + num);

                if (float.IsNaN(vector3D.X)) vector3D.X = 0f;
                if (float.IsNaN(vector3D.Y)) vector3D.Y = 0f;
                if (float.IsNaN(vector3D.Z)) vector3D.Z = 0f;

                return vector3D;
            }

            public static Vector3D QuaternionToEulerRAD2(Quaternion3D quat)
            {
                Vector3D vector3D = new Vector3D();
                float real = quat.real;
                float i = quat.i;
                float j = quat.j;
                float k = quat.k;
                float num = i * i;
                float num2 = j * j;
                float num3 = k * k;

                vector3D.Z = (float)Math.Atan2(2.0 * (real * i + j * k), 1f - 2f * (num + num2));
                vector3D.X = (float)Math.Asin(2.0 * (real * j - k * i));
                vector3D.Y = (float)Math.Atan2(2.0 * (real * k + i * j), 1f - 2f * (num2 + num3));

                return vector3D;
            }

            public static Quaternion3D EulerAnglesToQuaternion(float yaw, float pitch, float roll)
            {
                double num = NormalizeAngle(yaw);
                double num2 = NormalizeAngle(pitch);
                double num3 = NormalizeAngle(roll);

                double num4 = Math.Cos(num);
                double num5 = Math.Cos(num2);
                double num6 = Math.Cos(num3);
                double num7 = Math.Sin(num);
                double num8 = Math.Sin(num2);
                double num9 = Math.Sin(num3);

                return new Quaternion3D
                {
                    real = (float)(num4 * num5 * num6 - num7 * num8 * num9),
                    i = (float)(num7 * num8 * num6 + num4 * num5 * num9),
                    j = (float)(num7 * num5 * num6 + num4 * num8 * num9),
                    k = (float)(num4 * num8 * num6 - num7 * num5 * num9)
                };
            }

            public static Quaternion3D DEG_EulerAnglesToQuaternion(float yaw, float pitch, float roll)
            {
                double num = deg2rad(yaw);
                double num2 = deg2rad(pitch);
                double num3 = deg2rad(roll);

                double num4 = Math.Cos(num);
                double num5 = Math.Cos(num2);
                double num6 = Math.Cos(num3);
                double num7 = Math.Sin(num);
                double num8 = Math.Sin(num2);
                double num9 = Math.Sin(num3);

                return new Quaternion3D
                {
                    real = (float)(num4 * num5 * num6 - num7 * num8 * num9),
                    i = (float)(num7 * num8 * num6 + num4 * num5 * num9),
                    j = (float)(num7 * num5 * num6 + num4 * num8 * num9),
                    k = (float)(num4 * num8 * num6 - num7 * num5 * num9)
                };
            }

            public static Quaternion3D Euler2Quat(Vector3D orientation)
            {
                Quaternion3D quaternion3D = new Quaternion3D();
                float num = 0f;
                float num2 = 0f;
                float num3 = 0f;
                float num4 = 0f;
                float num5 = 0f;
                float num6 = 0f;

                MathUtil.SinCos(ref num, ref num4, orientation.X * 0.5f);
                MathUtil.SinCos(ref num2, ref num5, orientation.Y * 0.5f);
                MathUtil.SinCos(ref num3, ref num6, orientation.Z * 0.5f);

                quaternion3D.real = num6 * num4 * num5 + num3 * num * num2;
                quaternion3D.i = -num6 * num * num5 - num3 * num4 * num2;
                quaternion3D.j = num6 * num * num2 - num3 * num5 * num4;
                quaternion3D.k = num3 * num * num5 - num6 * num4 * num2;

                return quaternion3D;
            }

            public static float NormalizeAngle(float input)
            {
                return (float)(input * 3.141592653589793 / 360.0);
            }

            public static Vector3D ToEulerAngles(Quaternion3D q)
            {
                return Eul_FromQuat(q, 0, 1, 2, 0, EulerParity.Even, EulerRepeat.No, EulerFrame.S);
            }

            private static Vector3D Eul_FromQuat(Quaternion3D q, int i, int j, int k, int h, EulerParity parity, EulerRepeat repeat, EulerFrame frame)
            {
                double[,] array = new double[4, 4];
                double num = q.i * q.i + q.j * q.j + q.k * q.k + q.real * q.real;
                double num2 = num > 0.0 ? 2.0 / num : 0.0;

                double num3 = q.i * num2;
                double num4 = q.j * num2;
                double num5 = q.k * num2;
                double num6 = q.real * num3;
                double num7 = q.real * num4;
                double num8 = q.real * num5;
                double num9 = q.i * num3;
                double num10 = q.i * num4;
                double num11 = q.i * num5;
                double num12 = q.j * num4;
                double num13 = q.j * num5;
                double num14 = q.k * num5;

                array[0, 0] = 1.0 - (num12 + num14);
                array[0, 1] = num10 - num8;
                array[0, 2] = num11 + num7;
                array[1, 0] = num10 + num8;
                array[1, 1] = 1.0 - (num9 + num14);
                array[1, 2] = num13 - num6;
                array[2, 0] = num11 - num7;
                array[2, 1] = num13 + num6;
                array[2, 2] = 1.0 - (num9 + num12);
                array[3, 3] = 1.0;

                return Eul_FromHMatrix(array, i, j, k, h, parity, repeat, frame);
            }

            private static Vector3D Eul_FromHMatrix(double[,] M, int i, int j, int k, int h, EulerParity parity, EulerRepeat repeat, EulerFrame frame)
            {
                Vector3D vector3D = new Vector3D();

                if (repeat == EulerRepeat.Yes)
                {
                    double num = Math.Sqrt(M[i, j] * M[i, j] + M[i, k] * M[i, k]);
                    if (num > 0.00016)
                    {
                        vector3D.X = (float)Math.Atan2(M[i, j], M[i, k]);
                        vector3D.Y = (float)Math.Atan2(num, M[i, i]);
                        vector3D.Z = (float)Math.Atan2(M[j, i], -M[k, i]);
                    }
                    else
                    {
                        vector3D.X = (float)Math.Atan2(-M[j, k], M[j, j]);
                        vector3D.Y = (float)Math.Atan2(num, M[i, i]);
                        vector3D.Z = 0f;
                    }
                }
                else
                {
                    double num2 = Math.Sqrt(M[i, i] * M[i, i] + M[j, i] * M[j, i]);
                    if (num2 > 0.00016)
                    {
                        vector3D.X = (float)Math.Atan2(M[k, j], M[k, k]);
                        vector3D.Y = (float)Math.Atan2(-M[k, i], num2);
                        vector3D.Z = (float)Math.Atan2(M[j, i], M[i, i]);
                    }
                    else
                    {
                        vector3D.X = (float)Math.Atan2(-M[j, k], M[j, j]);
                        vector3D.Y = (float)Math.Atan2(-M[k, i], num2);
                        vector3D.Z = 0f;
                    }
                }

                if (parity == EulerParity.Odd)
                {
                    vector3D.X = -vector3D.X;
                    vector3D.Y = -vector3D.Y;
                    vector3D.Z = -vector3D.Z;
                }

                if (frame == EulerFrame.R)
                {
                    float temp = vector3D.X;
                    vector3D.X = vector3D.Z;
                    vector3D.Z = temp;
                }

                return vector3D;
            }

            public enum EulerParity { Even, Odd }
            public enum EulerRepeat { No, Yes }
            public enum EulerFrame { S, R }

            public class MathUtil
            {
                public static float kPi = 3.1415927f;
                public static float k2Pi = kPi * 2f;
                public static float kPiOver2 = kPi / 2f;
                public static float k1OverPi = 1f / kPi;
                public static float k1Over2Pi = 1f / k2Pi;
                public static float kPiOver180 = kPi / 180f;
                public static float k180OverPi = 180f / kPi;
                public static Vector3D kZeroVector = new Vector3D(0f, 0f, 0f);

                public static void SinCos(ref float returnSin, ref float returnCos, float theta)
                {
                    returnSin = (float)Math.Sin(deg2rad(theta));
                    returnCos = (float)Math.Cos(deg2rad(theta));
                }
            }
        }
    }
    #endregion
}