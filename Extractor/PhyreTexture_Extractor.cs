using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class PhyreTexture_Extractor : BaseExtractor
    {
        private static string _tempExePath;

        private static readonly byte[] OFS3_MAGIC = { 0x4F, 0x46, 0x53, 0x33 }; // "OFS3"
        private static readonly byte[] RYHP_MAGIC = { 0x52, 0x59, 0x48, 0x50 }; // "RYHP"

        static PhyreTexture_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.dds_phyre_tool.exe", "dds-phyre-tool.exe");
        }

        private bool CheckFileHeader(string filePath, byte[] expectedMagic)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    byte[] header = reader.ReadBytes(expectedMagic.Length);
                    return header.SequenceEqual(expectedMagic);
                }
            }
            catch
            {
                return false;
            }
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

                    int fragmentLength;
                    if (nextHeaderPos == -1)
                    {
                        fragmentLength = fileData.Length - startPos;
                    }
                    else
                    {
                        fragmentLength = nextHeaderPos - startPos;
                    }

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
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"\"{phyreFilePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    OnFileExtracted(phyreFilePath);
                    return true;
                }
                else
                {
                    string error = process.StandardError.ReadToEnd();
                    OnExtractionFailed($"处理文件 {originalFileName} 时出错: {error}");
                }
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"处理文件 {originalFileName} 时发生异常: {ex.Message}");
            }

            return false;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"错误: {directoryPath} 不是有效的目录");
                return;
            }

            var allFiles = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .ToList();

            TotalFilesToExtract = allFiles.Count;
            if (TotalFilesToExtract == 0)
            {
                OnExtractionCompleted();
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;

                    foreach (var filePath in allFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        processedCount++;

                        try
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            byte[] fileData = File.ReadAllBytes(filePath);

                            string fileDirectory = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();

                            if (CheckFileHeader(fileData, 0, OFS3_MAGIC))
                            {
                                var fragments = ExtractAllPhyreFragments(fileData);

                                if (fragments.Count == 0)
                                {
                                    OnExtractionFailed($"在 {Path.GetFileName(filePath)} 中未找到PHYRE片段");
                                    continue;
                                }

                                for (int i = 0; i < fragments.Count; i++)
                                {
                                    ThrowIfCancellationRequested(cancellationToken);

                                    string fragmentFile = Path.Combine(fileDirectory,
                                                                     $"{fileName}_fragment_{i}.phyre");
                                    File.WriteAllBytes(fragmentFile, fragments[i]);

                                    ProcessPhyreFile(fragmentFile, fileName, i);
                                }
                            }
                            else if (CheckFileHeader(fileData, 0, RYHP_MAGIC))
                            {
                                ProcessPhyreFile(filePath, fileName);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"处理 {Path.GetFileName(filePath)} 时出错: {ex.Message}");
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取失败: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
