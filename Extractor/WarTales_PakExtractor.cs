namespace super_toolbox
{
    public class WarTales_PakExtractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误:目录路径为空");
                OnExtractionFailed("错误:目录路径为空");
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录不存在:{directoryPath}");
                OnExtractionFailed($"错误:目录不存在:{directoryPath}");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*.pak", SearchOption.TopDirectoryOnly)
                .ToList();

            int totalSourceFiles = filePaths.Count;
            int processedSourceFiles = 0;
            int totalExtractedFiles = 0;

            TotalFilesToExtract = totalSourceFiles;

            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedSourceFiles++;

                ExtractionProgress?.Invoke(this, $"正在处理源文件({processedSourceFiles}/{totalSourceFiles}): {Path.GetFileName(filePath)}");

                try
                {
                    string outputPath = Path.Combine(directoryPath, Path.GetFileNameWithoutExtension(filePath));
                    Directory.CreateDirectory(outputPath);

                    await Task.Run(() =>
                    {
                        UnpackPakFile(filePath, outputPath);
                    }, cancellationToken);

                    var extractedFiles = Directory.EnumerateFiles(outputPath, "*.*", SearchOption.AllDirectories).ToList();

                    foreach (var extractedFile in extractedFiles)
                    {
                        OnFileExtracted(extractedFile);
                        totalExtractedFiles++;
                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(extractedFile)}");
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
                    ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{Path.GetFileName(filePath)}时出错:{e.Message}");
                }
            }

            if (totalExtractedFiles > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共处理{totalSourceFiles}个源文件,提取出{totalExtractedFiles}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共处理{totalSourceFiles}个源文件,未找到可提取的文件");
            }

            OnExtractionCompleted();
        }

        public static void UnpackPakFile(string inputFile, string outputFolder)
        {
            using (FileStream fs = new FileStream(inputFile, FileMode.Open))
            {
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    if (new string(reader.ReadChars(4)) == "PAK\0")
                    {
                        uint dataOffset = reader.ReadUInt32();
                        uint fileSize = reader.ReadUInt32();
                        reader.ReadUInt16();
                        uint rootItems = reader.ReadUInt32();
                        
                        string outputPath = outputFolder;
                        Directory.CreateDirectory(outputPath);
                        
                        int i = 0;
                        while ((long)i < (long)((ulong)rootItems))
                        {
                            Unpack(reader, dataOffset, outputPath, "");
                            i++;
                        }
                    }
                }
            }
        }

        public static void Unpack(BinaryReader reader, uint dataOffset, string currentPath, string folderName)
        {
            byte nameLen = reader.ReadByte();
            string name = new string(reader.ReadChars((int)nameLen));
            byte entryType = reader.ReadByte();
            string fullPath = Path.Combine(currentPath, name);
            
            if (entryType == 0)
            {
                uint offset = reader.ReadUInt32();
                uint size = reader.ReadUInt32();
                reader.ReadUInt32();
                long actualOffset = (long)((ulong)(dataOffset + offset));
                SaveFile(reader.BaseStream, actualOffset, size, fullPath);
                return;
            }
            
            if (entryType == 1)
            {
                uint numEntries = reader.ReadUInt32();
                Directory.CreateDirectory(fullPath);
                int i = 0;
                while ((long)i < (long)((ulong)numEntries))
                {
                    Unpack(reader, dataOffset, fullPath, "");
                    i++;
                }
                return;
            }
            
            if (entryType == 2)
            {
                long num = (long)reader.ReadDouble();
                uint size2 = reader.ReadUInt32();
                reader.ReadUInt32();
                long offsetLong = num;
                long actualOffset2 = (long)((ulong)dataOffset + (ulong)offsetLong);
                SaveFile(reader.BaseStream, actualOffset2, size2, fullPath);
                return;
            }
        }

        public static void SaveFile(Stream stream, long offset, uint size, string path)
        {
            long currentPos = stream.Position;
            try
            {
                stream.Seek(offset, SeekOrigin.Begin);
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                using (FileStream outFile = File.Create(path))
                {
                    if (size > 104857600U)
                    {
                        stream.CopyTo(outFile, 8388608);
                    }
                    else
                    {
                        byte[] buffer = new byte[8388608];
                        long bytesWritten = 0L;
                        while (bytesWritten < (long)((ulong)size))
                        {
                            int readSize = (int)Math.Min(8388608L, (long)((ulong)size - (ulong)bytesWritten));
                            int bytesRead = stream.Read(buffer, 0, readSize);
                            if (bytesRead == 0) break;
                            outFile.Write(buffer, 0, bytesRead);
                            bytesWritten += (long)bytesRead;
                        }
                    }
                }
            }
            finally
            {
                stream.Seek(currentPos, SeekOrigin.Begin);
            }
        }
    }
}