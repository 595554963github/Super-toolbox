using System.Text;

namespace super_toolbox
{
    public class Thbgm_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

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

            var fmtFiles = Directory.EnumerateFiles(directoryPath, "thbgm.fmt", SearchOption.AllDirectories);
            var datFiles = Directory.EnumerateFiles(directoryPath, "Thbgm.dat", SearchOption.AllDirectories);

            if (!fmtFiles.Any())
            {
                ExtractionError?.Invoke(this, "未找到thbgm.fmt文件");
                OnExtractionFailed("未找到thbgm.fmt文件");
                return;
            }

            if (!datFiles.Any())
            {
                ExtractionError?.Invoke(this, "未找到Thbgm.dat文件");
                OnExtractionFailed("未找到Thbgm.dat文件");
                return;
            }

            int processedPairs = 0;
            TotalFilesToExtract = fmtFiles.Count();

            foreach (var fmtFile in fmtFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                string datFile = FindMatchingDatFile(fmtFile, directoryPath);
                if (string.IsNullOrEmpty(datFile))
                {
                    ExtractionError?.Invoke(this, $"未找到与{fmtFile}匹配的Thbgm.dat文件");
                    continue;
                }

                try
                {
                    ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(fmtFile)}和{Path.GetFileName(datFile)}");

                    string outputDir = Path.Combine(Path.GetDirectoryName(fmtFile) ?? directoryPath,
                                                   Path.GetFileNameWithoutExtension(datFile));
                    Directory.CreateDirectory(outputDir);

                    List<BgmInfo> bgmList = ReadBgmList(fmtFile);
                    await ProcessDatFile(datFile, bgmList, outputDir, extractedFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件时出错:{e.Message}");
                    OnExtractionFailed($"处理文件时出错:{e.Message}");
                }

                processedPairs++;
            }

            ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个音频文件");
            OnExtractionCompleted();
        }

        private string FindMatchingDatFile(string fmtFile, string directoryPath)
        {
            string baseName = Path.GetFileNameWithoutExtension(fmtFile);
            string expectedDatName = baseName.Replace(".fmt", ".dat");

            var possiblePaths = new List<string>
            {
                Path.Combine(Path.GetDirectoryName(fmtFile) ?? "", "Thbgm.dat"),
                Path.Combine(Path.GetDirectoryName(fmtFile) ?? "", expectedDatName)
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            var allDatFiles = Directory.EnumerateFiles(directoryPath, "*.dat", SearchOption.AllDirectories);
            foreach (var datFile in allDatFiles)
            {
                if (Path.GetFileName(datFile).Equals("thbgm.dat", StringComparison.OrdinalIgnoreCase))
                    return datFile;
            }

            return string.Empty;
        }

        private List<BgmInfo> ReadBgmList(string fmtPath)
        {
            List<BgmInfo> bgmList = new List<BgmInfo>();

            using (FileStream fs = new FileStream(fmtPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                fs.Seek(0, SeekOrigin.End);
                long size = fs.Length;
                fs.Seek(0, SeekOrigin.Begin);

                while (fs.Position < size)
                {
                    byte[] data = br.ReadBytes(52);
                    if (data.Length == 52)
                    {
                        string name = Encoding.UTF8.GetString(data, 0, 16).TrimEnd('\0');
                        if (!string.IsNullOrEmpty(name))
                        {
                            int start = BitConverter.ToInt32(data, 16);
                            int duration = BitConverter.ToInt32(data, 20);
                            int loopStart = BitConverter.ToInt32(data, 24);
                            int loopDuration = BitConverter.ToInt32(data, 28);
                            int channels = BitConverter.ToInt16(data, 34);
                            int sampleRate = BitConverter.ToInt32(data, 36);
                            int bits = BitConverter.ToInt16(data, 46);

                            bgmList.Add(new BgmInfo
                            {
                                Name = name,
                                Start = start,
                                Duration = duration,
                                LoopStart = loopStart,
                                LoopDuration = loopDuration,
                                Channels = channels,
                                SampleRate = sampleRate,
                                Bits = bits
                            });
                        }
                    }
                }
            }

            return bgmList;
        }

        private async Task ProcessDatFile(string datPath, List<BgmInfo> bgmList, string outputDir,
                                         List<string> extractedFiles, CancellationToken cancellationToken)
        {
            using (FileStream fs = new FileStream(datPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                TotalFilesToExtract = bgmList.Count;
                int processedFiles = 0;

                foreach (var bgm in bgmList)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    try
                    {
                        fs.Seek(bgm.Start, SeekOrigin.Begin);
                        byte[] audioData = br.ReadBytes(bgm.Duration);

                        if (audioData.Length == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"跳过空文件:{bgm.Name}");
                            continue;
                        }

                        string baseName = bgm.Name.ToLower().EndsWith(".wav") ? bgm.Name.Substring(0, bgm.Name.Length - 4) : bgm.Name;
                        string outputPath = Path.Combine(outputDir, $"{baseName}.wav");
                        outputPath = GetUniqueFilePath(outputPath);

                        using (FileStream outputFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                        {
                            WriteWavHeader(outputFs, bgm, audioData.Length);
                            await outputFs.WriteAsync(audioData, 0, audioData.Length, cancellationToken);
                        }

                        extractedFiles.Add(outputPath);
                        OnFileExtracted(outputPath);

                        string sizeStr = HumanSize(audioData.Length);
                        ExtractionProgress?.Invoke(this, $"已提取:{baseName}.wav {sizeStr}");
                    }
                    catch (Exception e)
                    {
                        ExtractionError?.Invoke(this, $"处理{bgm.Name}时出错:{e.Message}");
                    }

                    processedFiles++;
                }
            }
        }

        private void WriteWavHeader(FileStream fs, BgmInfo bgm, int dataSize)
        {
            using (BinaryWriter bw = new BinaryWriter(fs, Encoding.Default, true))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + dataSize);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)bgm.Channels);
                bw.Write(bgm.SampleRate);
                bw.Write(bgm.SampleRate * bgm.Channels * (bgm.Bits / 8));
                bw.Write((short)(bgm.Channels * (bgm.Bits / 8)));
                bw.Write((short)bgm.Bits);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(dataSize);
            }
        }

        private string HumanSize(long size)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double value = size;
            int unitIndex = 0;

            while (value >= 1024.0 && unitIndex < units.Length - 1)
            {
                value /= 1024.0;
                unitIndex++;
            }

            return $"{value:F1}{units[unitIndex]}";
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string fileExtension = Path.GetExtension(filePath);

            int duplicateCount = 1;
            string newFilePath;
            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{duplicateCount}{fileExtension}");
                duplicateCount++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }

        private class BgmInfo
        {
            public string Name { get; set; } = string.Empty;
            public int Start { get; set; }
            public int Duration { get; set; }
            public int LoopStart { get; set; }
            public int LoopDuration { get; set; }
            public int Channels { get; set; }
            public int SampleRate { get; set; }
            public int Bits { get; set; }
        }
    }
}