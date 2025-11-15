using System.IO.Compression;
using DALLib.File;
using DALLib.IO;
using DALLib.Exceptions;

namespace super_toolbox
{
    public class StingPckExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static readonly byte[] TARGET_HEADER = { 0x5A, 0x4C, 0x49, 0x42 };
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

            var filePaths = Directory.EnumerateFiles(directoryPath, "*.pck", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase));
            TotalFilesToExtract = 0;
            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");
                try
                {
                    await ProcessPckFileAsync(filePath, extractedDir, extractedFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (SignatureMismatchException e)
                {
                    ExtractionError?.Invoke(this, $"PCK签名不匹配:{Path.GetFileName(filePath)} - {e.Message}");
                    OnExtractionFailed($"PCK签名不匹配:{Path.GetFileName(filePath)} - {e.Message}");
                }
                catch (IOException e)
                {
                    ExtractionError?.Invoke(this, $"读取文件{filePath}时出错:{e.Message}");
                    OnExtractionFailed($"读取文件{filePath}时出错:{e.Message}");
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{filePath}时出错:{e.Message}");
                }
            }
            TotalFilesToExtract = extractedFiles.Count;
            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到可提取的文件");
            }
            OnExtractionCompleted();
        }
        private async Task ProcessPckFileAsync(string filePath, string extractedDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                byte[] fileData;
                using (var fs = File.OpenRead(filePath))
                using (var ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    fileData = ms.ToArray();
                }
                bool isTargetHeader = fileData.Length >= 12 && fileData.Take(4).SequenceEqual(TARGET_HEADER);
                if (isTargetHeader)
                {
                    fileData = fileData.Skip(12).ToArray();

                    if (fileData.Length >= 2 && fileData[0] == 0x78 && fileData[1] == 0xDA)
                    {
                        try
                        {
                            fileData = DecompressZlib(fileData);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidDataException($"ZLIB解压失败:{ex.Message}");
                        }
                    }
                    else
                    {
                        throw new InvalidDataException("不是有效的ZLIB数据");
                    }
                }
                using (var ms = new MemoryStream(fileData))
                using (var reader = new ExtendedBinaryReader(ms))
                {
                    var pckFile = new PCKFile();
                    pckFile.Load(reader);
                    string pckName = Path.GetFileNameWithoutExtension(filePath);
                    string pckOutputDir = Path.Combine(extractedDir, pckName);
                    Directory.CreateDirectory(pckOutputDir);
                    pckFile.ExtractAllFiles(pckOutputDir);
                    foreach (var entry in pckFile.FileEntries)
                    {
                        string extractedFilePath = Path.Combine(pckOutputDir, entry.FileName);
                        if (!extractedFiles.Contains(extractedFilePath))
                        {
                            extractedFiles.Add(extractedFilePath);
                            OnFileExtracted(extractedFilePath);
                            ExtractionProgress?.Invoke(this, $"已提取:{Path.Combine(pckName, entry.FileName)}");
                        }
                    }
                }
            }, cancellationToken);
        }
        private byte[] DecompressZlib(byte[] compressedData)
        {
            using (var inputMs = new MemoryStream(compressedData, 2, compressedData.Length - 2))
            using (var zlibStream = new DeflateStream(inputMs, CompressionMode.Decompress))
            using (var outputMs = new MemoryStream())
            {
                zlibStream.CopyTo(outputMs);
                return outputMs.ToArray();
            }
        }
        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("提取操作已取消", cancellationToken);
            }
        }
    }
}