namespace super_toolbox
{
    public class CmvExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] CMV_HEADER = { 0x43, 0x4D, 0x56 };
        private static readonly byte[] JBPD_HEADER = { 0x4A, 0x42, 0x50, 0x44, 0x2C, 0x00, 0x00, 0x00 };
        private static readonly byte[] OGG_HEADER = { 0x4F, 0x67, 0x67, 0x53 };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*.cmv", SearchOption.AllDirectories)
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;
            int totalJbpdFrames = 0;
            int totalOggFiles = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                    string fileDir = Path.GetDirectoryName(filePath) ?? "";
                    string extractedDir = Path.Combine(fileDir, fileNameWithoutExt);
                    Directory.CreateDirectory(extractedDir);

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);

                        if (content.Length < 4 || content[0] != CMV_HEADER[0] || content[1] != CMV_HEADER[1] || content[2] != CMV_HEADER[2])
                        {
                            ExtractionError?.Invoke(this, $"文件{Path.GetFileName(filePath)}不是有效的CMV文件");
                            continue;
                        }

                        List<long> framePositions = new List<long>();
                        long oggPosition = -1;

                        for (long i = 0; i < content.Length - JBPD_HEADER.Length; i++)
                        {
                            ThrowIfCancellationRequested(cancellationToken);

                            bool foundJbpd = true;
                            for (int j = 0; j < JBPD_HEADER.Length; j++)
                            {
                                if (content[i + j] != JBPD_HEADER[j])
                                {
                                    foundJbpd = false;
                                    break;
                                }
                            }
                            if (foundJbpd)
                            {
                                framePositions.Add(i);
                            }

                            if (oggPosition == -1)
                            {
                                bool foundOgg = true;
                                for (int j = 0; j < OGG_HEADER.Length; j++)
                                {
                                    if (i + j >= content.Length || content[i + j] != OGG_HEADER[j])
                                    {
                                        foundOgg = false;
                                        break;
                                    }
                                }
                                if (foundOgg)
                                {
                                    oggPosition = i;
                                }
                            }
                        }

                        ExtractionProgress?.Invoke(this, $"找到{framePositions.Count}个JBPD");

                        if (framePositions.Count > 0)
                        {
                            string baseName = Path.GetFileNameWithoutExtension(filePath);

                            for (int i = 0; i < framePositions.Count; i++)
                            {
                                ThrowIfCancellationRequested(cancellationToken);

                                long start = framePositions[i];
                                long end;

                                if (i < framePositions.Count - 1)
                                {
                                    end = framePositions[i + 1];
                                }
                                else
                                {
                                    end = (oggPosition != -1) ? oggPosition : content.Length;
                                }

                                int frameSize = (int)(end - start);
                                byte[] frameData = new byte[frameSize];
                                Array.Copy(content, start, frameData, 0, frameSize);

                                string outputFileName = $"{baseName}_{i + 1}.jbpd";
                                string outputFilePath = Path.Combine(extractedDir, outputFileName);

                                await File.WriteAllBytesAsync(outputFilePath, frameData, cancellationToken);

                                totalJbpdFrames++;
                                OnFileExtracted(outputFilePath);
                            }
                        }

                        if (oggPosition != -1)
                        {
                            int oggSize = (int)(content.Length - oggPosition);
                            byte[] oggData = new byte[oggSize];
                            Array.Copy(content, oggPosition, oggData, 0, oggSize);

                            string outputFileName = $"{fileNameWithoutExt}.ogg";
                            string outputFilePath = Path.Combine(extractedDir, outputFileName);

                            await File.WriteAllBytesAsync(outputFilePath, oggData, cancellationToken);

                            totalOggFiles++;
                            OnFileExtracted(outputFilePath);
                        }

                        ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(filePath)}处理完成: 提取{framePositions.Count}个JBPD帧, {(oggPosition != -1 ? 1 : 0)}个OGG文件");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                if (totalJbpdFrames > 0 || totalOggFiles > 0)
                {
                    ExtractionProgress?.Invoke(this, $"全部处理完成,共提取出{totalJbpdFrames}个JBPD帧,{totalOggFiles}个OGG文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成,未找到CMV文件内容");
                }

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }
    }
}