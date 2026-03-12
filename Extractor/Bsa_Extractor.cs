using SharpBSABA2.BSAUtil;
using System.Text;

namespace super_toolbox
{
    public class Bsa_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        private Encoding _encoding = Encoding.UTF8;
        private bool _retrieveRealSize = true;

        public Encoding Encoding
        {
            get => _encoding;
            set => _encoding = value;
        }

        public bool RetrieveRealSize
        {
            get => _retrieveRealSize;
            set => _retrieveRealSize = value;
        }

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

            var bsaFiles = Directory.EnumerateFiles(directoryPath, "*.bsa", SearchOption.AllDirectories)
                           .Concat(Directory.EnumerateFiles(directoryPath, "*.ba2", SearchOption.AllDirectories));

            TotalFilesToExtract = bsaFiles.Count();
            int processedFiles = 0;

            foreach (var bsaFile in bsaFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(bsaFile)}");

                BSA? bsa = null;
                try
                {
                    string outputDir = Path.Combine(Path.GetDirectoryName(bsaFile) ?? directoryPath,
                                                   $"{Path.GetFileNameWithoutExtension(bsaFile)}");
                    Directory.CreateDirectory(outputDir);

                    bsa = new BSA(bsaFile, _encoding, _retrieveRealSize);

                    foreach (var entry in bsa.Files)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        string outputPath = Path.Combine(outputDir, entry.FullPath);
                        string outputDirPath = Path.GetDirectoryName(outputPath) ?? outputDir;

                        if (!Directory.Exists(outputDirPath))
                            Directory.CreateDirectory(outputDirPath);

                        using (var fs = File.Create(outputPath))
                        {
                            if (entry is BSAFileEntry bsaEntry)
                            {
                                var ms = bsaEntry.GetDataStream();
                                ms.CopyTo(fs);
                            }
                            else
                            {
                                var ms = entry.GetDataStream();
                                ms.CopyTo(fs);
                            }
                        }

                        extractedFiles.Add(outputPath);
                        OnFileExtracted(outputPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{entry.FullPath}");
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{bsaFile}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{bsaFile}时出错:{e.Message}");
                }
                finally
                {
                    bsa?.Close();
                }

                processedFiles++;
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到有效文件");
            }
            OnExtractionCompleted();
        }

        public async Task ExtractSingleFile(string bsaPath, string outputDirectory, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(bsaPath))
            {
                ExtractionError?.Invoke(this, $"BSA文件{bsaPath}不存在");
                OnExtractionFailed($"BSA文件{bsaPath}不存在");
                return;
            }

            BSA? bsa = null;
            try
            {
                ExtractionStarted?.Invoke(this, $"开始处理BSA文件:{Path.GetFileName(bsaPath)}");

                Directory.CreateDirectory(outputDirectory);

                bsa = new BSA(bsaPath, _encoding, _retrieveRealSize);
                TotalFilesToExtract = bsa.FileCount;

                foreach (var entry in bsa.Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string outputPath = Path.Combine(outputDirectory, entry.FullPath);
                    string outputDirPath = Path.GetDirectoryName(outputPath) ?? outputDirectory;

                    if (!Directory.Exists(outputDirPath))
                        Directory.CreateDirectory(outputDirPath);

                    using (var fs = File.Create(outputPath))
                    {
                        if (entry is BSAFileEntry bsaEntry)
                        {
                            var ms = bsaEntry.GetDataStream();
                            ms.CopyTo(fs);
                        }
                        else
                        {
                            var ms = entry.GetDataStream();
                            ms.CopyTo(fs);
                        }
                    }

                    OnFileExtracted(outputPath);
                    ExtractionProgress?.Invoke(this, $"已提取:{entry.FullPath}");
                }

                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{bsaPath}中的所有文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception e)
            {
                ExtractionError?.Invoke(this, $"处理BSA文件{bsaPath}时出错:{e.Message}");
                OnExtractionFailed($"处理BSA文件{bsaPath}时出错:{e.Message}");
            }
            finally
            {
                bsa?.Close();
            }
        }

        public async Task<List<string>> ListFiles(string bsaPath, CancellationToken cancellationToken = default)
        {
            var fileList = new List<string>();

            if (!File.Exists(bsaPath))
            {
                ExtractionError?.Invoke(this, $"BSA文件{bsaPath}不存在");
                return fileList;
            }

            BSA? bsa = null;
            try
            {
                bsa = new BSA(bsaPath, _encoding, false);

                foreach (var entry in bsa.Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    fileList.Add(entry.FullPath);
                }

                return fileList;
            }
            catch (Exception e)
            {
                ExtractionError?.Invoke(this, $"列出BSA文件{bsaPath}内容时出错:{e.Message}");
                return fileList;
            }
            finally
            {
                bsa?.Close();
            }
        }

        public async Task<byte[]?> ExtractFileToMemory(string bsaPath, string filePathInArchive, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(bsaPath))
            {
                ExtractionError?.Invoke(this, $"BSA文件{bsaPath}不存在");
                return null;
            }

            BSA? bsa = null;
            try
            {
                bsa = new BSA(bsaPath, _encoding, _retrieveRealSize);
                var entry = bsa.Files.FirstOrDefault(x =>
                    x.FullPath.Equals(filePathInArchive, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                    return null;

                ThrowIfCancellationRequested(cancellationToken);

                if (entry is BSAFileEntry bsaEntry)
                {
                    using (var ms = bsaEntry.GetDataStream())
                    {
                        return ms.ToArray();
                    }
                }
                else
                {
                    using (var ms = entry.GetDataStream())
                    {
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception e)
            {
                ExtractionError?.Invoke(this, $"从BSA提取文件到内存时出错:{e.Message}");
                return null;
            }
            finally
            {
                bsa?.Close();
            }
        }
    }
}