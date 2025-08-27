using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .Where(file => !file.StartsWith(Path.Combine(directoryPath, "Extracted"), StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = files.Count;

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            try
            {
                foreach (string filePath in files)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    try
                    {
                        string filename = Path.GetFileName(filePath);

                        if (filename.Equals("Font.pck", StringComparison.OrdinalIgnoreCase) ||
                            filename.Equals("FontMsg.pck", StringComparison.OrdinalIgnoreCase))
                        {
                            await ExtractFontFilesAsync(filePath, extractedDir, cancellationToken);
                        }
                        else if (filename.Equals("PatternFade.pck", StringComparison.OrdinalIgnoreCase))
                        {
                            await ExtractPatternFadeFilesAsync(filePath, extractedDir, cancellationToken);
                        }
                        else
                        {
                            await ExtractFromFileAsync(filePath, extractedDir, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        OnExtractionFailed($"处理文件 {filePath} 时出错: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("提取操作已被取消");
                throw;
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取过程中发生错误: {ex.Message}");
                throw;
            }
        }

        private async Task ExtractFontFilesAsync(string filePath, string extractedDir, CancellationToken cancellationToken)
        {
            byte[] fileContent = await File.ReadAllBytesAsync(filePath, cancellationToken);
            string baseFilename = Path.GetFileNameWithoutExtension(filePath);
            string outputDir = Path.Combine(extractedDir, baseFilename);
            Directory.CreateDirectory(outputDir);

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

            if (Directory.GetFiles(outputDir).Length == 0)
            {
                Directory.Delete(outputDir);
            }
        }

        private async Task ExtractPatternFadeFilesAsync(string filePath, string extractedDir, CancellationToken cancellationToken)
        {
            byte[] fileContent = await File.ReadAllBytesAsync(filePath, cancellationToken);
            string baseFilename = Path.GetFileNameWithoutExtension(filePath);
            string outputDir = Path.Combine(extractedDir, baseFilename);
            Directory.CreateDirectory(outputDir);

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

            if (Directory.GetFiles(outputDir).Length == 0)
            {
                Directory.Delete(outputDir);
            }
        }

        private async Task ExtractFromFileAsync(string filePath, string extractedDir, CancellationToken cancellationToken)
        {
            byte[] fileContent = await File.ReadAllBytesAsync(filePath, cancellationToken);
            string baseFilename = Path.GetFileNameWithoutExtension(filePath);
            string outputDir = Path.Combine(extractedDir, baseFilename);
            Directory.CreateDirectory(outputDir);

            var filenames = ExtractValidFilenames(fileContent);
            int filenameIndex = 0;

            foreach (var at3Data in ExtractAt3Data(fileContent))
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

            foreach (var envData in ExtractEnvData(fileContent))
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

            if (Directory.GetFiles(outputDir).Length == 0)
            {
                Directory.Delete(outputDir);
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

        private static int IndexOf(byte[] source, byte[] pattern, int startIndex)
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