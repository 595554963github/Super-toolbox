namespace super_toolbox
{
    public class XenobladeMTXT_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] MTXT_SIGNATURE = { 0x4D, 0x54, 0x58, 0x54 };
        private static readonly byte[] MTHS_SIGNATURE = { 0x4D, 0x54, 0x48, 0x53 };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase));

            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);

                string fileName = Path.GetFileName(filePath);

                ExtractionProgress?.Invoke(this, $"正在处理文件:{fileName}");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);

                    if (fileName.Contains("casmt", StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractCasmtFile(content, filePath, extractedDir, extractedFiles);
                    }
                    else if (fileName.Contains("camdo", StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractCamdoFile(content, filePath, extractedDir, extractedFiles);
                    }
                    else if (fileName.Contains("casmda", StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractCasmdaFile(content, filePath, extractedDir, extractedFiles);
                    }
                    else
                    {
                        ExtractGenericFile(content, filePath, extractedDir, extractedFiles);
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

            TotalFilesToExtract = extractedFiles.Count;
            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个MTXT文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到MTXT文件");
            }

            OnExtractionCompleted();
        }

        private void ExtractCasmtFile(byte[] content, string sourceFilePath, string extractedDir, List<string> extractedFiles)
        {
            List<int> mtxtPositions = FindSignaturePositions(content, MTXT_SIGNATURE);

            if (mtxtPositions.Count == 0)
            {
                ExtractionProgress?.Invoke(this, $"在{Path.GetFileName(sourceFilePath)}中未找到MTXT标记");
                return;
            }

            ExtractionProgress?.Invoke(this, $"找到{mtxtPositions.Count}个MTXT标记，开始提取...");

            int fileIndex = 1;

            int firstStart = 0;
            int firstEnd = mtxtPositions[0] + MTXT_SIGNATURE.Length;
            ExtractSegment(content, firstStart, firstEnd, sourceFilePath, fileIndex++, extractedDir, extractedFiles);

            for (int i = 0; i < mtxtPositions.Count - 1; i++)
            {
                int startPos = mtxtPositions[i] + MTXT_SIGNATURE.Length;
                int endPos = mtxtPositions[i + 1] + MTXT_SIGNATURE.Length;
                ExtractSegment(content, startPos, endPos, sourceFilePath, fileIndex++, extractedDir, extractedFiles);
            }
        }

        private void ExtractCamdoFile(byte[] content, string sourceFilePath, string extractedDir, List<string> extractedFiles)
        {
            List<int> mtxtPositions = FindSignaturePositions(content, MTXT_SIGNATURE);

            if (mtxtPositions.Count == 0)
            {
                ExtractionProgress?.Invoke(this, $"在{Path.GetFileName(sourceFilePath)}中未找到MTXT标记");
                return;
            }

            ExtractionProgress?.Invoke(this, $"找到{mtxtPositions.Count}个MTXT标记，开始提取...");

            int fileIndex = 1;

            if (mtxtPositions.Count > 0)
            {
                int firstMtxtPos = mtxtPositions[0];
                int startPos = Math.Max(0, firstMtxtPos - 252);
                int endPos = firstMtxtPos + MTXT_SIGNATURE.Length;

                ExtractSegment(content, startPos, endPos, sourceFilePath, fileIndex++, extractedDir, extractedFiles);
            }

            for (int i = 0; i < mtxtPositions.Count - 1; i++)
            {
                int startPos = mtxtPositions[i] + MTXT_SIGNATURE.Length;
                int endPos = mtxtPositions[i + 1] + MTXT_SIGNATURE.Length;

                if (startPos < endPos)
                {
                    ExtractSegment(content, startPos, endPos, sourceFilePath, fileIndex++, extractedDir, extractedFiles);
                }
            }

            if (mtxtPositions.Count > 0)
            {
                int startPos = mtxtPositions[mtxtPositions.Count - 1] + MTXT_SIGNATURE.Length;
                int endPos = content.Length;

                if (startPos < endPos)
                {
                    ExtractSegment(content, startPos, endPos, sourceFilePath, fileIndex++, extractedDir, extractedFiles);
                }
            }
        }

        private void ExtractCasmdaFile(byte[] content, string sourceFilePath, string extractedDir, List<string> extractedFiles)
        {
            List<int> mtxtPositions = FindSignaturePositions(content, MTXT_SIGNATURE);
            List<int> mthsPositions = FindSignaturePositions(content, MTHS_SIGNATURE);

            if (mtxtPositions.Count == 0)
            {
                ExtractionProgress?.Invoke(this, $"在{Path.GetFileName(sourceFilePath)}中未找到MTXT标记");
                return;
            }

            ExtractionProgress?.Invoke(this, $"找到{mtxtPositions.Count}个MTXT标记，{mthsPositions.Count}个MTHS标记，开始提取...");

            int fileIndex = 1;
            const int MAX_FILE_SIZE = 2 * 1024 * 1024;
            const int MIN_FILE_SIZE = 256;

            List<int> validMtxtPositions = new List<int>();
            foreach (int mtxtPos in mtxtPositions)
            {
                if (!IsInMthsRegion(mtxtPos, mthsPositions, content))
                {
                    validMtxtPositions.Add(mtxtPos);
                }
                else
                {
                    ExtractionProgress?.Invoke(this, $"跳过MTHS区域中的MTXT位置:0x{mtxtPos:X}");
                }
            }

            if (validMtxtPositions.Count < 2)
            {
                ExtractionProgress?.Invoke(this, $"有效MTXT标记不足2个，无法提取");
                return;
            }

            ExtractionProgress?.Invoke(this, $"找到{validMtxtPositions.Count}个有效MTXT标记");
            int startPos = validMtxtPositions[0] + MTXT_SIGNATURE.Length;
            int endPos = validMtxtPositions[1] + MTXT_SIGNATURE.Length;
            int segmentSize = endPos - startPos;

            if (segmentSize > MIN_FILE_SIZE && segmentSize <= MAX_FILE_SIZE && startPos < endPos)
            {
                ExtractSegment(content, startPos, endPos, sourceFilePath, fileIndex++, extractedDir, extractedFiles);
                ExtractionProgress?.Invoke(this, $"提取第1段:0x{startPos:X}-0x{endPos:X}，大小:{segmentSize}字节");
            }
            else if (segmentSize == MIN_FILE_SIZE)
            {
                ExtractionProgress?.Invoke(this, $"跳过256字节小文件段:0x{startPos:X}-0x{endPos:X}");
            }

            for (int i = 1; i < validMtxtPositions.Count - 1; i++)
            {
                startPos = validMtxtPositions[i] + MTXT_SIGNATURE.Length;
                endPos = validMtxtPositions[i + 1] + MTXT_SIGNATURE.Length;
                segmentSize = endPos - startPos;

                if (segmentSize > MIN_FILE_SIZE && segmentSize <= MAX_FILE_SIZE && startPos < endPos)
                {
                    ExtractSegment(content, startPos, endPos, sourceFilePath, fileIndex++, extractedDir, extractedFiles);
                    ExtractionProgress?.Invoke(this, $"提取第{i + 1}段:0x{startPos:X}-0x{endPos:X}，大小:{segmentSize}字节");
                }
                else if (segmentSize == MIN_FILE_SIZE)
                {
                    ExtractionProgress?.Invoke(this, $"跳过256字节小文件段:0x{startPos:X}-0x{endPos:X}");
                }
                else if (segmentSize > MAX_FILE_SIZE)
                {
                    ExtractionProgress?.Invoke(this, $"跳过过大文件段:0x{startPos:X}-0x{endPos:X}，大小:{segmentSize}字节");
                }
            }
            if (validMtxtPositions.Count > 0)
            {
                int lastMtxtPos = validMtxtPositions[validMtxtPositions.Count - 1];
                startPos = lastMtxtPos + MTXT_SIGNATURE.Length;
                endPos = content.Length;
                segmentSize = endPos - startPos;

                if (segmentSize > MIN_FILE_SIZE && segmentSize <= MAX_FILE_SIZE && segmentSize > 0)
                {
                    ExtractSegment(content, startPos, endPos, sourceFilePath, fileIndex++, extractedDir, extractedFiles);
                    ExtractionProgress?.Invoke(this, $"提取最后段:0x{startPos:X}-文件末尾，大小:{segmentSize}字节");
                }
                else if (segmentSize == MIN_FILE_SIZE)
                {
                    ExtractionProgress?.Invoke(this, $"跳过256字节小文件段:0x{startPos:X}-文件末尾");
                }
            }
        }

        private bool IsInMthsRegion(int position, List<int> mthsPositions, byte[] content)
        {
            foreach (int mthsPos in mthsPositions)
            {
                int mthsEnd = Math.Min(content.Length, mthsPos + 1024);
                if (position >= mthsPos && position < mthsEnd)
                {
                    return true;
                }
            }
            return false;
        }

        private void ExtractGenericFile(byte[] content, string sourceFilePath, string extractedDir, List<string> extractedFiles)
        {
            List<int> mtxtPositions = FindSignaturePositions(content, MTXT_SIGNATURE);

            if (mtxtPositions.Count == 0)
            {
                return;
            }

            ExtractionProgress?.Invoke(this, $"找到{mtxtPositions.Count}个MTXT标记，开始提取...");

            int fileIndex = 1;

            for (int i = 0; i < mtxtPositions.Count; i++)
            {
                int startPos = mtxtPositions[i];
                int endPos;

                if (i < mtxtPositions.Count - 1)
                {
                    endPos = mtxtPositions[i + 1];
                }
                else
                {
                    endPos = content.Length;
                }

                if (endPos > startPos)
                {
                    ExtractSegment(content, startPos, endPos, sourceFilePath, fileIndex++, extractedDir, extractedFiles);
                }
            }
        }

        private List<int> FindSignaturePositions(byte[] content, byte[] signature)
        {
            List<int> positions = new List<int>();
            int searchIndex = 0;

            while (searchIndex <= content.Length - signature.Length)
            {
                bool found = true;
                for (int i = 0; i < signature.Length; i++)
                {
                    if (content[searchIndex + i] != signature[i])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    positions.Add(searchIndex);
                    searchIndex += signature.Length;
                }
                else
                {
                    searchIndex++;
                }
            }

            return positions;
        }

        private void ExtractSegment(byte[] content, int startPos, int endPos, string sourceFilePath, int index,
                                  string extractedDir, List<string> extractedFiles)
        {
            int length = endPos - startPos;
            if (length <= 0) return;

            byte[] segmentData = new byte[length];
            Array.Copy(content, startPos, segmentData, 0, length);

            SaveMtxtFile(segmentData, sourceFilePath, index, extractedDir, extractedFiles);
        }

        private void SaveMtxtFile(byte[] mtxtData, string sourceFilePath, int index, string extractedDir, List<string> extractedFiles)
        {
            string baseFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string outputFileName = $"{baseFileName}_{index}.mtxt";
            string outputFilePath = Path.Combine(extractedDir, outputFileName);

            if (File.Exists(outputFilePath))
            {
                int duplicateCount = 1;
                do
                {
                    outputFileName = $"{baseFileName}_{index}_dup{duplicateCount}.mtxt";
                    outputFilePath = Path.Combine(extractedDir, outputFileName);
                    duplicateCount++;
                } while (File.Exists(outputFilePath));
            }

            try
            {
                File.WriteAllBytes(outputFilePath, mtxtData);
                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{outputFileName} (大小:{mtxtData.Length}字节, 位置:0x{baseFileName.Substring(Math.Max(0, baseFileName.Length - 8))})");
                }
            }
            catch (IOException e)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{e.Message}");
                OnExtractionFailed($"写入文件{outputFilePath}时出错:{e.Message}");
            }
        }
    }
}