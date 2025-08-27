using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Filename2Extractor : BaseExtractor
    {
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            try
            {
                var pckFiles = Directory.GetFiles(directoryPath, "*.pck", SearchOption.AllDirectories)
                    .Where(file => !file.Contains("Extracted"))
                    .ToList();

                TotalFilesToExtract = pckFiles.Count;

                if (TotalFilesToExtract == 0)
                {
                    OnExtractionFailed("未找到任何PCK文件");
                    return;
                }

                string extractedDir = Path.Combine(directoryPath, "Extracted");
                Directory.CreateDirectory(extractedDir);

                await Task.Run(() =>
                {
                    Parallel.ForEach(pckFiles, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    }, pckFile =>
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        try
                        {
                            ProcessPckFile(pckFile, extractedDir);
                            OnFileExtracted(Path.GetFileName(pckFile));
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"处理文件 {pckFile} 时发生错误: {ex.Message}");
                        }
                    });
                }, cancellationToken);

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("提取操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取过程中发生错误: {ex.Message}");
            }
        }

        private void ProcessPckFile(string pckFilePath, string extractedDir)
        {
            byte[] content = File.ReadAllBytes(pckFilePath);
            string fileName = Path.GetFileNameWithoutExtension(pckFilePath);

            string outputDir = Path.Combine(extractedDir, fileName);
            Directory.CreateDirectory(outputDir);

            int packPos = FindBytes(content, Encoding.ASCII.GetBytes("Pack"));
            Console.WriteLine($"文件 {fileName}: Pack标记位置 = {(packPos == -1 ? "未找到" : "0x" + packPos.ToString("X"))}");

            if (fileName.Contains("Font", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("PatternFade", StringComparison.OrdinalIgnoreCase))
            {
                ExtractTexOnly(content, outputDir);
            }
            else if (fileName.StartsWith("MA", StringComparison.OrdinalIgnoreCase))
            {
                ExtractTexAndMaFiles(content, outputDir);
            }
            else if (fileName.StartsWith("MP", StringComparison.OrdinalIgnoreCase))
            {
                ExtractPckFiles(content, outputDir);
            }
            else
            {
                throw new NotSupportedException($"不支持的PCK文件类型: {fileName}");
            }
        }

        private void ExtractTexOnly(byte[] content, string outputDir)
        {
            int texStart = FindBytes(content, new byte[] { 0x81, 0x01, 0x40, 0x00 });
            if (texStart == -1)
            {
                throw new Exception("未找到.tex文件起始标记");
            }

            byte[] texData = new byte[content.Length - texStart];
            Array.Copy(content, texStart, texData, 0, texData.Length);
            texData = TrimTrailingNulls(texData);
            SaveFile(outputDir, "file.tex", texData);
        }

        private void ExtractTexAndMaFiles(byte[] content, string outputDir)
        {
            int maStart = FindBytes(content, new byte[] { 0x4D, 0x41, 0x42, 0x30 });
            if (maStart == -1)
            {
                throw new Exception("未找到.MA文件起始标记");
            }

            int texStart = FindBytes(content, new byte[] { 0x81, 0x01, 0x40, 0x00 }, maStart);
            if (texStart == -1)
            {
                throw new Exception("未找到.tex文件起始标记");
            }

            byte[] maData = new byte[texStart - maStart];
            Array.Copy(content, maStart, maData, 0, maData.Length);
            maData = TrimTrailingNulls(maData);
            SaveFile(outputDir, "file.MA", maData);

            byte[] texData = new byte[content.Length - texStart];
            Array.Copy(content, texStart, texData, 0, texData.Length);
            texData = TrimTrailingNulls(texData);
            SaveFile(outputDir, "file.tex", texData);
        }

        private void ExtractPckFiles(byte[] content, string outputDir)
        {
            int currentPos = 0;

            int faceLz7Start = FindBytes(content, new byte[] { 0x4C, 0x5A, 0x37, 0x37 });
            int texAllSTexStart = FindBytes(content, new byte[] { 0x81, 0x01, 0x40, 0x00 });

            if (faceLz7Start != -1 && texAllSTexStart != -1)
            {
                byte[] faceLz7Data = new byte[texAllSTexStart - faceLz7Start];
                Array.Copy(content, faceLz7Start, faceLz7Data, 0, faceLz7Data.Length);
                SaveFile(outputDir, "face.lz7", faceLz7Data);
                currentPos = texAllSTexStart;
            }
            else
            {
                throw new Exception("未找到 face.lz7 文件");
            }

            int texAllSTexEnd = FindBytes(content, new byte[] { 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }, currentPos);
            if (texAllSTexEnd != -1)
            {
                texAllSTexEnd += 11;
                byte[] texAllSTexData = new byte[texAllSTexEnd - currentPos];
                Array.Copy(content, currentPos, texAllSTexData, 0, texAllSTexData.Length);
                SaveFile(outputDir, "tex_all_s.tex", texAllSTexData);
                currentPos = texAllSTexEnd;
            }
            else
            {
                throw new Exception("未找到 tex_all_s.tex 文件结束标记");
            }

            currentPos = FindNextLineStart(content, currentPos);
            byte[] layernameData = new byte[489];
            Array.Copy(content, currentPos, layernameData, 0, 489);
            SaveFile(outputDir, "layername.bin", layernameData);
            currentPos += 489;

            currentPos = FindNextLineStart(content, currentPos);
            byte[] screenData = new byte[11];
            Array.Copy(content, currentPos, screenData, 0, 11);
            SaveFile(outputDir, "screen.txt", screenData);
            currentPos += 11;

            currentPos = FindNextLineStart(content, currentPos);
            byte[] exprData = new byte[96];
            Array.Copy(content, currentPos, exprData, 0, 96);
            SaveFile(outputDir, "exprLoop.exl", exprData);
            currentPos += 96;

            byte[] ucaData = new byte[84];
            Array.Copy(content, currentPos, ucaData, 0, 84);
            SaveFile(outputDir, "face.uca.bin", ucaData);
            currentPos += 84;

            currentPos = FindNextLineStart(content, currentPos);
            byte[] configData = new byte[70];
            Array.Copy(content, currentPos, configData, 0, 70);
            SaveFile(outputDir, "Config.txt", configData);
            currentPos += 70;

            int xtpsStart = FindBytes(content, new byte[] { 0x58, 0x54, 0x50, 0x53 }, currentPos);
            int bmamStart = FindBytes(content, new byte[] { 0x42, 0x4D, 0x41, 0x4D }, xtpsStart);

            if (xtpsStart != -1 && bmamStart != -1)
            {
                int actualEnd = bmamStart - 1;
                while (actualEnd >= xtpsStart && content[actualEnd] == 0x00)
                {
                    actualEnd--;
                }

                int validLength = actualEnd - xtpsStart + 1;

                byte[] texAllSBinData = new byte[validLength];
                Array.Copy(content, xtpsStart, texAllSBinData, 0, validLength);

                SaveFile(outputDir, "tex_all_s.bin", texAllSBinData);
                currentPos = bmamStart;
            }
            else
            {
                throw new Exception("未找到 tex_all_s.bin 文件");
            }

            byte[] ambData = new byte[content.Length - currentPos];
            Array.Copy(content, currentPos, ambData, 0, ambData.Length);
            SaveFile(outputDir, "001.amb", ambData);
        }

        private int FindBytes(byte[] data, byte[] pattern, int startIndex = 0)
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

        private int FindNextLineStart(byte[] content, int currentPos)
        {
            int pos = currentPos;
            while (pos < content.Length && content[pos] == 0)
            {
                pos++;
            }
            return pos;
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

        private void SaveFile(string outputDir, string filename, byte[] data)
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
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
