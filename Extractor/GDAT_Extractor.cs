using System.Text;

namespace super_toolbox
{
    public class GDAT_Extractor : BaseExtractor
    {
        private readonly Dictionary<byte[], string> _markerExtensions = new Dictionary<byte[], string>(new ByteArrayComparer())
        {
            { Encoding.ASCII.GetBytes("3LC"), "cl3" },
            { Encoding.ASCII.GetBytes("xet"), "tex" },
            { Encoding.ASCII.GetBytes("smc"), "cms" },
            { Encoding.ASCII.GetBytes("cdm"), "mdc" },
            { Encoding.ASCII.GetBytes("ldm"), "mdl" },
            { Encoding.ASCII.GetBytes("emf"), "fme" },
            { Encoding.ASCII.GetBytes("hsc"), "csh" },
            { Encoding.ASCII.GetBytes("kpm"), "mpk" },
            { Encoding.ASCII.GetBytes("pim"), "mip" },
            { Encoding.ASCII.GetBytes("dsc"), "csd" },
            { Encoding.ASCII.GetBytes("psb"), "bsp" },
            { Encoding.ASCII.GetBytes("vsb"), "bsv" },
            { Encoding.ASCII.GetBytes("spk"), "kps" },
            { Encoding.ASCII.GetBytes("tid"), "dit" },
            { Encoding.ASCII.GetBytes("dat"), "tad" },
            { Encoding.ASCII.GetBytes("enc"), "cne" },
            { Encoding.ASCII.GetBytes("nibg"), "gbin" },
            { Encoding.ASCII.GetBytes("rtsg"), "gstr" },
            { Encoding.ASCII.GetBytes("qstm"), "mtsq" },
        };

        private class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            public bool Equals(byte[]? x, byte[]? y)
            {
                if (x == null || y == null) return x == y;
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++)
                {
                    if (x[i] != y[i]) return false;
                }
                return true;
            }

