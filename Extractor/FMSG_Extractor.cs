using System.Text;

namespace super_toolbox
{
    public class FMSG_Extractor : BaseExtractor
    {
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"错误:目录不存在 {directoryPath}");
                return;
            }

            string extractedRootDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedRootDir);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var fmsgFiles = Directory.GetFiles(directoryPath, "*.fmsg", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedRootDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = fmsgFiles.Count;

            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(fmsgFiles, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    }, fmsgFilePath =>
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string fmsgFileName = Path.GetFileNameWithoutExtension(fmsgFilePath);
                            string outputFilePath = Path.Combine(extractedRootDir, $"{fmsgFileName}.txt");

                            if (ExtractSingleFmsg(fmsgFilePath, outputFilePath))
                            {
                                OnFileExtracted(outputFilePath);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"处理文件{Path.GetFileName(fmsgFilePath)}失败:{ex.Message}");
                        }
                    });
                }, cancellationToken);

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("用户取消了操作");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取过程出错:{ex.Message}");
            }
            finally
            {
                sw.Stop();
            }
        }
        private bool ExtractSingleFmsg(string inputPath, string outputPath)
        {
            List<string> result = new List<string>();
            using (Stream stream = File.OpenRead(inputPath))
            {
                BinaryReader reader = new BinaryReader(stream);
                if (reader.BaseStream.Length < 4)
                {
                    throw new Exception("文件大小不足，可能已损坏");
                }

                var header = ReadHeader(ref reader);
                reader.BaseStream.Position = header.TextOffset;

                for (int i = 0; i < header.StrCount; i++)
                {
                    int textLen = reader.ReadInt32();
                    int blockLen = reader.ReadInt32();
                    int strSize = (textLen * 2) - 2;
                    string str = Encoding.Unicode.GetString(reader.ReadBytes(strSize)).Replace("\n", "{LF}");
                    int zeroes = blockLen - (8 + strSize);
                    reader.BaseStream.Position += zeroes;
                    result.Add(str);
                }
                reader.Close();
            }

            result.Reverse();

            int emptyLine = 0;
            foreach (var entry in result)
            {
                if (entry.Length <= 0) emptyLine++;
                else break;
            }
            result.RemoveRange(0, emptyLine);
            result.Reverse();

            if (result.Count > 0)
            {
                if (string.IsNullOrEmpty(outputPath))
                {
                    throw new ArgumentException("输出路径不能为空", nameof(outputPath));
                }

                string? directoryPath = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                File.WriteAllLines(outputPath, result);
                return true;
            }
            return false;
        }
        private struct Header
        {
            public int Magic;
            public int StrCount;
            public int HeaderSize;
            public int TextOffset;
        }
        private static Header ReadHeader(ref BinaryReader reader)
        {
            reader.BaseStream.Seek(0, SeekOrigin.Begin);
            Header header = new Header();
            header.Magic = reader.ReadInt32();
            if (header.Magic != 0x47534D46) throw new Exception("不支持的文件类型");
            reader.BaseStream.Position += 4;
            header.StrCount = reader.ReadInt32();
            header.HeaderSize = reader.ReadInt32();
            header.TextOffset = reader.ReadInt32();
            return header;
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}