using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class PhyreTexture_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static string _tempDllPath;
        private static readonly byte[] OFS3_MAGIC = { 0x4F, 0x46, 0x53, 0x33 };
        private static readonly byte[] RYHP_MAGIC = { 0x52, 0x59, 0x48, 0x50 };

        static PhyreTexture_Extractor()
        {
            _tempDllPath = LoadEmbeddedExe("embedded.dds_phyre_tool.dll", "dds-phyre-tool.dll");
        }

        private class PhyreToolDll
        {
            [DllImport("dds-phyre-tool.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
            public static extern bool IsPhyreFile(string filePath);

            [DllImport("dds-phyre-tool.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
            public static extern bool ConvertPhyreToDDS(string inputFile);

            [DllImport("dds-phyre-tool.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr GetLastErrorString();

            [DllImport("dds-phyre-tool.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern void FreeErrorString(IntPtr str);

            public static string GetLastError()
            {
                IntPtr errorPtr = GetLastErrorString();
                if (errorPtr == IntPtr.Zero)
                    return string.Empty;

                string error = Marshal.PtrToStringUni(errorPtr) ?? string.Empty;
                FreeErrorString(errorPtr);
                return error;
            }

            public static bool SafeConvertPhyreToDDS(string inputFile, out string error)
            {
                bool result = ConvertPhyreToDDS(inputFile);
                error = result ? string.Empty : GetLastError();
                return result;
            }

            public static bool SafeIsPhyreFile(string filePath, out string error)
            {
                bool result = IsPhyreFile(filePath);
                error = result ? string.Empty : GetLastError();
                return result;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            var allFiles = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
            TotalFilesToExtract = allFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            ExtractionProgress?.Invoke(this, $"发现{TotalFilesToExtract}个文件");
            int processedCount = 0;
            int successCount = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var filePath in allFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            processedCount++;
                            string fileName = Path.GetFileName(filePath);
                            ExtractionProgress?.Invoke(this, $"正在处理({processedCount}/{TotalFilesToExtract}): {fileName}");
                            int extractedFromFile = ProcessFile(filePath, cancellationToken);
                            successCount += extractedFromFile;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{Path.GetFileName(filePath)}时出错:{ex.Message}");
                            OnExtractionFailed($"处理{Path.GetFileName(filePath)}时出错:{ex.Message}");
                        }
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
                throw;
            }

            ExtractionProgress?.Invoke(this, $"完成!成功提取{successCount}个图片");
            OnExtractionCompleted();
        }

        private int ProcessFile(string filePath, CancellationToken cancellationToken)
        {
            int extractedCount = 0;
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string fileDirectory = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();

            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                if (CheckFileHeader(fileData, 0, OFS3_MAGIC))
                {
                    var fragments = ExtractAllPhyreFragments(fileData);
                    if (fragments.Count == 0)
                    {
                        ExtractionError?.Invoke(this, $"在{Path.GetFileName(filePath)}中未找到PHYRE片段");
                        return 0;
                    }

                    for (int i = 0; i < fragments.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string fragmentFile = Path.Combine(fileDirectory, $"{fileName}_fragment_{i}.phyre");
                        File.WriteAllBytes(fragmentFile, fragments[i]);
                        if (ProcessPhyreFile(fragmentFile, fileName, i))
                        {
                            extractedCount++;
                        }
                        try { File.Delete(fragmentFile); } catch { }
                    }
                }
                else if (CheckFileHeader(fileData, 0, RYHP_MAGIC))
                {
                    if (ProcessPhyreFile(filePath, fileName))
                    {
                        extractedCount++;
                    }
                }
                else
                {
                    ExtractionProgress?.Invoke(this, $"{Path.GetFileName(filePath)}不是phyre格式文件,跳过处理");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理文件{fileName}时出错:{ex.Message}");
            }

            return extractedCount;
        }

        private bool CheckFileHeader(byte[] fileData, int offset, byte[] expectedMagic)
        {
            if (offset + expectedMagic.Length > fileData.Length)
                return false;

            for (int i = 0; i < expectedMagic.Length; i++)
            {
                if (fileData[offset + i] != expectedMagic[i])
                    return false;
            }
            return true;
        }

        private List<byte[]> ExtractAllPhyreFragments(byte[] fileData)
        {
            List<byte[]> fragments = new List<byte[]>();
            int position = 0;
            int magicLength = RYHP_MAGIC.Length;

            while (position <= fileData.Length - magicLength)
            {
                if (CheckFileHeader(fileData, position, RYHP_MAGIC))
                {
                    int startPos = position;
                    int nextHeaderPos = -1;

                    for (int i = position + magicLength; i <= fileData.Length - magicLength; i++)
                    {
                        if (CheckFileHeader(fileData, i, RYHP_MAGIC) || CheckFileHeader(fileData, i, OFS3_MAGIC))
                        {
                            nextHeaderPos = i;
                            break;
                        }
                    }

                    int fragmentLength = (nextHeaderPos == -1) ? fileData.Length - startPos : nextHeaderPos - startPos;
                    bool containsInvalidHeader = false;

                    for (int i = startPos; i <= startPos + fragmentLength - OFS3_MAGIC.Length; i++)
                    {
                        if (CheckFileHeader(fileData, i, OFS3_MAGIC))
                        {
                            containsInvalidHeader = true;
                            break;
                        }
                    }

                    if (!containsInvalidHeader)
                    {
                        byte[] fragment = new byte[fragmentLength];
                        Array.Copy(fileData, startPos, fragment, 0, fragmentLength);
                        fragments.Add(fragment);
                    }

                    position += fragmentLength;
                }
                else
                {
                    position++;
                }
            }

            return fragments;
        }

        private bool ProcessPhyreFile(string phyreFilePath, string originalFileName, int fragmentIndex = 0)
        {
            try
            {
                string error;
                bool isValid = PhyreToolDll.SafeIsPhyreFile(phyreFilePath, out error);

                if (!isValid)
                {
                    ExtractionError?.Invoke(this, $"文件不是有效的Phyre格式: {error}");
                    return false;
                }

                bool success = PhyreToolDll.SafeConvertPhyreToDDS(phyreFilePath, out error);

                if (success)
                {
                    string outputFile = Path.ChangeExtension(phyreFilePath, ".dds");
                    if (File.Exists(outputFile))
                    {
                        OnFileExtracted(outputFile);
                        return true;
                    }
                    else
                    {
                        ExtractionError?.Invoke(this, $"转换成功但输出文件未创建: {outputFile}");
                    }
                }
                else
                {
                    ExtractionError?.Invoke(this, $"处理文件{originalFileName}时出错: {error}");
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理文件{originalFileName}时发生异常: {ex.Message}");
            }

            return false;
        }
    }
}
