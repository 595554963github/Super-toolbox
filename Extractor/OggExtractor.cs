namespace super_toolbox
{
    public class OggExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private bool _stopParsingOnFormatError = true;
        private int _globalIndex = 1; 

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
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();
            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;
            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;
                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");
                    try
                    {
                        await ProcessFileAsync(filePath, extractedDir, extractedFiles, cancellationToken);
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
                if (extractedFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个ogg文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，未找到ogg文件");
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
        private async Task ProcessFileAsync(string filePath, string outputDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            long offset = 0;
            Dictionary<uint, (FileStream stream, string fileName)> outputStreams = new Dictionary<uint, (FileStream, string)>();
            string fileNamePrefix = Path.GetFileNameWithoutExtension(filePath);
            using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read))
            {
                try
                {
                    while ((offset = ParseFile.GetNextOffset(fs, offset, XiphOrgOggContainer.MAGIC_BYTES)) > -1)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        byte pageType = ParseFile.ParseSimpleOffset(fs, offset + 5, 1)[0];
                        uint bitstreamSerialNumber = BitConverter.ToUInt32(ParseFile.ParseSimpleOffset(fs, offset + 0xE, 4), 0);
                        byte segmentCount = ParseFile.ParseSimpleOffset(fs, offset + 0x1A, 1)[0];
                        uint sizeOfAllSegments = 0;
                        for (byte i = 0; i < segmentCount; i++)
                        {
                            sizeOfAllSegments += ParseFile.ParseSimpleOffset(fs, offset + 0x1B + i, 1)[0];
                        }
                        long pageSize = 0x1B + segmentCount + sizeOfAllSegments;
                        byte[] rawPageBytes = ParseFile.ParseSimpleOffset(fs, offset, (int)pageSize);
                        bool pageWrittenToFile = false;
                        if ((pageType & XiphOrgOggContainer.PAGE_TYPE_BEGIN_STREAM) == XiphOrgOggContainer.PAGE_TYPE_BEGIN_STREAM)
                        {
                            if (outputStreams.ContainsKey(bitstreamSerialNumber))
                            {
                                if (_stopParsingOnFormatError)
                                {
                                    throw new FormatException(
                                        $"多次找到流开始页面，但没有流结束页面，用于序列号:{bitstreamSerialNumber:X8}，文件:{filePath}");
                                }
                                else
                                {
                                    ExtractionProgress?.Invoke(this, $"警告:对于文件 <{filePath}>，多次找到流开始页面但没有流结束页面，序列号为:{bitstreamSerialNumber:X8}");
                                    continue;
                                }
                            }
                            else
                            {
                                string outputFileName = Path.Combine(outputDir, $"{fileNamePrefix}_{_globalIndex}.ogg");
                                outputFileName = await GetNonDuplicateFileNameAsync(outputFileName, cancellationToken);
                                var fileStream = File.Open(outputFileName, FileMode.CreateNew, FileAccess.Write);
                                fileStream.Write(rawPageBytes, 0, rawPageBytes.Length);
                                pageWrittenToFile = true;
                                outputStreams[bitstreamSerialNumber] = (fileStream, outputFileName);
                                extractedFiles.Add(outputFileName);
                                OnFileExtracted(outputFileName);
                                _globalIndex++;
                            }
                        }
                        if (outputStreams.ContainsKey(bitstreamSerialNumber))
                        {
                            if (!pageWrittenToFile)
                            {
                                outputStreams[bitstreamSerialNumber].stream.Write(rawPageBytes, 0, rawPageBytes.Length);
                                pageWrittenToFile = true;
                            }
                        }
                        else
                        {
                            if (_stopParsingOnFormatError)
                            {
                                throw new FormatException(
                                    $"找到没有流开始页的流数据页，用于序列号:{bitstreamSerialNumber:X8}，文件:{filePath}");
                            }
                            else
                            {
                                ExtractionProgress?.Invoke(this, $"警告:对于文件 <{filePath}>，找到没有流开始页的流数据页，序列号为:{bitstreamSerialNumber:X8}");
                                continue;
                            }
                        }
                        if ((pageType & XiphOrgOggContainer.PAGE_TYPE_END_STREAM) == XiphOrgOggContainer.PAGE_TYPE_END_STREAM)
                        {
                            if (outputStreams.ContainsKey(bitstreamSerialNumber))
                            {
                                if (!pageWrittenToFile)
                                {
                                    outputStreams[bitstreamSerialNumber].stream.Write(rawPageBytes, 0, rawPageBytes.Length);
                                    pageWrittenToFile = true;
                                }
                                var (stream, fileName) = outputStreams[bitstreamSerialNumber];
                                stream.Close();
                                stream.Dispose();
                                outputStreams.Remove(bitstreamSerialNumber);
                            }
                            else
                            {
                                if (_stopParsingOnFormatError)
                                {
                                    throw new FormatException(
                                        $"找到没有流开始页面的流结束页面，用于序列号:{bitstreamSerialNumber:X8}，文件:{filePath}");
                                }
                                else
                                {
                                    ExtractionProgress?.Invoke(this, $"警告:对于文件 <{filePath}>，找到没有流开始页面的流结束页面，序列号为:{bitstreamSerialNumber:X8}");
                                }
                            }
                        }
                        offset += pageSize;
                    }
                }
                catch (Exception ex)
                {
                    if (_stopParsingOnFormatError)
                    {
                        throw;
                    }
                    else
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出现异常: {ex.Message}");
                        OnExtractionFailed($"处理文件{filePath}时出现异常: {ex.Message}");
                    }
                }
                finally
                {
                    foreach (var (stream, fileName) in outputStreams.Values)
                    {
                        stream.Close();
                        stream.Dispose();
                    }
                }
            }
        }
        private async Task<string> GetNonDuplicateFileNameAsync(string fileName, CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(fileName) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            int counter = 1;
            string newFileName = fileName;

            while (File.Exists(newFileName))
            {
                ThrowIfCancellationRequested(cancellationToken);
                newFileName = Path.Combine(directory, $"{name}_{counter}{extension}");
                counter++;
            }
            return newFileName;
        }
        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        private static class ParseFile
        {
            public static byte[] ParseSimpleOffset(FileStream fs, long offset, int length)
            {
                byte[] buffer = new byte[length];
                fs.Seek(offset, SeekOrigin.Begin);
                fs.Read(buffer, 0, length);
                return buffer;
            }
            public static long GetNextOffset(FileStream fs, long offset, byte[] magicBytes)
            {
                byte[] buffer = new byte[magicBytes.Length];
                while (offset + magicBytes.Length <= fs.Length)
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    fs.Read(buffer, 0, magicBytes.Length);
                    if (AreByteArraysEqual(buffer, magicBytes))
                    {
                        return offset;
                    }
                    offset++;
                }
                return -1;
            }
            private static bool AreByteArraysEqual(byte[] a, byte[] b)
            {
                if (a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i] != b[i]) return false;
                }
                return true;
            }
        }
        private static class XiphOrgOggContainer
        {
            public static readonly byte[] MAGIC_BYTES = { 0x4F, 0x67, 0x67, 0x53 };
            public const byte PAGE_TYPE_BEGIN_STREAM = 0x02;
            public const byte PAGE_TYPE_END_STREAM = 0x04;
        }
    }
}