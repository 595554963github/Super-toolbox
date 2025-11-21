using System.Text;

namespace super_toolbox
{
    public class Sdat_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly Dictionary<string, long> FileNameStartAddresses = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["BGM.sdat"] = 0x000001c8,
            ["BtlVoice.sdat"] = 0x0000846c,
            ["DataBase2.sdat"] = 0x00000750,
            ["Exi_UT2.sdat"] = 0x00000560,
            ["Model.sdat"] = 0x00000670,
            ["PatternFade.sdat"] = 0x00000178,
            ["SE.sdat"] = 0x00002578,
            ["SE_OnMemory.sdat"] = 0x00000068,
            ["Voice.sdat"] = 0x0001afdc,
            ["DataBase.sdat"] = 0x0000084c,
            ["Grp1.sdat"] = 0x00003d54,
            ["Grp2.sdat"] = 0x00000fac,
            ["Grp3.sdat"] = 0x000008cc,
            ["ScriptEvent.sdat"] = 0x00000ae4
        };

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

            var sdatFiles = Directory.EnumerateFiles(directoryPath, "*.sdat", SearchOption.AllDirectories).ToList();

            TotalFilesToExtract = sdatFiles.Count;

            foreach (var sdatPath in sdatFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);

                string sdatFileName = Path.GetFileNameWithoutExtension(sdatPath);
                string sdatDirectory = Path.GetDirectoryName(sdatPath)!;
                string outputDir = Path.Combine(sdatDirectory, sdatFileName);

                ExtractionProgress?.Invoke(this, $"正在处理SDAT文件:{Path.GetFileName(sdatPath)}，输出至:{outputDir}");

                try
                {
                    Directory.CreateDirectory(outputDir);
                    await UnpackSdatAsync(sdatPath, outputDir, extractedFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件{sdatPath}时出错:{ex.Message}");
                    OnExtractionFailed($"处理文件{sdatPath}时出错:{ex.Message}");
                }
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到可提取的文件");
            }

            OnExtractionCompleted();
        }

        private async Task UnpackSdatAsync(string sdatPath, string outputDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using var fs = new FileStream(sdatPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var br = new BinaryReader(fs, Encoding.UTF8);

                    await UnpackSdatInternalAsync(fs, br, sdatPath, outputDir, extractedFiles, cancellationToken);
                    return;
                }
                catch (IOException) when (i < 9)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }

            throw new InvalidDataException("文件被其他进程占用，无法访问");
        }

        private async Task UnpackSdatInternalAsync(FileStream fs, BinaryReader br, string sdatPath, string outputDir,
                                                 List<string> extractedFiles, CancellationToken cancellationToken)
        {
            var magic = br.ReadBytes(8);
            if (Encoding.ASCII.GetString(magic).Trim() != "Filename")
            {
                ExtractionError?.Invoke(this, $"文件{sdatPath}不是有效的SDAT文件(缺少Filename签名)");
                throw new InvalidDataException("不是有效的SDAT文件(缺少Filename签名)");
            }

            var unk1 = br.ReadUInt32();
            var packHeaderOffset = br.ReadUInt32();
            var packHeaderAlign = (packHeaderOffset % 8 != 0) ? (8 - packHeaderOffset % 8) : 0;

            ExtractionProgress?.Invoke(this, $"Pack头偏移:0x{packHeaderOffset:X}, 对齐:{packHeaderAlign}");

            string fileName = Path.GetFileName(sdatPath);
            if (!FileNameStartAddresses.TryGetValue(fileName, out long fileNameStartAddress))
            {
                ExtractionError?.Invoke(this, $"未找到{fileName}的预定义起始地址");
                throw new InvalidDataException($"未找到{fileName}的预定义起始地址");
            }

            ExtractionProgress?.Invoke(this, $"文件名区域起始地址:0x{fileNameStartAddress:X}");

            long packHeaderPosition = (long)packHeaderOffset + (long)packHeaderAlign;
            fs.Seek(packHeaderPosition, SeekOrigin.Begin);

            var packSign = br.ReadBytes(4);
            if (Encoding.ASCII.GetString(packSign).Trim() != "Pack")
            {
                ExtractionError?.Invoke(this, "Pack头签名错误");
                throw new InvalidDataException("Pack头签名错误");
            }

            var unk2 = br.ReadUInt32();
            var unk3 = br.ReadUInt32();
            var packHeaderLength = br.ReadUInt32();
            var fileCount = br.ReadUInt32();

            ExtractionProgress?.Invoke(this, $"文件数量:{fileCount}");

            var fileEntries = new List<FileEntry>();
            for (int i = 0; i < fileCount; i++)
            {
                ThrowIfCancellationRequested(cancellationToken);

                var entry = new FileEntry
                {
                    Index = i,
                    FileDataOffset = br.ReadUInt32(),
                    FileSize = br.ReadUInt32()
                };
                fileEntries.Add(entry);
            }

            br.ReadBytes((int)(fileCount * 4));

            var fileNames = ReadFileNamesFromRegion(br, fileNameStartAddress, packHeaderPosition, (int)fileCount);

            for (int i = 0; i < fileCount; i++)
            {
                ThrowIfCancellationRequested(cancellationToken);

                var entry = fileEntries[i];
                string currentFileName = i < fileNames.Count ? fileNames[i] : $"file_{i:00000}";

                if (entry.FileDataOffset >= fs.Length)
                {
                    ExtractionProgress?.Invoke(this, $"跳过无效文件{i}偏移超出文件长度");
                    continue;
                }

                if (entry.FileSize > int.MaxValue)
                {
                    ExtractionError?.Invoke(this, $"文件{currentFileName}大小超出限制:{entry.FileSize}字节");
                    throw new InvalidDataException($"文件{currentFileName}大小超出限制:{entry.FileSize}字节");
                }

                int actualFileSize = (int)entry.FileSize;
                if ((long)entry.FileDataOffset + (long)entry.FileSize > fs.Length)
                    actualFileSize = (int)(fs.Length - (long)entry.FileDataOffset);

                fs.Seek((long)entry.FileDataOffset, SeekOrigin.Begin);
                var fileData = br.ReadBytes(actualFileSize);

                string safeFileName = SanitizeFileName(currentFileName, i);
                var filePath = Path.Combine(outputDir ?? string.Empty, safeFileName);
                var dir = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                try
                {
                    await File.WriteAllBytesAsync(filePath, fileData, cancellationToken);
                    extractedFiles.Add(filePath);
                    OnFileExtracted(filePath);
                    ExtractionProgress?.Invoke(this, $"{safeFileName}已保存!({fileData.Length}字节)");
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"保存文件{safeFileName}时出错:{ex.Message}");

                    string fallbackFileName = $"file_{i:00000}{Path.GetExtension(safeFileName)}";
                    var fallbackPath = Path.Combine(outputDir ?? string.Empty, fallbackFileName);

                    await File.WriteAllBytesAsync(fallbackPath, fileData, cancellationToken);
                    extractedFiles.Add(fallbackPath);
                    OnFileExtracted(fallbackPath);
                    ExtractionProgress?.Invoke(this, $"{fallbackFileName}已保存!({fileData.Length}字节)");
                }
            }
        }

        private static string SanitizeFileName(string fileName, int index)
        {
            if (fileName.StartsWith("\\"))
                fileName = fileName.Substring(1);

            if (string.IsNullOrEmpty(fileName) || ContainsInvalidChars(fileName))
            {
                string baseName = $"tex_{index:00000}";
                string extension = ".tex";

                if (!string.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        string ext = Path.GetExtension(fileName);
                        if (!string.IsNullOrEmpty(ext) && ext.Length <= 5)
                            extension = ext;
                    }
                    catch
                    {
                    }
                }

                return baseName + extension;
            }

            return fileName;
        }

        private static bool ContainsInvalidChars(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return fileName.Any(c => invalidChars.Contains(c)) ||
                   fileName.All(c => c == '?') ||
                   string.IsNullOrWhiteSpace(fileName);
        }

        private static List<string> ReadFileNamesFromRegion(BinaryReader br, long startAddress, long endAddress, int expectedCount)
        {
            var fileNames = new List<string>();
            long originalPos = br.BaseStream.Position;

            try
            {
                br.BaseStream.Seek(startAddress, SeekOrigin.Begin);

                while (br.BaseStream.Position < endAddress && fileNames.Count < expectedCount)
                {
                    string fileName = ReadNullTerminatedString(br);

                    if (!string.IsNullOrEmpty(fileName))
                    {
                        fileNames.Add(fileName);
                    }

                    if (br.BaseStream.Position < endAddress && br.BaseStream.Position < br.BaseStream.Length)
                    {
                        byte nextByte = br.ReadByte();
                        if (nextByte != 0)
                        {
                            br.BaseStream.Seek(-1, SeekOrigin.Current);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                br.BaseStream.Seek(originalPos, SeekOrigin.Begin);
            }

            while (fileNames.Count < expectedCount)
            {
                fileNames.Add($"file_{fileNames.Count:00000}.tex");
            }

            return fileNames;
        }

        private static string ReadNullTerminatedString(BinaryReader br)
        {
            var bytes = new List<byte>();
            byte b;

            int maxLength = 260;
            int bytesRead = 0;

            while (bytesRead < maxLength)
            {
                if (br.BaseStream.Position >= br.BaseStream.Length)
                    break;

                b = br.ReadByte();
                bytesRead++;

                if (b == 0)
                    break;

                bytes.Add(b);
            }

            string result = Encoding.ASCII.GetString(bytes.ToArray()).Trim();
            return result;
        }
    }

    internal class FileEntry
    {
        public int Index { get; set; }
        public uint FileDataOffset { get; set; }
        public uint FileSize { get; set; }
    }
}