namespace super_toolbox
{
    public class GpkExtractor : BaseExtractor
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

            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            var gpkFiles = Directory.EnumerateFiles(directoryPath, "*.gpk", SearchOption.AllDirectories);
            TotalFilesToExtract = 0;

            foreach (var gpkFilePath in gpkFiles)
            {
                try
                {
                    var gpk = new GPK(gpkFilePath);
                    TotalFilesToExtract += gpk.Files.Count;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"读取GPK文件头失败:{Path.GetFileName(gpkFilePath)} - {ex.Message}");
                }
            }

            int processedGpkFiles = 0;
            int totalGpkFiles = gpkFiles.Count();

            try
            {
                foreach (var gpkFilePath in gpkFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedGpkFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理GPK文件({processedGpkFiles}/{totalGpkFiles}): {Path.GetFileName(gpkFilePath)}");

                    try
                    {
                        await ExtractGpkFile(gpkFilePath, extractedFiles, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理GPK文件失败:{Path.GetFileName(gpkFilePath)} - {ex.Message}");
                        OnExtractionFailed($"处理GPK文件失败:{Path.GetFileName(gpkFilePath)} - {ex.Message}");
                    }
                }

                if (extractedFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成，共从{processedGpkFiles} 个GPK文件中提取出{extractedFiles.Count}个文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，未找到可提取的文件");
                }

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnExtractionFailed($"严重错误:{ex.Message}");
            }
        }

        private async Task ExtractGpkFile(string gpkFilePath, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            string outputDir = GetOutputDirectory(gpkFilePath);
            Directory.CreateDirectory(outputDir);

            var gpk = new GPK(gpkFilePath);

            ExtractionProgress?.Invoke(this, $"在GPK文件中找到{gpk.Files.Count}个文件");

            for (int i = 0; i < gpk.Files.Count; i++)
            {
                ThrowIfCancellationRequested(cancellationToken);

                var declaration = gpk.Declarations[i];
                var fileData = gpk.Files[i];

                string outputFileName = declaration.FileName;
                string outputFilePath = Path.Combine(outputDir, outputFileName);

                if (File.Exists(outputFilePath))
                {
                    int duplicateCount = 1;
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(outputFileName);
                    string fileExt = Path.GetExtension(outputFileName);

                    do
                    {
                        outputFileName = $"{fileNameWithoutExt}_dup{duplicateCount}{fileExt}";
                        outputFilePath = Path.Combine(outputDir, outputFileName);
                        duplicateCount++;
                    } while (File.Exists(outputFilePath));
                }

                try
                {
                    string? fileDir = Path.GetDirectoryName(outputFilePath);
                    if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                    {
                        Directory.CreateDirectory(fileDir);
                    }

                    await File.WriteAllBytesAsync(outputFilePath, fileData, cancellationToken);

                    if (!extractedFiles.Contains(outputFilePath))
                    {
                        extractedFiles.Add(outputFilePath);
                        OnFileExtracted(outputFilePath);
                        ExtractionProgress?.Invoke(this, $"已提取:{outputFileName}");
                    }
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"写入文件{outputFileName}时出错:{ex.Message}");
                    OnExtractionFailed($"写入文件{outputFileName}时出错:{ex.Message}");
                }
            }

            ExtractionProgress?.Invoke(this, $"成功从{Path.GetFileName(gpkFilePath)}中提取{gpk.Files.Count}个文件");
        }

        private string GetOutputDirectory(string gpkFilePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(gpkFilePath);
            string directory = Path.GetDirectoryName(gpkFilePath) ?? "";

            string safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(directory, $"{safeFileName}_Extracted");
        }
    }

    public class GPK
    {
        public List<GPKDeclaration> Declarations { get; set; }
        public List<byte[]> Files { get; set; }

        public GPK()
        {
            Declarations = new List<GPKDeclaration>();
            Files = new List<byte[]>();
        }

        public GPK(string path)
        {
            using var reader = new BinaryReader(File.Open(path, FileMode.Open));
            uint fileCount = reader.ReadUInt32();
            Declarations = new List<GPKDeclaration>((int)fileCount);
            Files = new List<byte[]>((int)fileCount);

            for (int i = 0; i < fileCount; i++)
            {
                Declarations.Add(new GPKDeclaration(reader));
            }

            foreach (var declaration in Declarations)
            {
                reader.BaseStream.Seek(declaration.Offset, SeekOrigin.Begin);
                Files.Add(reader.ReadBytes(declaration.Size));
            }
        }

        public void Save(string path)
        {
            using var writer = new BinaryWriter(File.Open(path, FileMode.Create));
            writer.Write(Declarations.Count);

            foreach (var declaration in Declarations)
            {
                declaration.Save(writer);
            }

            foreach (var file in Files)
            {
                writer.Write(file);
            }
        }
    }

    public class GPKDeclaration
    {
        public string FileName { get; set; }
        public uint Offset { get; set; }
        public int Size { get; set; }

        public GPKDeclaration()
        {
            FileName = "";
        }

        public GPKDeclaration(BinaryReader reader)
        {
            FileName = new string(reader.ReadChars(260)).Replace("\0", "");
            Size = reader.ReadInt32();
            Offset = reader.ReadUInt32();
        }

        public void Save(BinaryWriter writer)
        {
            for (int i = 0; i < FileName.Length; i++)
                writer.Write(FileName[i]);

            for (int i = 0; i < 260 - FileName.Length; i++)
                writer.Write((byte)0);

            writer.Write(Size);
            writer.Write(Offset);
        }
    }
}