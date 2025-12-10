using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class FilenameExtractor : BaseExtractor
    {
        private static readonly byte[] at3Header = { 0x52, 0x49, 0x46, 0x46 };
        private static readonly byte[] envHeader = { 0x52, 0x52, 0x52, 0x52 };
        private static readonly byte[] audioBlock = { 0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74 };
        private static readonly byte[] fontPattern = { 0x81, 0x01, 0x40, 0x00 };
        private static readonly byte[] texturePattern = { 0x54, 0x65, 0x78, 0x74, 0x75, 0x72, 0x65 };
        private static readonly byte[] pngEndPattern = { 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };
        private static readonly byte[] packPattern = Encoding.ASCII.GetBytes("Pack");
        private static readonly byte[] maPattern = { 0x4D, 0x41, 0x42, 0x30 };

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            int totalPckFilesProcessed = 0;

            try
            {
                var pckFiles = Directory.GetFiles(directoryPath, "*.pck", SearchOption.AllDirectories)
                    .Where(file => !file.Contains("Extracted"))
                    .ToList();
                TotalFilesToExtract = pckFiles.Count;

                if (TotalFilesToExtract == 0)
                {
                    ExtractionError?.Invoke(this, "未找到任何PCK文件");
                    OnExtractionFailed("未找到任何PCK文件");
                    return;
                }

                string extractedDir = Path.Combine(directoryPath, "Extracted");
                Directory.CreateDirectory(extractedDir);

                ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}，共找到{TotalFilesToExtract}个PCK文件");

                foreach (var pckFile in pckFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ExtractionProgress?.Invoke(this, $"正在处理PCK文件:{Path.GetFileName(pckFile)}");

                    try
                    {
                        await ProcessPckFileAsync(pckFile, extractedDir, cancellationToken);
                        totalPckFilesProcessed++;
                        ExtractionProgress?.Invoke(this, $"{Path.GetFileName(pckFile)}处理完成");
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{pckFile}时发生错误: {ex.Message}");
                        OnExtractionFailed($"处理文件{pckFile}时发生错误:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成，共处理{totalPckFilesProcessed}个PCK文件");
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
                ExtractionError?.Invoke(this, $"提取过程中发生错误:{ex.Message}");
                OnExtractionFailed($"提取过程中发生错误:{ex.Message}");
            }
        }

        private async Task ProcessPckFileAsync(string pckFilePath, string extractedDir, CancellationToken cancellationToken)
        {
            byte[] content = await File.ReadAllBytesAsync(pckFilePath, cancellationToken);
            string fileName = Path.GetFileNameWithoutExtension(pckFilePath);

            string outputDir = Path.Combine(extractedDir, fileName);
            Directory.CreateDirectory(outputDir);

            int packPos = IndexOf(content, packPattern, 0);
            Console.WriteLine($"文件{fileName}:Pack标记位置={(packPos == -1 ? "未找到" : "0x" + packPos.ToString("X"))}");

            if (fileName.StartsWith("MA", StringComparison.OrdinalIgnoreCase))
            {
                ExtractTexAndMaFiles(content, outputDir);
            }
            else if (fileName.Contains("Font", StringComparison.OrdinalIgnoreCase) ||
                     fileName.Equals("PatternFade", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractFontOrPatternFadeFilesAsync(fileName, content, outputDir, cancellationToken);
            }
            else
            {
                await ExtractOtherFilesAsync(fileName, content, outputDir, cancellationToken);
            }

            if (Directory.GetFiles(outputDir).Length == 0)
            {
                Directory.Delete(outputDir);
            }
        }

        private void ExtractTexAndMaFiles(byte[] content, string outputDir)
        {
            int maStart = IndexOf(content, maPattern, 0);
            if (maStart == -1)
            {
                throw new Exception("未找到.MA文件起始标记");
            }

            int texStart = IndexOf(content, fontPattern, maStart);
            if (texStart == -1)
            {
                throw new Exception("未找到.tex文件起始标记");
            }

            byte[] maData = new byte[texStart - maStart];
            Array.Copy(content, maStart, maData, 0, maData.Length);
            maData = TrimTrailingNulls(maData);
            string maFilePath = SaveFile(outputDir, "file.MA", maData);
            OnFileExtracted(maFilePath);

            byte[] texData = new byte[content.Length - texStart];
            Array.Copy(content, texStart, texData, 0, texData.Length);
            texData = TrimTrailingNulls(texData);
            string texFilePath = SaveFile(outputDir, "file.tex", texData);
            OnFileExtracted(texFilePath);
        }

        private async Task ExtractFontOrPatternFadeFilesAsync(string fileName, byte[] content, string outputDir, CancellationToken cancellationToken)
        {
            if (fileName.Contains("Font", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractFontFilesAsync(content, fileName, outputDir, cancellationToken);
            }
            else if (fileName.Equals("PatternFade", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractPatternFadeFilesAsync(content, fileName, outputDir, cancellationToken);
            }
        }

        private async Task ExtractOtherFilesAsync(string fileName, byte[] content, string outputDir, CancellationToken cancellationToken)
        {
            var filenames = ExtractValidFilenames(content);
            int filenameIndex = 0;

            foreach (var at3Data in ExtractAt3Data(content))
            {
                ThrowIfCancellationRequested(cancellationToken);

                if (filenameIndex < filenames.Count)
                {
                    string outputPath = Path.Combine(outputDir, filenames[filenameIndex]);
                    await File.WriteAllBytesAsync(outputPath, at3Data, cancellationToken);
                    OnFileExtracted(outputPath);
                    filenameIndex++;
                }
            }

            foreach (var envData in ExtractEnvData(content))
            {
                ThrowIfCancellationRequested(cancellationToken);

                if (filenameIndex < filenames.Count)
                {
                    string outputPath = Path.Combine(outputDir, filenames[filenameIndex]);
                    await File.WriteAllBytesAsync(outputPath, envData, cancellationToken);
                    OnFileExtracted(outputPath);
                    filenameIndex++;
                }
            }
        }

        private async Task ExtractFontFilesAsync(byte[] fileContent, string baseFilename, string outputDir, CancellationToken cancellationToken)
        {
            int index = 0;
            int fileCount = 0;

            while ((index = IndexOf(fileContent, fontPattern, index)) != -1)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int nextPattern = IndexOf(fileContent, fontPattern, index + 1);
                int endIndex = nextPattern == -1 ? fileContent.Length : nextPattern;

                int dataLength = endIndex - index;
                byte[] extractedData = new byte[dataLength];
                Array.Copy(fileContent, index, extractedData, 0, dataLength);

                string outputPath = Path.Combine(outputDir, $"{baseFilename}_{fileCount++}.tex");
                await File.WriteAllBytesAsync(outputPath, extractedData, cancellationToken);
                OnFileExtracted(outputPath);
                index = endIndex;
            }
        }

        private async Task ExtractPatternFadeFilesAsync(byte[] fileContent, string baseFilename, string outputDir, CancellationToken cancellationToken)
        {
            var filenames = ExtractValidFilenames(fileContent);
            int filenameIndex = 0;

            int dataIndex = 0;

            while ((dataIndex = IndexOf(fileContent, texturePattern, dataIndex)) != -1)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int endIndex = IndexOf(fileContent, pngEndPattern, dataIndex);
                if (endIndex == -1)
                {
                    dataIndex += texturePattern.Length;
                    continue;
                }

                endIndex += pngEndPattern.Length;

                int dataLength = endIndex - dataIndex;
                byte[] extractedData = new byte[dataLength];
                Array.Copy(fileContent, dataIndex, extractedData, 0, dataLength);

                string outputFilename = filenameIndex < filenames.Count ?
                    EnsureTexExtension(filenames[filenameIndex]) :
                    $"{baseFilename}_{filenameIndex}.tex";

                string outputPath = Path.Combine(outputDir, outputFilename);
                await File.WriteAllBytesAsync(outputPath, extractedData, cancellationToken);
                OnFileExtracted(outputPath);
                filenameIndex++;
                dataIndex = endIndex;
            }
        }

        private List<string> ExtractValidFilenames(byte[] content)
        {
            int packPos = IndexOf(content, packPattern, 0);
            if (packPos == -1) return new List<string>();

            byte[] target = new byte[packPos];
            Array.Copy(content, 0, target, 0, packPos);

            List<string> result = new List<string>();
            List<char> currentSeq = new List<char>();

            foreach (byte b in target)
            {
                if (b >= 32 && b <= 126)
                {
                    currentSeq.Add((char)b);
                }
                else
                {
                    if (currentSeq.Count > 0)
                    {
                        string seqStr = new string(currentSeq.ToArray());
                        if (Regex.IsMatch(seqStr, @"[^.]+\.[^.]+"))
                        {
                            result.Add(seqStr);
                        }
                        currentSeq.Clear();
                    }
                }
            }

            if (currentSeq.Count > 0)
            {
                string seqStr = new string(currentSeq.ToArray());
                if (Regex.IsMatch(seqStr, @"[^.]+\.[^.]+"))
                {
                    result.Add(seqStr);
                }
            }

            return result;
        }

        private string EnsureTexExtension(string filename)
        {
            return Path.ChangeExtension(filename, ".tex");
        }

        private static IEnumerable<byte[]> ExtractAt3Data(byte[] fileContent)
        {
            int at3Start = 0;
            while ((at3Start = IndexOf(fileContent, at3Header, at3Start)) != -1)
            {
                if (at3Start + 12 > fileContent.Length)
                {
                    at3Start += 4;
                    continue;
                }

                int fileSize = BitConverter.ToInt32(fileContent, at3Start + 4);
                fileSize = (fileSize + 1) & ~1;

                if (fileSize <= 0 || at3Start + 8 + fileSize > fileContent.Length)
                {
                    at3Start += 4;
                    continue;
                }

                int blockStart = at3Start + 8;
                bool hasAudioBlock = IndexOf(fileContent, audioBlock, blockStart) != -1;

                if (hasAudioBlock)
                {
                    int actualLength = Math.Min(fileSize + 8, fileContent.Length - at3Start);
                    byte[] at3Data = new byte[actualLength];
                    Array.Copy(fileContent, at3Start, at3Data, 0, actualLength);
                    yield return at3Data;
                }

                at3Start += Math.Max(4, fileSize + 8);
            }
        }

        private static IEnumerable<byte[]> ExtractEnvData(byte[] fileContent)
        {
            int envStart = 0;
            while ((envStart = IndexOf(fileContent, envHeader, envStart)) != -1)
            {
                if (envStart + 12 > fileContent.Length)
                {
                    envStart += 4;
                    continue;
                }

                int nextHeader = IndexOf(fileContent, envHeader, envStart + 4);
                if (nextHeader == -1) nextHeader = fileContent.Length;

                int dataLength = nextHeader - envStart;
                if (dataLength <= 0)
                {
                    envStart += 4;
                    continue;
                }

                byte[] envData = new byte[dataLength];
                Array.Copy(fileContent, envStart, envData, 0, dataLength);
                yield return envData;

                envStart = nextHeader;
            }
        }

        private byte[] TrimTrailingNulls(byte[] data)
        {
            int endIndex = data.Length - 1;
            while (endIndex >= 0 && data[endIndex] == 0)
            {
                endIndex--;
            }

            byte[] result = new byte[endIndex + 1];
            Array.Copy(data, result, endIndex + 1);
            return result;
        }

        private string SaveFile(string outputDir, string filename, byte[] data)
        {
            string outputPath = Path.Combine(outputDir, filename);
            int counter = 1;
            string originalOutputPath = outputPath;

            while (File.Exists(outputPath))
            {
                string name = Path.GetFileNameWithoutExtension(originalOutputPath);
                string ext = Path.GetExtension(originalOutputPath);
                outputPath = Path.Combine(outputDir, $"{name}_{counter}{ext}");
                counter++;
            }
            File.WriteAllBytes(outputPath, data);
            return outputPath;
        }

        private new static int IndexOf(byte[] source, byte[] pattern, int startIndex)
        {
            for (int i = startIndex; i <= source.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
