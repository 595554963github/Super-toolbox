using System.Text;

namespace super_toolbox
{
    public class MMF_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] MXMI_SIG = { 0x4D, 0x58, 0x4D, 0x49 };
        private static readonly byte[] MXMH_SIG = { 0x4D, 0x58, 0x4D, 0x48 };
        private static readonly byte[] VALID_MXMH_MARK = { 0x03, 0x00, 0x00, 0x00 };

        private static int IndexOf(byte[] data, byte[] pattern, int start)
        {
            for (int i = start; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        public override void Extract(string dirPath)
        {
            ExtractAsync(dirPath).Wait();
        }

        public override async Task ExtractAsync(string dirPath, CancellationToken ct = default)
        {
            List<string> extractedFiles = new List<string>();

            if (!Directory.Exists(dirPath))
            {
                ExtractionError?.Invoke(this, $"目录不存在:{dirPath}");
                OnExtractionFailed($"目录不存在:{dirPath}");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理:{dirPath}");
            TotalFilesToExtract = 0;
            var files = Directory.EnumerateFiles(dirPath, "*.MMF", SearchOption.AllDirectories);

            foreach (var filePath in files)
            {
                ThrowIfCancellationRequested(ct);
                ExtractionProgress?.Invoke(this, $"处理文件:{Path.GetFileName(filePath)}");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, ct);
                    int currOffset = 0;
                    int pairIdx = 0;

                    string sourceFileName = Path.GetFileNameWithoutExtension(filePath);
                    string outputBaseDir = Path.Combine(dirPath, sourceFileName);

                    while (true)
                    {
                        ThrowIfCancellationRequested(ct);
                        int mxmiStart = IndexOf(content, MXMI_SIG, currOffset);
                        if (mxmiStart == -1) break;

                        int mxmhStart = IndexOf(content, MXMH_SIG, mxmiStart + 4);
                        if (mxmhStart == -1) break;

                        int mxmhCheckPos = mxmhStart + 0x10;
                        if (mxmhCheckPos + 3 >= content.Length)
                        {
                            currOffset = mxmhStart + 4;
                            continue;
                        }

                        bool isValidMxmh = true;
                        for (int i = 0; i < 4; i++)
                        {
                            if (content[mxmhCheckPos + i] != VALID_MXMH_MARK[i])
                            {
                                isValidMxmh = false;
                                break;
                            }
                        }

                        if (!isValidMxmh)
                        {
                            currOffset = mxmhStart + 4;
                            continue;
                        }

                        int dataStart = mxmiStart + 0x20;
                        if (dataStart >= mxmhStart || dataStart >= content.Length)
                        {
                            currOffset = mxmhStart + 4;
                            continue;
                        }
                        int dataSize = mxmhStart - dataStart;
                        if (dataSize <= 0)
                        {
                            currOffset = mxmhStart + 4;
                            continue;
                        }
                        byte[] fileData = new byte[dataSize];
                        Array.Copy(content, dataStart, fileData, 0, dataSize);

                        string folderName = string.Empty;
                        int folderStart = mxmhStart + 0x40;
                        int folderEnd = mxmhStart + 0x4D;
                        if (folderEnd < content.Length)
                        {
                            int nullPos = Array.IndexOf(content, (byte)0x00, folderStart);
                            int actualEnd = nullPos > folderStart ? nullPos : folderEnd + 1;
                            int folderLen = actualEnd - folderStart;
                            if (folderLen > 0)
                            {
                                folderName = Encoding.UTF8.GetString(content, folderStart, folderLen);
                            }
                        }

                        string fileName = string.Empty;
                        int fileNameStart = mxmhStart + 0x4F;
                        if (fileNameStart < content.Length)
                        {
                            int nullPos = Array.IndexOf(content, (byte)0x00, fileNameStart);
                            if (nullPos > fileNameStart)
                            {
                                int fileNameLen = nullPos - fileNameStart;
                                fileName = Encoding.UTF8.GetString(content, fileNameStart, fileNameLen);
                            }
                        }

                        string fullPath = string.Empty;
                        if (!string.IsNullOrWhiteSpace(folderName) && !string.IsNullOrWhiteSpace(fileName))
                        {
                            folderName = folderName.TrimStart('.', '\\');
                            fullPath = Path.Combine(folderName, fileName);
                        }
                        else if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            fullPath = fileName;
                        }
                        else if (!string.IsNullOrWhiteSpace(folderName))
                        {
                            fullPath = Path.Combine(folderName, $"mmf_item_{pairIdx}");
                        }
                        else
                        {
                            fullPath = $"mmf_item_{pairIdx}";
                        }

                        string? folderPart = Path.GetDirectoryName(fullPath);
                        string extractDir = string.IsNullOrEmpty(folderPart) ? outputBaseDir : Path.Combine(outputBaseDir, folderPart);
                        string finalFileName = Path.GetFileName(fullPath);
                        Directory.CreateDirectory(extractDir);
                        string outputPath = Path.Combine(extractDir, finalFileName);

                        await File.WriteAllBytesAsync(outputPath, fileData, ct);

                        extractedFiles.Add(outputPath);
                        OnFileExtracted(outputPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{finalFileName}");

                        currOffset = mxmhStart + 4;
                        pairIdx++;
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理{filePath}失败:{e.Message}");
                }
            }

            TotalFilesToExtract = extractedFiles.Count;
            ExtractionProgress?.Invoke(this, $"处理完成,共提取{extractedFiles.Count}个文件");
            OnExtractionCompleted();
        }
    }
}