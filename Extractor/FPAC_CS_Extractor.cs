using System.Text;

namespace super_toolbox
{
    public class FPAC_CS_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] FPAC_SIGNATURE = { 0x46, 0x50, 0x41, 0x43 };
        private static readonly byte[] SH_HCA_HEADER = { 0x53, 0x48, 0x03, 0x05, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] HXET_HEADER = { 0x48, 0x58, 0x45, 0x54 }; // "HXET"
        private static readonly byte[] SH_HEADER = { 0x53, 0x48 }; // "SH"
        private static readonly byte[] RIFF_HEADER = { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
        private static readonly byte[] HCA_HEADER = { 0x48, 0x43, 0x41, 0x00 }; // "HCA"
        private static readonly byte[] VAGP_HEADER = { 0x56, 0x41, 0x47, 0x70 }; // "VAGp"

        private const int MIN_PAC_FILE_SIZE = 1024;

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
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

        private static bool StartsWith(byte[] data, byte[] pattern)
        {
            if (data.Length < pattern.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (data[i] != pattern[i]) return false;
            }
            return true;
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

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);

            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                    string extension = Path.GetExtension(filePath).ToLower();

                    if (extension == ".pac")
                    {
                        if (content.Length >= 4 && content[0] == 0x46 && content[1] == 0x50 && content[2] == 0x41 && content[3] == 0x43)
                        {
                            ExtractFromPacFile(filePath, content);
                        }
                    }
                    else if (extension == ".psarc")
                    {
                        ExtractFromPsarc(content, filePath);
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (IOException e)
                {
                    ExtractionError?.Invoke(this, $"读取文件{filePath}时出错:{e.Message}");
                    OnExtractionFailed($"读取文件{filePath}时出错:{e.Message}");
                }
            }

            if (ExtractedFileCount > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{ExtractedFileCount}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到相关文件");
            }
            OnExtractionCompleted();
        }

        private void ExtractFromPacFile(string pacFilePath, byte[] content)
        {
            string outputDir = Path.Combine(Path.GetDirectoryName(pacFilePath) ?? Directory.GetCurrentDirectory(),
                Path.GetFileNameWithoutExtension(pacFilePath));

            try
            {
                using (var fs = new FileStream(pacFilePath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    byte[] signature = br.ReadBytes(4);
                    if (!signature.SequenceEqual(FPAC_SIGNATURE))
                    {
                        ExtractionError?.Invoke(this, $"{Path.GetFileName(pacFilePath)}不是有效的FPAC文件");
                        return;
                    }

                    long tmpPos = fs.Position;
                    fs.Seek(8, SeekOrigin.Current);
                    int files = br.ReadInt32();
                    fs.Position = tmpPos;

                    int baseOffset = br.ReadInt32();
                    int totalSize = br.ReadInt32();
                    int fileCount = br.ReadInt32();
                    int dummy1 = br.ReadInt32();
                    int nameLength = br.ReadInt32();
                    int dummy2 = br.ReadInt32();
                    int dummy3 = br.ReadInt32();

                    bool hasValidFile = false;
                    bool outputDirCreated = false;

                    Encoding latin1 = Encoding.GetEncoding(28591);

                    for (int i = 0; i < fileCount; i++)
                    {
                        byte[] nameBytes = br.ReadBytes(nameLength);
                        string fileName = latin1.GetString(nameBytes).TrimEnd('\0');
                        int fileId = br.ReadInt32();
                        int offset = br.ReadInt32();
                        int size = br.ReadInt32();

                        long currentPos = fs.Position;
                        int zero = br.ReadInt32();
                        if (zero != 0)
                        {
                            fs.Position = currentPos;
                        }

                        long alignedPos = ((fs.Position + 15) / 16) * 16;
                        fs.Position = alignedPos;

                        int actualOffset = baseOffset + offset;

                        if (actualOffset + size > fs.Length || actualOffset < 0 || size <= 0)
                        {
                            continue;
                        }

                        if (size < MIN_PAC_FILE_SIZE)
                        {
                            ExtractionProgress?.Invoke(this, $"跳过小文件:{fileName} ({size}字节)");
                            continue;
                        }

                        fs.Position = actualOffset;
                        byte[] fileData = br.ReadBytes(size);

                        (byte[] processedData, string newExtension) = ProcessFileHeader(fileData, fileName);

                        string newFileName = Path.ChangeExtension(fileName, newExtension);
                        string outputPath = Path.Combine(outputDir, newFileName);

                        if (!hasValidFile)
                        {
                            hasValidFile = true;
                            Directory.CreateDirectory(outputDir);
                            outputDirCreated = true;
                        }

                        string? directory = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.WriteAllBytes(outputPath, processedData);
                        OnFileExtracted(outputPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{newFileName} ({size}字节)");

                        fs.Position = alignedPos;
                    }
                    if (!hasValidFile && outputDirCreated && Directory.Exists(outputDir))
                    {
                        try
                        {
                            Directory.Delete(outputDir, true);
                            ExtractionProgress?.Invoke(this, "没有提取到有效文件，已删除输出目录");
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解包{Path.GetFileName(pacFilePath)}时出错:{ex.Message}");
            }
        }

        private (byte[] data, string extension) ProcessFileHeader(byte[] fileData, string originalFileName)
        {
            string extension = Path.GetExtension(originalFileName).ToLower();

            if (fileData.Length >= 12 && StartsWith(fileData, HXET_HEADER))
            {
                byte[] newData = new byte[fileData.Length - 12];
                Array.Copy(fileData, 12, newData, 0, newData.Length);
                return (newData, ".gxt");
            }

            if (fileData.Length >= 16 && StartsWith(fileData, SH_HEADER))
            {
                byte[] newData = new byte[fileData.Length - 16];
                Array.Copy(fileData, 16, newData, 0, newData.Length);

                if (newData.Length >= 4)
                {
                    if (StartsWith(newData, HCA_HEADER))
                    {
                        return (newData, ".hca");
                    }
                    else if (StartsWith(newData, RIFF_HEADER))
                    {
                        return (newData, ".at9");
                    }
                    else if (StartsWith(newData, VAGP_HEADER))
                    {
                        return (newData, ".vag");
                    }
                }

                return (newData, extension);
            }

            return (fileData, extension);
        }

        private void ExtractFromPsarc(byte[] content, string sourceFilePath)
        {
            int index = 0;
            int innerCount = 1;
            string outputDir = Path.Combine(Path.GetDirectoryName(sourceFilePath) ?? Directory.GetCurrentDirectory(),
                Path.GetFileNameWithoutExtension(sourceFilePath));
            Directory.CreateDirectory(outputDir);
            int firstFpacIndex = IndexOf(content, FPAC_SIGNATURE, 0);
            int firstShHcaIndex = IndexOf(content, SH_HCA_HEADER, 0);
            int startIndex = 0;
            if (firstFpacIndex != -1 && (firstShHcaIndex == -1 || firstFpacIndex < firstShHcaIndex))
            {
                startIndex = firstFpacIndex;
            }
            else if (firstShHcaIndex != -1)
            {
                startIndex = firstShHcaIndex;
            }
            else
            {
                ExtractionError?.Invoke(this, "未找到有效的文件头");
                return;
            }
            index = startIndex;
            while (index < content.Length)
            {
                bool isFpac = index + 4 <= content.Length &&
                              content[index] == 0x46 &&
                              content[index + 1] == 0x50 &&
                              content[index + 2] == 0x41 &&
                              content[index + 3] == 0x43;
                bool isShHca = index + 8 <= content.Length &&
                               StartsWith(content.Skip(index).Take(8).ToArray(), SH_HCA_HEADER);
                if (!isFpac && !isShHca)
                {
                    index++;
                    continue;
                }
                int nextFpacIndex = IndexOf(content, FPAC_SIGNATURE, index + 1);
                int nextShHcaIndex = IndexOf(content, SH_HCA_HEADER, index + 1);
                int nextHeaderIndex = content.Length;
                if (nextFpacIndex != -1 && (nextShHcaIndex == -1 || nextFpacIndex < nextShHcaIndex))
                {
                    nextHeaderIndex = nextFpacIndex;
                }
                else if (nextShHcaIndex != -1)
                {
                    nextHeaderIndex = nextShHcaIndex;
                }
                int length = nextHeaderIndex - index;
                if (length <= 0)
                {
                    index++;
                    continue;
                }
                byte[] fileData = new byte[length];
                Array.Copy(content, index, fileData, 0, length);
                string baseFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
                string outputFileName;
                string outputFilePath;
                if (isFpac)
                {
                    outputFileName = $"{baseFileName}{innerCount}.pac";
                    outputFilePath = Path.Combine(outputDir, outputFileName);
                    if (File.Exists(outputFilePath))
                    {
                        outputFilePath = GetUniqueFilePath(outputFilePath);
                    }

                    try
                    {
                        File.WriteAllBytes(outputFilePath, fileData);
                        OnFileExtracted(outputFilePath);
                        ExtractionProgress?.Invoke(this, $"已提取PAC文件:{Path.GetFileName(outputFilePath)}");
                    }
                    catch (IOException e)
                    {
                        ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{e.Message}");
                    }
                }
                else if (isShHca)
                {
                    if (fileData.Length >= 16)
                    {
                        byte[] hcaData = new byte[fileData.Length - 16];
                        Array.Copy(fileData, 16, hcaData, 0, hcaData.Length);
                        outputFileName = $"{baseFileName}{innerCount}.hca";
                        outputFilePath = Path.Combine(outputDir, outputFileName);
                        if (File.Exists(outputFilePath))
                        {
                            outputFilePath = GetUniqueFilePath(outputFilePath);
                        }

                        try
                        {
                            File.WriteAllBytes(outputFilePath, hcaData);
                            OnFileExtracted(outputFilePath);
                            ExtractionProgress?.Invoke(this, $"已提取HCA文件:{Path.GetFileName(outputFilePath)}");
                        }
                        catch (IOException e)
                        {
                            ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{e.Message}");
                        }
                    }
                }

                index = nextHeaderIndex;
                innerCount++;
            }
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
    }
}