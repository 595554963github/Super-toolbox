using System.Text;

namespace super_toolbox
{
    public class DoubleDragonNeon_PakExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const int PADDING = 8;
        private bool LIST_ONLY = false;
        private bool CLIP_PATH = false;

        private const int SIG_LINK = 0;
        private const int SIG_DATA = 1;

        private byte[] MakeSig(int sigType)
        {
            string[] signatures = {
                "FILELINK",
                "MANAGEDFILE_DATABLOCK_USED_IN_ENGINE_________________________END"
            };
            return Encoding.ASCII.GetBytes(signatures[sigType]);
        }

        private void WriteToFile(string path, byte[] data)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(path, data);
        }

        private void LogPaddingInfo(string pakFilePath, int paddingCount)
        {
            if (paddingCount <= 0) return;

            string logPath = Path.Combine(Path.GetDirectoryName(pakFilePath)!, "padding_info.txt");
            string logEntry = $"文件: {Path.GetFileName(pakFilePath)} | 填充大小:{paddingCount}字节| 记录时间:{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}";

            File.AppendAllText(logPath, logEntry);
            ExtractionProgress?.Invoke(this, $"已记录填充信息到:{logPath}");
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                try
                {
                    ExtractionStarted?.Invoke(this, "开始解包双截龙彩虹PAK文件");

                    string[] pakFiles = Directory.GetFiles(directoryPath, "*.pak", SearchOption.AllDirectories);

                    int totalExtractedFiles = 0;
                    foreach (string filename in pakFiles)
                    {
                        try
                        {
                            int fileCount = CountFilesInPak(filename);
                            totalExtractedFiles += fileCount;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"统计文件{filename}时出错:{ex.Message}");
                        }
                    }

                    TotalFilesToExtract = totalExtractedFiles;

                    if (TotalFilesToExtract == 0)
                    {
                        ExtractionError?.Invoke(this, "未找到任何.pak文件或pak文件中没有可提取的文件");
                        OnExtractionFailed("未找到任何.pak文件或pak文件中没有可提取的文件");
                        return;
                    }

                    int processedPakFiles = 0;
                    foreach (string filename in pakFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        try
                        {
                            ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filename)}");
                            string pakFileDirectory = Path.GetDirectoryName(filename)!;
                            int paddingCount = Unpack(filename, pakFileDirectory, null);
                            processedPakFiles++;
                            if (paddingCount > 0)
                            {
                                ExtractionProgress?.Invoke(this, $"检测到填充:{paddingCount}bytes（文件:{Path.GetFileName(filename)}）");
                                LogPaddingInfo(filename, paddingCount);
                            }

                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理文件{filename}时出错:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"解包完成，共处理{processedPakFiles}个PAK文件，提取了{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"解包过程中出错:{ex.Message}");
                    OnExtractionFailed($"解包过程中出错:{ex.Message}");
                }
            });
        }

        private int CountFilesInPak(string filename)
        {
            if (!File.Exists(filename))
            {
                return 0;
            }

            int fileCount = 0;

            using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                uint loc = reader.ReadUInt32();
                uint numFiles = reader.ReadUInt32();
                long offset = 8;

                for (int i = 0; i < numFiles; i++)
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    byte[] sig = reader.ReadBytes(MakeSig(SIG_LINK).Length);
                    byte[] expectedSig = MakeSig(SIG_LINK);

                    if (!sig.SequenceEqual(expectedSig))
                    {
                        return fileCount;
                    }

                    reader.ReadUInt32(); // at
                    reader.ReadUInt32(); // size

                    List<byte> nameBytes = new List<byte>();
                    byte currentByte;
                    while ((currentByte = reader.ReadByte()) != 0)
                    {
                        nameBytes.Add(currentByte);
                    }

                    offset = fs.Position;
                    while (offset % PADDING != 0)
                    {
                        offset++;
                    }

                    fileCount++;
                }
            }

            return fileCount;
        }

        private int Unpack(string filename, string? outputRootDir, List<string>? filesFilter)
        {
            if (!File.Exists(filename))
            {
                ExtractionError?.Invoke(this, $"文件不存在:{filename}");
                return -1;
            }

            string pakFileName = Path.GetFileNameWithoutExtension(filename);
            outputRootDir ??= Path.GetDirectoryName(filename)!;
            string outputDir = Path.Combine(outputRootDir, pakFileName);

            int originalPaddingCount = 0;
            int unpacked = 0;

            try
            {
                using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    long fileSize = fs.Length;
                    uint loc = reader.ReadUInt32();
                    uint numFiles = reader.ReadUInt32();
                    long offset = 8;

                    for (int i = 0; i < numFiles; i++)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);
                        byte[] sig = reader.ReadBytes(MakeSig(SIG_LINK).Length);
                        byte[] expectedSig = MakeSig(SIG_LINK);

                        if (!sig.SequenceEqual(expectedSig))
                        {
                            ExtractionProgress?.Invoke(this, $"警告:在偏移量{offset}处签名不匹配，但继续处理剩余文件");
                            break;
                        }

                        uint at = reader.ReadUInt32();
                        uint size = reader.ReadUInt32();

                        List<byte> nameBytes = new List<byte>();
                        byte currentByte;
                        while ((currentByte = reader.ReadByte()) != 0)
                        {
                            nameBytes.Add(currentByte);
                        }
                        string name = Encoding.ASCII.GetString(nameBytes.ToArray());

                        if (!CLIP_PATH)
                        {
                            name = name.Replace(":", Path.DirectorySeparatorChar.ToString());
                        }

                        string[] pathParts = name.Split(Path.DirectorySeparatorChar);
                        string outputDirName = Path.GetFileName(outputDir);
                        if (pathParts.Length > 0 && pathParts[0] == outputDirName)
                        {
                            name = string.Join(Path.DirectorySeparatorChar.ToString(), pathParts.Skip(1));
                        }

                        offset = fs.Position;
                        while (offset % PADDING != 0)
                        {
                            offset++;
                        }

                        int sigDataLen = MakeSig(SIG_DATA).Length;
                        at += loc + (uint)sigDataLen;

                        if (at >= fileSize || at + size > fileSize)
                        {
                            ExtractionError?.Invoke(this, $"文件数据位置无效:{name} (位置:{at}, 大小:{size}, 文件大小:{fileSize})");
                            continue;
                        }

                        fs.Seek(at, SeekOrigin.Begin);
                        byte[] data = reader.ReadBytes((int)size);

                        if (LIST_ONLY)
                        {
                            string logMessage = $"{at,10} {size,10} {name}";
                            ExtractionProgress?.Invoke(this, logMessage);
                            continue;
                        }

                        string outPath = CLIP_PATH ? name : Path.Combine(outputDir, name);
                        if (filesFilter == null || filesFilter.Contains(name))
                        {
                            WriteToFile(outPath, data);
                            unpacked++;
                            OnFileExtracted(name);
                            ExtractionProgress?.Invoke(this, $"已提取:{outPath}");
                        }
                    }

                    try
                    {
                        long currentPosition = fs.Position;
                        int paddingCount = 0;

                        while (currentPosition < fileSize)
                        {
                            fs.Seek(currentPosition, SeekOrigin.Begin);
                            byte byteRead = reader.ReadByte();
                            if (byteRead == 0x3F)
                            {
                                paddingCount++;
                                currentPosition++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        originalPaddingCount = paddingCount;
                    }
                    catch (EndOfStreamException)
                    {
                        originalPaddingCount = 0;
                    }
                    catch (Exception ex)
                    {
                        ExtractionProgress?.Invoke(this, $"读取填充数据时出现小问题:{ex.Message}");
                        originalPaddingCount = 0;
                    }
                }

                if (!LIST_ONLY)
                {
                    ExtractionProgress?.Invoke(this, $"已解包{unpacked}个文件到{outputDir}");
                    return originalPaddingCount;
                }
                return 0;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解包文件{filename} 时出错:{ex.Message}");
                return -1;
            }
        }
    }
}