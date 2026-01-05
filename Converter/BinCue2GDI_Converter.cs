using CueSharp;

namespace super_toolbox
{
    public class BinCue2GDI_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var cueFiles = Directory.GetFiles(directoryPath, "*.cue", SearchOption.AllDirectories);
            TotalFilesToConvert = cueFiles.Length;
            int successCount = 0;
            long totalOutputFilesCount = 0;

            try
            {
                foreach (var cueFilePath in cueFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(cueFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.cue");

                    try
                    {
                        (bool conversionSuccess, long outputFileCount) = await ConvertCueToGdi(cueFilePath, cancellationToken);

                        if (conversionSuccess)
                        {
                            successCount++;
                            totalOutputFilesCount += outputFileCount;
                            ConversionProgress?.Invoke(this, $"转换成功:{fileName}.cue,生成文件数量:{outputFileCount}");
                            OnFileConverted(cueFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.cue转换失败");
                            OnConversionFailed($"{fileName}.cue转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.cue处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个CUE文件,累计生成文件总数:{totalOutputFilesCount}");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");
                }

                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnConversionFailed($"严重错误:{ex.Message}");
            }
        }

        private async Task<(bool, long)> ConvertCueToGdi(string cueFilePath, CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        var cueSheet = new CueSheet(cueFilePath);
                        var dir = Path.GetDirectoryName(cueFilePath) ?? string.Empty;
                        var baseName = Path.GetFileNameWithoutExtension(cueFilePath);

                        string outputDir = Path.Combine(dir, baseName);
                        Directory.CreateDirectory(outputDir);

                        using var gdiOutput = new StringWriter();
                        gdiOutput.WriteLine(cueSheet.Tracks.Length);

                        int currentSector = 0;

                        for (int i = 0; i < cueSheet.Tracks.Length; i++)
                        {
                            ThrowIfCancellationRequested(cancellationToken);

                            var track = cueSheet.Tracks[i];
                            var dataFile = track.DataFile;
                            var inputFileName = dataFile.Filename ?? string.Empty;

                            if (string.IsNullOrEmpty(inputFileName))
                            {
                                ConversionError?.Invoke(this, $"轨道{track.TrackNumber}:缺少文件名");
                                continue;
                            }

                            var inputFile = Path.Combine(dir, inputFileName);
                            var outputFile = Path.Combine(outputDir,
                                $"{Path.GetFileNameWithoutExtension(inputFileName)}.{(track.TrackDataType == DataType.AUDIO ? "raw" : "bin")}");

                            int sectorAmount;

                            if (track.Indices.Length == 1)
                            {
                                if (!File.Exists(outputFile))
                                {
                                    if (File.Exists(inputFile))
                                        File.Copy(inputFile, outputFile);
                                    else
                                    {
                                        ConversionError?.Invoke(this, $"文件不存在:{inputFile}");
                                        continue;
                                    }
                                }
                                sectorAmount = (int)(new FileInfo(inputFile).Length / 2352);
                            }
                            else
                            {
                                if (!File.Exists(inputFile))
                                {
                                    ConversionError?.Invoke(this, $"文件不存在:{inputFile}");
                                    continue;
                                }

                                int gapOffset = CountFrames(track.Indices[1]);
                                sectorAmount = CopyWithOffset(inputFile, outputFile, gapOffset);
                                currentSector += gapOffset;
                            }

                            gdiOutput.WriteLine($"{track.TrackNumber} {currentSector} {(track.TrackDataType == DataType.AUDIO ? "0" : "4")} 2352 \"{Path.GetFileName(outputFile)}\" 0");
                            currentSector += sectorAmount;

                            if (track.Comments != null && track.Comments.Contains("HIGH-DENSITY AREA") && currentSector < 45000)
                                currentSector = 45000;
                        }

                        var gdiPath = Path.Combine(outputDir, $"{baseName}.gdi");
                        File.WriteAllText(gdiPath, gdiOutput.ToString());

                        long fileCount = Directory.GetFiles(outputDir, "*.*", SearchOption.TopDirectoryOnly).Length;

                        return (true, fileCount);
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换过程错误:{ex.Message}");
                        return (false, 0);
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                return (false, 0);
            }
        }

        private int CountFrames(CueSharp.Index index)
        {
            return index.Frames + (index.Seconds * 75) + (index.Minutes * 60 * 75);
        }

        private int CopyWithOffset(string input, string output, int frames)
        {
            const int blockSize = 2352;

            if (!File.Exists(input))
                throw new FileNotFoundException($"输入文件不存在:{input}");

            using var infile = File.OpenRead(input);
            using var outfile = File.OpenWrite(output);

            infile.Position = frames * blockSize;

            if (infile.Position >= infile.Length)
                return 0;

            int result = (int)((infile.Length - infile.Position) / blockSize);
            byte[] buffer = new byte[blockSize];

            int bytesRead;
            while ((bytesRead = infile.Read(buffer, 0, blockSize)) > 0)
                outfile.Write(buffer, 0, bytesRead);

            return result;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}