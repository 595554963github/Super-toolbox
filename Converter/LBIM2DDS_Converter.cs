using System.Runtime.InteropServices;
using System.IO.Compression;

namespace super_toolbox
{
    public class LBIM2DDS_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct DDS_PIXELFORMAT
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwFourCC;
            public uint dwRGBBitCount;
            public uint dwRBitMask;
            public uint dwGBitMask;
            public uint dwBBitMask;
            public uint dwABitMask;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct DDS_HEADER
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwHeight;
            public uint dwWidth;
            public uint dwPitchOrLinearSize;
            public uint dwDepth;
            public uint dwMipMapCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
            public uint[] dwReserved1;
            public DDS_PIXELFORMAT ddspf;
            public uint dwCaps;
            public uint dwCaps2;
            public uint dwCaps3;
            public uint dwCaps4;
            public uint dwReserved2;
        }

        private enum LBIMFormat
        {
            LBIM_BC1_UNORM = 66,
            LBIM_BC2_UNORM = 67,
            LBIM_BC3_UNORM = 68,
            LBIM_BC4_UNORM = 73,
            LBIM_BC5_UNORM = 75,
            LBIM_R8_G8_B8_A8_UNORM = 37
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct LBIMHeader
        {
            public int datasize;
            public int headersize;
            public int width;
            public int height;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public int[] unk0;
            public int format;
            public int unk1;
            public int version;
            public int magic;
        }

        private class ConvertLBIM_Out
        {
            public int result;
            public byte[]? outBuffer;
            public int outBufferSize;
            public DDS_HEADER ddsHeader;
        }

        private static readonly byte[] DDS_MAGIC = new byte[] { 0x44, 0x44, 0x53, 0x20 };
        private static readonly byte[] XBC1_HEADER = new byte[] { 0x78, 0x62, 0x63, 0x31 };
        private static readonly byte[] LBIM_MAGIC = new byte[] { 0x4C, 0x42, 0x49, 0x4D };

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var allFiles = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToConvert = allFiles.Count;
            int successCount = 0;
            int skippedCount = 0;
            int processedCount = 0;
            int totalBlocksProcessed = 0;

            try
            {
                foreach (var filePath in allFiles)
                {
                    processedCount++;
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理({processedCount}/{TotalFilesToConvert}):{Path.GetFileName(filePath)}");

                    try
                    {
                        byte[] fileData = await File.ReadAllBytesAsync(filePath, cancellationToken);
                        List<byte[]> splitDatas = new List<byte[]>();

                        if (Path.GetExtension(filePath).Equals(".wismda", StringComparison.OrdinalIgnoreCase) &&
                            fileData.Length >= 4 &&
                            fileData.Take(4).SequenceEqual(XBC1_HEADER))
                        {
                            ConversionProgress?.Invoke(this, "检测到.wismda文件，开始分割xbc1数据块...");
                            splitDatas = SplitXBC1Data(fileData);
                            ConversionProgress?.Invoke(this, $"分割完成，共{splitDatas.Count}个数据块");
                        }
                        else
                        {
                            splitDatas.Add(fileData);
                        }

                        int blockIndex = 0;
                        int fileSuccess = 0;
                        int fileSkipped = 0;

                        foreach (var dataBlock in splitDatas)
                        {
                            blockIndex++;
                            totalBlocksProcessed++; 

                            if (splitDatas.Count > 1)
                            {
                                ConversionProgress?.Invoke(this, $"处理第{blockIndex}/{splitDatas.Count}个数据块");
                            }

                            byte[] processedData = dataBlock;
                            if (processedData.Length >= 4 && processedData.Take(4).SequenceEqual(XBC1_HEADER))
                            {
                                ConversionProgress?.Invoke(this, "检测到xbc1文件头，移除前48字节...");
                                if (processedData.Length > 48)
                                {
                                    byte[] compressedData = new byte[processedData.Length - 48];
                                    Array.Copy(processedData, 48, compressedData, 0, compressedData.Length);
                                    processedData = compressedData;
                                    ConversionProgress?.Invoke(this, "已移除xbc1头48字节");
                                }
                            }
                            if (processedData.Length >= 2 && processedData[0] == 0x78 &&
                                (processedData[1] == 0x01 || processedData[1] == 0x9C || processedData[1] == 0xDA))
                            {
                                ConversionProgress?.Invoke(this, "检测到zlib压缩数据，开始解压...");
                                try
                                {
                                    using (var compressedStream = new MemoryStream(processedData, 2, processedData.Length - 2))
                                    using (var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
                                    using (var resultStream = new MemoryStream())
                                    {
                                        await deflateStream.CopyToAsync(resultStream, cancellationToken);
                                        processedData = resultStream.ToArray();
                                        ConversionProgress?.Invoke(this, "zlib解压成功");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ConversionProgress?.Invoke(this, $"zlib解压失败:{ex.Message}");
                                }
                            }
                            else
                            {
                                ConversionProgress?.Invoke(this, "未检测到zlib压缩数据，跳过解压");
                            }
                            if (processedData.Length >= 4 &&
                                processedData.Skip(processedData.Length - 4).Take(4).SequenceEqual(LBIM_MAGIC))
                            {
                                ConversionProgress?.Invoke(this, "检测到LBIM文件尾，开始转换...");

                                string fileName = Path.GetFileNameWithoutExtension(filePath);
                                string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                                string baseName = $"{fileName}_{blockIndex}";
                                if (splitDatas.Count == 1)
                                {
                                    baseName = fileName;
                                }
                                string ddsFilePath = Path.Combine(fileDirectory, $"{baseName}.dds");

                                bool conversionSuccess = await ConvertLBIMDataToDDSAsync(processedData, ddsFilePath, cancellationToken);
                                if (conversionSuccess && File.Exists(ddsFilePath))
                                {
                                    fileSuccess++;
                                    successCount++;
                                    ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(ddsFilePath)}");
                                    OnFileConverted(ddsFilePath);
                                }
                                else
                                {
                                    fileSkipped++;
                                    skippedCount++;
                                    ConversionError?.Invoke(this, $"{Path.GetFileName(filePath)}转换失败");
                                    OnConversionFailed($"{Path.GetFileName(filePath)}转换失败");
                                }
                            }
                            else
                            {
                                fileSkipped++;
                                skippedCount++;
                                ConversionProgress?.Invoke(this, "未找到LBIM文件尾，跳过当前数据块");
                            }

                            if (splitDatas.Count > 1)
                            {
                                ConversionProgress?.Invoke(this, $"--- 第{blockIndex}个数据块处理完成 ---");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        skippedCount++;
                        ConversionError?.Invoke(this, $"处理异常:{ex.Message}");
                        OnConversionFailed($"{Path.GetFileName(filePath)} 处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    int totalCount = successCount + skippedCount;
                    ConversionProgress?.Invoke(this, $"转换完成，成功转换{successCount}个文件，跳过{skippedCount}个数据块");
                    ConversionProgress?.Invoke(this, $"总计处理数据块: {totalCount} 个");

                    if (totalBlocksProcessed == totalCount)
                    {
                        ConversionProgress?.Invoke(this, $"数据统计正确: {totalBlocksProcessed} = {successCount}(成功) + {skippedCount}(跳过)");
                    }
                    else
                    {
                        ConversionProgress?.Invoke(this, $"数据统计差异: 处理了{totalBlocksProcessed}个数据块，但统计为{totalCount}个");
                    }
                }
                else
                {
                    ConversionProgress?.Invoke(this, "未找到任何有效的LBIM文件");
                }
                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnConversionFailed($"严重错误:{ex.Message}");
            }
        }
        private List<byte[]> SplitXBC1Data(byte[] fileData)
        {
            List<byte[]> result = new List<byte[]>();
            List<int> xbc1Positions = new List<int>();

            for (int i = 0; i <= fileData.Length - 4; i++)
            {
                if (fileData[i] == XBC1_HEADER[0] &&
                    fileData[i + 1] == XBC1_HEADER[1] &&
                    fileData[i + 2] == XBC1_HEADER[2] &&
                    fileData[i + 3] == XBC1_HEADER[3])
                {
                    xbc1Positions.Add(i);
                }
            }

            if (xbc1Positions.Count == 0)
            {
                result.Add(fileData);
                return result;
            }

            for (int i = 0; i < xbc1Positions.Count; i++)
            {
                int startPos = xbc1Positions[i];
                int endPos = (i < xbc1Positions.Count - 1) ? xbc1Positions[i + 1] : fileData.Length;
                int length = endPos - startPos;
                if (length >= 0)
                {
                    byte[] chunk = new byte[length];
                    if (length > 0)
                    {
                        Array.Copy(fileData, startPos, chunk, 0, length);
                    }
                    result.Add(chunk);
                }
            }

            ConversionProgress?.Invoke(this, $"找到{xbc1Positions.Count}个xbc1文件头，分割为{result.Count}个数据块");
            return result;
        }
        private async Task<bool> ConvertLBIMDataToDDSAsync(byte[] lbimData, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                ConvertLBIM_Out result = ConvertLBIM(lbimData, lbimData.Length, null, 0);
                if (result.result != 0)
                {
                    ConversionError?.Invoke(this, $"LBIM转换失败，错误代码:{result.result}");
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(DDS_MAGIC);
                    writer.Write(result.ddsHeader.dwSize);
                    writer.Write(result.ddsHeader.dwFlags);
                    writer.Write(result.ddsHeader.dwHeight);
                    writer.Write(result.ddsHeader.dwWidth);
                    writer.Write(result.ddsHeader.dwPitchOrLinearSize);
                    writer.Write(result.ddsHeader.dwDepth);
                    writer.Write(result.ddsHeader.dwMipMapCount);
                    for (int i = 0; i < 11; i++)
                    {
                        writer.Write(result.ddsHeader.dwReserved1[i]);
                    }
                    writer.Write(result.ddsHeader.ddspf.dwSize);
                    writer.Write(result.ddsHeader.ddspf.dwFlags);
                    writer.Write(result.ddsHeader.ddspf.dwFourCC);
                    writer.Write(result.ddsHeader.ddspf.dwRGBBitCount);
                    writer.Write(result.ddsHeader.ddspf.dwRBitMask);
                    writer.Write(result.ddsHeader.ddspf.dwGBitMask);
                    writer.Write(result.ddsHeader.ddspf.dwBBitMask);
                    writer.Write(result.ddsHeader.ddspf.dwABitMask);
                    writer.Write(result.ddsHeader.dwCaps);
                    writer.Write(result.ddsHeader.dwCaps2);
                    writer.Write(result.ddsHeader.dwCaps3);
                    writer.Write(result.ddsHeader.dwCaps4);
                    writer.Write(result.ddsHeader.dwReserved2);
                    if (result.outBuffer != null)
                    {
                        writer.Write(result.outBuffer, 0, result.outBufferSize);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"DDS写入异常:{ex.Message}");
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { }
                }
                return false;
            }
        }

        private static LBIMHeader ReadLBIMHeader(byte[] buffer, int size)
        {
            int headerSize = Marshal.SizeOf(typeof(LBIMHeader));
            byte[] headerBytes = new byte[headerSize];
            Array.Copy(buffer, size - headerSize, headerBytes, 0, headerSize);
            LBIMHeader header;
            GCHandle handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                object? structObj = Marshal.PtrToStructure(ptr, typeof(LBIMHeader));
                header = structObj != null ? (LBIMHeader)structObj : default;
                if (header.unk0 == null)
                {
                    header.unk0 = new int[2];
                }
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
            return header;
        }

        private static ConvertLBIM_Out ConvertLBIM(byte[] buffer, int size, byte[]? exBuffer, int exBuffSize)
        {
            LBIMHeader header = ReadLBIMHeader(buffer, size);
            ConvertLBIM_Out retVal = new ConvertLBIM_Out();
            if (header.magic != 0x4D49424C)
            {
                retVal.result = 1;
                return retVal;
            }

            if (exBuffSize > 0)
            {
                header.width *= 2;
                header.height *= 2;
            }

            DDS_PIXELFORMAT pixelFormat = new DDS_PIXELFORMAT();
            int bpp = 0;
            int ppb = 1;

            switch ((LBIMFormat)header.format)
            {
                case LBIMFormat.LBIM_BC1_UNORM:
                    bpp = 8;
                    ppb = 4;
                    pixelFormat.dwSize = 32;
                    pixelFormat.dwFlags = 0x04;
                    pixelFormat.dwFourCC = 0x31545844;
                    pixelFormat.dwRGBBitCount = 0;
                    pixelFormat.dwRBitMask = 0;
                    pixelFormat.dwGBitMask = 0;
                    pixelFormat.dwBBitMask = 0;
                    pixelFormat.dwABitMask = 0;
                    break;
                case LBIMFormat.LBIM_BC2_UNORM:
                    bpp = 16;
                    ppb = 4;
                    pixelFormat.dwSize = 32;
                    pixelFormat.dwFlags = 0x04;
                    pixelFormat.dwFourCC = 0x33545844;
                    pixelFormat.dwRGBBitCount = 0;
                    pixelFormat.dwRBitMask = 0;
                    pixelFormat.dwGBitMask = 0;
                    pixelFormat.dwBBitMask = 0;
                    pixelFormat.dwABitMask = 0;
                    break;
                case LBIMFormat.LBIM_BC3_UNORM:
                    bpp = 16;
                    ppb = 4;
                    pixelFormat.dwSize = 32;
                    pixelFormat.dwFlags = 0x04;
                    pixelFormat.dwFourCC = 0x35545844;
                    pixelFormat.dwRGBBitCount = 0;
                    pixelFormat.dwRBitMask = 0;
                    pixelFormat.dwGBitMask = 0;
                    pixelFormat.dwBBitMask = 0;
                    pixelFormat.dwABitMask = 0;
                    break;
                case LBIMFormat.LBIM_BC4_UNORM:
                    bpp = 8;
                    ppb = 4;
                    pixelFormat.dwSize = 32;
                    pixelFormat.dwFlags = 0x04;
                    pixelFormat.dwFourCC = 0x31495441;
                    pixelFormat.dwRGBBitCount = 0;
                    pixelFormat.dwRBitMask = 0;
                    pixelFormat.dwGBitMask = 0;
                    pixelFormat.dwBBitMask = 0;
                    pixelFormat.dwABitMask = 0;
                    break;
                case LBIMFormat.LBIM_BC5_UNORM:
                    bpp = 16;
                    ppb = 4;
                    pixelFormat.dwSize = 32;
                    pixelFormat.dwFlags = 0x04;
                    pixelFormat.dwFourCC = 0x32495441;
                    pixelFormat.dwRGBBitCount = 0;
                    pixelFormat.dwRBitMask = 0;
                    pixelFormat.dwGBitMask = 0;
                    pixelFormat.dwBBitMask = 0;
                    pixelFormat.dwABitMask = 0;
                    break;
                case LBIMFormat.LBIM_R8_G8_B8_A8_UNORM:
                    bpp = 4;
                    ppb = 1;
                    pixelFormat.dwSize = 32;
                    pixelFormat.dwFlags = 0x41;
                    pixelFormat.dwFourCC = 0;
                    pixelFormat.dwRGBBitCount = 32;
                    pixelFormat.dwRBitMask = 0x000000FF;
                    pixelFormat.dwGBitMask = 0x0000FF00;
                    pixelFormat.dwBBitMask = 0x00FF0000;
                    pixelFormat.dwABitMask = 0xFF000000;
                    break;
                default:
                    retVal.result = 2;
                    return retVal;
            }

            int blockWidth = (header.width + ppb - 1) / ppb;
            int blockHeight = (header.height + ppb - 1) / ppb;

            DDS_HEADER ddsHeader = new DDS_HEADER();
            ddsHeader.dwSize = 124;

            if ((LBIMFormat)header.format == LBIMFormat.LBIM_R8_G8_B8_A8_UNORM)
            {
                ddsHeader.dwFlags = 0x81007;
                ddsHeader.dwPitchOrLinearSize = (uint)(header.width * 4);
            }
            else
            {
                ddsHeader.dwFlags = 0x1007;
                ddsHeader.dwPitchOrLinearSize = 0;
            }

            ddsHeader.dwHeight = (uint)header.height;
            ddsHeader.dwWidth = (uint)header.width;
            ddsHeader.dwDepth = 0;
            ddsHeader.dwMipMapCount = 0;
            ddsHeader.dwReserved1 = new uint[11];
            ddsHeader.ddspf = pixelFormat;
            ddsHeader.dwCaps = 0x1000;
            ddsHeader.dwCaps2 = 0;
            ddsHeader.dwCaps3 = 0;
            ddsHeader.dwCaps4 = 0;
            ddsHeader.dwReserved2 = 0;

            byte[] imageData = ReorderImageData(buffer, header, bpp, ppb, exBuffer, exBuffSize);
            retVal.ddsHeader = ddsHeader;
            retVal.outBuffer = imageData;
            retVal.outBufferSize = imageData.Length;
            retVal.result = 0;
            return retVal;
        }

        private static byte[] ReorderImageData(byte[] buffer, LBIMHeader header, int bpp, int ppb, byte[]? exBuffer, int exBuffSize)
        {
            int width = header.width;
            int height = header.height;
            int blockWidth = width / ppb;
            int blockHeight = height / ppb;
            int surfaceSize = blockWidth * blockHeight * bpp;
            byte[] deswbuffer = new byte[surfaceSize];
            int bppShift = NumLeadingZeroes(bpp);
            int lineShift = NumLeadingZeroes(blockWidth * bpp);
            int xBitsShift = 3;

            for (int i = 0; i < 4; i++)
            {
                if (((blockHeight - 1) & (8 << i)) != 0)
                    xBitsShift++;
            }

            byte[] sourceBuffer = exBuffSize > 0 && exBuffer != null ? exBuffer : buffer;

            for (int w = 0; w < blockWidth; w++)
            {
                for (int h = 0; h < blockHeight; h++)
                {
                    int _X = w << bppShift;
                    int address = (h & 0xff80) << lineShift
                        | ((h & 0x78) << 6)
                        | ((h & 6) << 5)
                        | ((h & 1) << 4)
                        | ((_X & 0xffc0) << xBitsShift)
                        | ((_X & 0x20) << 3)
                        | ((_X & 0x10) << 1)
                        | (_X & 0xf);

                    if (address + bpp <= sourceBuffer.Length && (h * blockWidth + w) * bpp + bpp <= deswbuffer.Length)
                    {
                        int destOffset = (h * blockWidth + w) * bpp;
                        Array.Copy(sourceBuffer, address, deswbuffer, destOffset, bpp);
                    }
                }
            }

            return deswbuffer;
        }

        private static int NumLeadingZeroes(int value)
        {
            if (value == 0)
                return 0;
            int numZeros = 0;
            for (; (((value >> numZeros) & 1) == 0); numZeros++) { }
            return numZeros;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}