            public int GetHashCode(byte[] obj)
            {
                int hash = 17;
                foreach (byte b in obj)
                {
                    hash = hash * 31 + b.GetHashCode();
                }
                return hash;
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"目录不存在:{directoryPath}");
                return;
            }

            var datFiles = Directory.GetFiles(directoryPath, "*.dat", SearchOption.AllDirectories);
            TotalFilesToExtract = datFiles.Length;

            if (TotalFilesToExtract == 0)
            {
                OnExtractionFailed("在指定目录中未找到.dat文件");
                return;
            }

            string outputDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(outputDir);

            try
            {
                foreach (var datFile in datFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        await ExtractFileAsync(datFile, outputDir, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        OnExtractionFailed($"处理文件{Path.GetFileName(datFile)}时出错:{ex.Message}");
                    }
                }

                await DecompressExtractedFilesAsync(outputDir, cancellationToken);
                await ClassifyFilesByExtension(outputDir, cancellationToken);
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取过程中发生错误:{ex.Message}");
            }
        }

        private async Task ExtractFileAsync(string filePath, string outputDir, CancellationToken cancellationToken)
        {
            byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
            List<int> bilzPositions = FindBilzPositions(content);
            if (bilzPositions.Count == 0)
            {
                OnExtractionFailed($"文件{Path.GetFileName(filePath)}中未找到BILZ标记");
                return;
            }

            string baseName = Path.GetFileNameWithoutExtension(filePath);

            for (int i = 0; i < bilzPositions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int currentPos = bilzPositions[i];
                (string? extension, int markerLength) = GetFileExtension(content, currentPos);

                if (extension == null) continue;

                int endPos = content.Length;
                if (i < bilzPositions.Count - 1)
                {
                    int nextPos = bilzPositions[i + 1];
                    int offset = markerLength == 4 ? 128 : 127;
                    endPos = nextPos - offset;
                }

                byte[] extractedData = new byte[endPos - currentPos];
                Array.Copy(content, currentPos, extractedData, 0, extractedData.Length);

                string tempOutputDir = Path.Combine(outputDir, "temp");
                Directory.CreateDirectory(tempOutputDir);
                string outputFile = Path.Combine(tempOutputDir, $"{baseName}_{i}.{extension}");

                await File.WriteAllBytesAsync(outputFile, extractedData, cancellationToken);
                OnFileExtracted(outputFile);
            }
        }

        private List<int> FindBilzPositions(byte[] content)
        {
            List<int> positions = new List<int>();
            byte[] bilzMarker = Encoding.ASCII.GetBytes("BILZ");
            int offset = 0;

            while (offset < content.Length)
            {
                offset = IndexOf(content, bilzMarker, offset);
                if (offset == -1) break;
                positions.Add(offset);
                offset++;
            }

            return positions;
        }

        private new int IndexOf(byte[] source, byte[] pattern, int startIndex)
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

        private (string? extension, int markerLength) GetFileExtension(byte[] content, int bilzPosition)
        {
            if (bilzPosition < 128) return (null, 0);

            byte[] markerRegion = new byte[128];
            Array.Copy(content, bilzPosition - 128, markerRegion, 0, 128);

            for (int i = 0; i <= markerRegion.Length - 4; i++)
            {
                byte[] marker = new byte[4];
                Array.Copy(markerRegion, i, marker, 0, 4);

                if (_markerExtensions.TryGetValue(marker, out string? ext))
                {
                    return (ext, 4);
                }
            }

            for (int i = 0; i <= markerRegion.Length - 3; i++)
            {
                byte[] marker = new byte[3];
                Array.Copy(markerRegion, i, marker, 0, 3);

                if (_markerExtensions.TryGetValue(marker, out string? ext))
                {
                    return (ext, 3);
                }
            }

            return (null, 0);
        }

        private async Task DecompressExtractedFilesAsync(string extractedDir, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(extractedDir)) return;

            string tempDir = Path.Combine(extractedDir, "temp");
            if (!Directory.Exists(tempDir)) return;

            string[] files = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await DecompressFileAsync(file, cancellationToken);
                }
                catch (Exception ex)
                {
                    OnExtractionFailed($"解压文件{Path.GetFileName(file)}时出错: {ex.Message}");
                }
            }
        }

        private async Task DecompressFileAsync(string filePath, CancellationToken cancellationToken)
        {
            byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
            int startIndex = IndexOf(content, new byte[] { 0x78, 0xDA }, 0);

            if (startIndex == -1) return;

            byte[] dataToDecompress = new byte[content.Length - startIndex];
            Array.Copy(content, startIndex, dataToDecompress, 0, dataToDecompress.Length);

            using MemoryStream inputStream = new MemoryStream(dataToDecompress);
            using System.IO.Compression.DeflateStream deflateStream = new System.IO.Compression.DeflateStream(inputStream, System.IO.Compression.CompressionMode.Decompress);
            using MemoryStream outputStream = new MemoryStream();

            await deflateStream.CopyToAsync(outputStream, cancellationToken);
            byte[] decompressedData = outputStream.ToArray();

            await File.WriteAllBytesAsync(filePath, decompressedData, cancellationToken);
        }

        private async Task ClassifyFilesByExtension(string directory, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(directory)) return;

            string tempDir = Path.Combine(directory, "temp");
            if (!Directory.Exists(tempDir)) return;

            string[] files = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories);
            int processed = 0;

            Console.WriteLine($"找到{files.Length}个文件需要分类...");

            foreach (string filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    string extension = Path.GetExtension(filePath).TrimStart('.').ToLower();

                    if (string.IsNullOrEmpty(extension))
                        extension = "unknown";

                    string subFolder = Path.Combine(directory, extension);
                    Directory.CreateDirectory(subFolder);

                    string fileName = Path.GetFileName(filePath);
                    string destinationPath = Path.Combine(subFolder, fileName);

                    if (File.Exists(destinationPath))
                    {
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        int counter = 1;
                        do
                        {
                            destinationPath = Path.Combine(subFolder, $"{fileNameWithoutExt}_{counter}.{extension}");
                            counter++;
                        }
                        while (File.Exists(destinationPath));
                    }

                    await Task.Run(() => File.Move(filePath, destinationPath), cancellationToken);
                    Console.WriteLine($"移动文件:{fileName} -> {extension}/{Path.GetFileName(destinationPath)}");

                    processed++;
                    if (processed % 100 == 0)
                    {
                        Console.WriteLine($"已处理:{processed}/{files.Length}");
                    }
                }
                catch (Exception ex)
                {
                    OnExtractionFailed($"分类文件{Path.GetFileName(filePath)}时出错: {ex.Message}");
                }
            }

            if (Directory.Exists(tempDir))
            {
                try
                {
                    await Task.Run(() => Directory.Delete(tempDir, true), cancellationToken);
                    Console.WriteLine("已删除临时目录");
                }
                catch (Exception ex)
                {
                    OnExtractionFailed($"删除临时目录时出错:{ex.Message}");
                }
            }

            Console.WriteLine($"文件分类完成:共处理{processed}个文件");
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
