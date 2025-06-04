﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class CdxaExtractor : BaseExtractor
    {
        public static readonly byte[] XA_SIG =
            new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

        public static readonly byte[] XA_RIFF_HEADER =
            new byte[] {
                0x52, 0x49, 0x46, 0x46, 0x84, 0x6E, 0x7D, 0x00, 0x43, 0x44, 0x58, 0x41, 0x66, 0x6D, 0x74, 0x20,
                0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x55, 0x58, 0x41, 0x01, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x64, 0x61, 0x74, 0x61, 0x60, 0x6E, 0x7D, 0x00
            };

        public static readonly byte[] XA_SILENT_FRAME =
            new byte[] {
                0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };

        public const int NUM_SILENT_FRAMES_FOR_SILENT_BLOCK = 1;
        public const int BLOCK_HEADER_SIZE = 0x18;

        public const long FILESIZE_OFFSET = 0x04;
        public const long DATA_LENGTH_OFFSET = 0x28;

        public const int XA_BLOCK_SIZE = 2352;
        public const int XA_TRACK_OFFSET = 0x10;
        public const int XA_TRACK_SIZE = 0x04;
        public const string XA_FILE_EXTENSION = ".xa";

        public const int XA_CHUNK_ID_DIGITS = 0x64;
        public const int XA_AUDIO_MASK = 0x04;
        public static readonly int XA_END_OF_TRACK_MARKER = 1 << 7;
        public static readonly int XA_END_OF_AUDIO_MARKER = 1 << 0;

        private void OnProgressUpdated()
        {
            Console.WriteLine($"提取进度: {ExtractedFileCount}/{TotalFilesToExtract}");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            var files = Directory.EnumerateFiles(directoryPath, "*.xa", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase));

            TotalFilesToExtract = files.Count();
            Console.WriteLine($"目录中的源XA文件数量为: {TotalFilesToExtract}");

            foreach (string filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    int count = await ExtractXaFilesAsync(filePath, extractedDir, cancellationToken);
                    Interlocked.Add(ref _extractedFileCount, count);
                    OnProgressUpdated();
                }
                catch (Exception ex)
                {
                    OnExtractionFailed($"处理文件 {filePath} 时出错: {ex.Message}");
                }
            }

            OnExtractionCompleted();

            int actualExtractedFileCount = Directory.EnumerateFiles(extractedDir, "*.xa", SearchOption.AllDirectories).Count();

            Console.WriteLine($"总共提取了 {ExtractedFileCount} 个文件，实际在Extracted文件夹中的文件数量为: {actualExtractedFileCount}");
            if (ExtractedFileCount != actualExtractedFileCount)
            {
                Console.WriteLine("警告: 统计数量与实际数量不符，可能存在文件操作异常。");
            }
        }

        private async Task<int> ExtractXaFilesAsync(string filePath, string extractedDir, CancellationToken cancellationToken)
        {
            int fileCount = 0;
            string baseFilename = Path.GetFileNameWithoutExtension(filePath);

            string sourceFileSubDir = Path.Combine(extractedDir, baseFilename);
            Directory.CreateDirectory(sourceFileSubDir);

            ExtractCdxaWorker extractor = new ExtractCdxaWorker();
            ExtractCdxaWorker.ExtractCdxaStruct extractCdxaStruct = new ExtractCdxaWorker.ExtractCdxaStruct
            {
                AddRiffHeader = true,
                PatchByte0x11 = true,
                SilentFramesCount = 10,
                SourcePath = filePath,
                FilterAgainstBlockId = true,
                DoTwoPass = true,
                UseSilentBlocksForEof = true,
                UseEndOfTrackMarkerForEof = true,
                OutputDirectory = sourceFileSubDir
            };

            List<string> extractedFileNames = await Task.Run(() =>
                extractor.ExtractCdxa(extractCdxaStruct), cancellationToken);

            foreach (string extractedFilePath in extractedFileNames)
            {
                if (File.Exists(extractedFilePath))
                {
                    try
                    {
                        string fileName = Path.GetFileName(extractedFilePath);
                        string newFileName = Path.Combine(extractedDir, fileName);
                        if (File.Exists(newFileName))
                        {
                            int counter = 1;
                            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                            string fileExt = Path.GetExtension(fileName);
                            do
                            {
                                newFileName = Path.Combine(extractedDir,
                                    $"{fileNameWithoutExt}_{counter}{fileExt}");
                                counter++;
                            }
                            while (File.Exists(newFileName));
                        }

                        File.Move(extractedFilePath, newFileName);
                        OnFileExtracted(newFileName);
                        fileCount++;
                    }
                    catch (Exception ex)
                    {
                        OnExtractionFailed($"移动文件 {extractedFilePath} 时出错: {ex.Message}");
                    }
                }
            }

            try
            {
                Directory.Delete(sourceFileSubDir, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理临时目录 {sourceFileSubDir} 失败: {ex.Message}");
            }

            return fileCount;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public struct ExtractXaStruct
        {
            public string Path;
            public bool AddRiffHeader;
            public bool PatchByte0x11;
            public uint SilentFramesCount;
            public bool FilterAgainstBlockId;
            public bool DoTwoPass;
            public bool UseSilentBlocksForEof;
            public bool UseEndOfTrackMarkerForEof;
            public string OutputDirectory;
        }

        public struct CdxaWriterStruct
        {
            public FileStream FileWriter;
            public long CurrentChunkOffset;
            public bool NonSilentBlockDetected;
        }

        public class CdxaUtil
        {
            const long EMPTY_BLOCK_OFFSET_FLAG = -1;
            const float MINIMUM_BLOCK_DISTANCE_PERCENTAGE = 0.15f;

            public static List<string> ExtractXaFiles(ExtractXaStruct pExtractXaStruct)
            {
                List<string> extractedFileNames = new List<string>();
                Dictionary<uint, CdxaWriterStruct> bwDictionary = new Dictionary<uint, CdxaWriterStruct>();
                List<uint> bwKeys;

                long offset;
                byte[] trackId;
                byte[] buffer = new byte[CdxaExtractor.XA_BLOCK_SIZE];
                uint trackKey;

                byte[] tempTrackId;
                uint tempTrackKey;

                long previousOffset;
                long distanceBetweenBlocks;
                long distanceCeiling = EMPTY_BLOCK_OFFSET_FLAG;
                Dictionary<long, int> distanceFrequency = new Dictionary<long, int>();

                CdxaWriterStruct workingStruct = new CdxaWriterStruct();

                string outputFileName;
                string outputDirectory = pExtractXaStruct.OutputDirectory ??
                    Path.GetDirectoryName(pExtractXaStruct.Path) ??
                    string.Empty;

                int totalPasses = 1;
                bool doFileWrite = false;

                if (pExtractXaStruct.DoTwoPass)
                {
                    totalPasses = 2;
                }

                using (FileStream fs = File.OpenRead(pExtractXaStruct.Path))
                {
                    for (int currentPass = 1; currentPass <= totalPasses; currentPass++)
                    {
                        if (currentPass == totalPasses)
                        {
                            doFileWrite = true;
                        }

                        offset = GetNextOffset(fs, 0, CdxaExtractor.XA_SIG);

                        if (offset != -1)
                        {
                            if (!Directory.Exists(outputDirectory))
                            {
                                Directory.CreateDirectory(outputDirectory);
                            }

                            while ((offset != -1) && ((offset + CdxaExtractor.XA_BLOCK_SIZE) <= fs.Length))
                            {
                                trackId = ParseSimpleOffset(fs, offset + CdxaExtractor.XA_TRACK_OFFSET, CdxaExtractor.XA_TRACK_SIZE);
                                trackKey = GetTrackKey(trackId);

                                if (pExtractXaStruct.UseEndOfTrackMarkerForEof &&
                                    (
                                      ((trackId[2] & CdxaExtractor.XA_END_OF_TRACK_MARKER) == CdxaExtractor.XA_END_OF_TRACK_MARKER) ||
                                      ((trackId[2] & CdxaExtractor.XA_END_OF_AUDIO_MARKER) == CdxaExtractor.XA_END_OF_AUDIO_MARKER)
                                    ))
                                {
                                    tempTrackId = trackId;
                                    tempTrackId[2] = (byte)CdxaExtractor.XA_CHUNK_ID_DIGITS;
                                    tempTrackKey = GetTrackKey(tempTrackId);

                                    if (bwDictionary.ContainsKey(tempTrackKey))
                                    {
                                        if (doFileWrite)
                                        {
                                            workingStruct = bwDictionary[tempTrackKey];
                                            workingStruct.CurrentChunkOffset = offset;
                                            bwDictionary[tempTrackKey] = workingStruct;

                                            buffer = ParseSimpleOffset(fs, offset, CdxaExtractor.XA_BLOCK_SIZE);

                                            if (pExtractXaStruct.PatchByte0x11)
                                            {
                                                buffer[0x11] = 0x00;
                                            }

                                            bwDictionary[tempTrackKey].FileWriter.Write(buffer, 0, buffer.Length);

                                            if (!bwDictionary[tempTrackKey].NonSilentBlockDetected)
                                            {
                                                workingStruct = bwDictionary[tempTrackKey];
                                                workingStruct.NonSilentBlockDetected = true;
                                                bwDictionary[tempTrackKey] = workingStruct;
                                            }

                                            string fileName = FixHeaderAndCloseWriter(bwDictionary[tempTrackKey].FileWriter, pExtractXaStruct,
                                                bwDictionary[tempTrackKey].NonSilentBlockDetected);

                                            if (!string.IsNullOrEmpty(fileName))
                                            {
                                                extractedFileNames.Add(fileName);
                                            }
                                        }

                                        bwDictionary.Remove(tempTrackKey);
                                    }

                                    offset += CdxaExtractor.XA_BLOCK_SIZE;
                                }
                                else if ((pExtractXaStruct.FilterAgainstBlockId) &&
                                         ((trackId[2] & 0x0F) != CdxaExtractor.XA_AUDIO_MASK))
                                {
                                    offset = GetNextOffset(fs, offset + 1, CdxaExtractor.XA_SIG);
                                }
                                else
                                {
                                    if ((doFileWrite) &&
                                        (bwDictionary.ContainsKey(trackKey)) &&
                                        (distanceCeiling != EMPTY_BLOCK_OFFSET_FLAG) &&
                                        (bwDictionary[trackKey].CurrentChunkOffset != EMPTY_BLOCK_OFFSET_FLAG))
                                    {
                                        previousOffset = bwDictionary[trackKey].CurrentChunkOffset;
                                        distanceBetweenBlocks = offset - previousOffset;

                                        if (distanceBetweenBlocks > distanceCeiling)
                                        {
                                            string fileName = FixHeaderAndCloseWriter(bwDictionary[trackKey].FileWriter, pExtractXaStruct,
                                                bwDictionary[trackKey].NonSilentBlockDetected);

                                            if (!string.IsNullOrEmpty(fileName))
                                            {
                                                extractedFileNames.Add(fileName);
                                            }

                                            bwDictionary.Remove(trackKey);
                                        }
                                    }

                                    if (!bwDictionary.ContainsKey(trackKey))
                                    {
                                        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(pExtractXaStruct.Path);
                                        int fileCountInDir = Directory.GetFiles(outputDirectory, fileNameWithoutExtension + "*" + CdxaExtractor.XA_FILE_EXTENSION).Length;
                                        outputFileName = Path.Combine(outputDirectory, fileNameWithoutExtension + "_" + fileCountInDir.ToString("D4") + CdxaExtractor.XA_FILE_EXTENSION);

                                        workingStruct = new CdxaWriterStruct();
                                        workingStruct.CurrentChunkOffset = EMPTY_BLOCK_OFFSET_FLAG;
                                        workingStruct.NonSilentBlockDetected = false;

                                        if (doFileWrite)
                                        {
                                            workingStruct.FileWriter = File.Open(outputFileName, FileMode.Create, FileAccess.ReadWrite);
                                        }

                                        bwDictionary.Add(trackKey, workingStruct);

                                        if (doFileWrite && pExtractXaStruct.AddRiffHeader)
                                        {
                                            bwDictionary[trackKey].FileWriter.Write(CdxaExtractor.XA_RIFF_HEADER, 0, CdxaExtractor.XA_RIFF_HEADER.Length);
                                        }
                                    }

                                    if ((!doFileWrite) &&
                                        (bwDictionary[trackKey].CurrentChunkOffset != EMPTY_BLOCK_OFFSET_FLAG))
                                    {
                                        previousOffset = bwDictionary[trackKey].CurrentChunkOffset;
                                        distanceBetweenBlocks = offset - previousOffset;

                                        if (!distanceFrequency.ContainsKey(distanceBetweenBlocks))
                                        {
                                            distanceFrequency.Add(distanceBetweenBlocks, 1);
                                        }
                                        else
                                        {
                                            distanceFrequency[distanceBetweenBlocks]++;
                                        }
                                    }

                                    workingStruct = bwDictionary[trackKey];
                                    workingStruct.CurrentChunkOffset = offset;
                                    bwDictionary[trackKey] = workingStruct;

                                    buffer = ParseSimpleOffset(fs, offset, CdxaExtractor.XA_BLOCK_SIZE);

                                    if ((pExtractXaStruct.UseSilentBlocksForEof) && IsSilentBlock(buffer, pExtractXaStruct))
                                    {
                                        if (doFileWrite)
                                        {
                                            string fileName = FixHeaderAndCloseWriter(bwDictionary[trackKey].FileWriter, pExtractXaStruct,
                                                bwDictionary[trackKey].NonSilentBlockDetected);

                                            if (!string.IsNullOrEmpty(fileName))
                                            {
                                                extractedFileNames.Add(fileName);
                                            }
                                        }

                                        bwDictionary.Remove(trackKey);
                                    }
                                    else if (doFileWrite)
                                    {
                                        if (pExtractXaStruct.PatchByte0x11)
                                        {
                                            buffer[0x11] = 0x00;
                                        }

                                        bwDictionary[trackKey].FileWriter.Write(buffer, 0, buffer.Length);

                                        if (!bwDictionary[trackKey].NonSilentBlockDetected)
                                        {
                                            workingStruct = bwDictionary[trackKey];
                                            workingStruct.NonSilentBlockDetected = true;
                                            bwDictionary[trackKey] = workingStruct;
                                        }
                                    }

                                    offset += CdxaExtractor.XA_BLOCK_SIZE;
                                }
                            }

                            bwKeys = new List<uint>(bwDictionary.Keys);
                            foreach (uint keyname in bwKeys)
                            {
                                if (doFileWrite)
                                {
                                    string fileName = FixHeaderAndCloseWriter(bwDictionary[keyname].FileWriter, pExtractXaStruct,
                                        bwDictionary[keyname].NonSilentBlockDetected);

                                    if (!string.IsNullOrEmpty(fileName))
                                    {
                                        extractedFileNames.Add(fileName);
                                    }
                                }

                                bwDictionary.Remove(keyname);
                            }

                            if (!doFileWrite)
                            {
                                distanceCeiling = GetDistanceCeiling(distanceFrequency);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                return extractedFileNames;
            }

            private static string FixHeaderAndCloseWriter(FileStream pFs, ExtractXaStruct pExtractXaStruct,
                bool nonSilentBlockFound)
            {
                string filename = pFs.Name;

                if (pExtractXaStruct.AddRiffHeader)
                {
                    uint xaFileSize = (uint)pFs.Length;

                    pFs.Position = CdxaExtractor.FILESIZE_OFFSET;
                    pFs.Write(BitConverter.GetBytes(xaFileSize - 8), 0, 4);

                    pFs.Position = CdxaExtractor.DATA_LENGTH_OFFSET;
                    pFs.Write(BitConverter.GetBytes((uint)(xaFileSize - CdxaExtractor.XA_RIFF_HEADER.Length)), 0, 4);
                }

                pFs.Close();
                pFs.Dispose();

                if (!nonSilentBlockFound && File.Exists(filename))
                {
                    File.Delete(filename);
                    return string.Empty;
                }

                return filename;
            }

            private static bool IsSilentBlock(byte[] pCdxaBlock, ExtractXaStruct pExtractXaStruct)
            {
                bool ret = false;
                int silentFrameCount = 0;
                long bufferOffset = 0;

                while ((bufferOffset = GetNextOffset(pCdxaBlock, bufferOffset, CdxaExtractor.XA_SILENT_FRAME)) > -1)
                {
                    silentFrameCount++;
                    bufferOffset += 1;
                }

                if (silentFrameCount >= pExtractXaStruct.SilentFramesCount)
                {
                    ret = true;
                }

                return ret;
            }

            private static long GetDistanceCeiling(Dictionary<long, int> distanceFrequencyList)
            {
                long distanceCeiling = EMPTY_BLOCK_OFFSET_FLAG;
                long totalBlockCount = 0;

                foreach (long key in distanceFrequencyList.Keys)
                {
                    totalBlockCount += distanceFrequencyList[key];
                }

                foreach (long key in distanceFrequencyList.Keys)
                {
                    if ((key > distanceCeiling) &&
                        ((((float)distanceFrequencyList[key] / (float)totalBlockCount) >= MINIMUM_BLOCK_DISTANCE_PERCENTAGE)))
                    {
                        distanceCeiling = key;
                    }
                }

                return distanceCeiling;
            }

            private static uint GetTrackKey(byte[] trackIdBytes)
            {
                uint ret = BitConverter.ToUInt32(trackIdBytes, 0);
                ret &= 0xFF00FFFF;

                return ret;
            }

            private static long GetNextOffset(Stream stream, long startOffset, byte[] pattern)
            {
                stream.Position = startOffset;
                byte[] buffer = new byte[pattern.Length];
                while (stream.Position <= stream.Length - pattern.Length)
                {
                    stream.Read(buffer, 0, pattern.Length);
                    if (CompareByteArrays(buffer, pattern))
                    {
                        return stream.Position - pattern.Length;
                    }
                    stream.Position = stream.Position - pattern.Length + 1;
                }
                return -1;
            }

            private static long GetNextOffset(byte[] buffer, long startOffset, byte[] pattern)
            {
                for (long i = startOffset; i <= buffer.Length - pattern.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < pattern.Length; j++)
                    {
                        if (buffer[i + j] != pattern[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        return i;
                    }
                }
                return -1;
            }

            private static byte[] ParseSimpleOffset(Stream stream, long offset, int length)
            {
                byte[] buffer = new byte[length];
                stream.Position = offset;
                stream.Read(buffer, 0, length);
                return buffer;
            }

            private static bool CompareByteArrays(byte[] a1, byte[] a2)
            {
                if (a1.Length != a2.Length)
                {
                    return false;
                }
                for (int i = 0; i < a1.Length; i++)
                {
                    if (a1[i] != a2[i])
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public class ExtractCdxaWorker
        {
            public struct ExtractCdxaStruct
            {
                public bool AddRiffHeader;
                public bool PatchByte0x11;
                public uint SilentFramesCount;
                public string SourcePath;
                public bool FilterAgainstBlockId;
                public bool DoTwoPass;
                public bool UseSilentBlocksForEof;
                public bool UseEndOfTrackMarkerForEof;
                public string OutputDirectory; 
            }

            public List<string> ExtractCdxa(ExtractCdxaStruct extractCdxaStruct)
            {
                ExtractXaStruct extStruct = new ExtractXaStruct();
                extStruct.Path = extractCdxaStruct.SourcePath;
                extStruct.AddRiffHeader = extractCdxaStruct.AddRiffHeader;
                extStruct.PatchByte0x11 = extractCdxaStruct.PatchByte0x11;
                extStruct.SilentFramesCount = extractCdxaStruct.SilentFramesCount;
                extStruct.FilterAgainstBlockId = extractCdxaStruct.FilterAgainstBlockId;
                extStruct.DoTwoPass = extractCdxaStruct.DoTwoPass;
                extStruct.UseSilentBlocksForEof = extractCdxaStruct.UseSilentBlocksForEof;
                extStruct.UseEndOfTrackMarkerForEof = extractCdxaStruct.UseEndOfTrackMarkerForEof;
                extStruct.OutputDirectory = extractCdxaStruct.OutputDirectory;

                return CdxaUtil.ExtractXaFiles(extStruct);
            }
        }
    }
}