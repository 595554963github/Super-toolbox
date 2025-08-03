﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class DdsExtractor : BaseExtractor
    {
        private readonly object _lockObject = new object();
        private const int BufferSize = 8192;

        public event EventHandler<string>? ExtractionProgress;

        private const uint DDS_MAGIC = 0x20534444; // "DDS "
        private const int DDS_HEADER_SIZE = 124;
        private const int DX10_HEADER_SIZE = 20;

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionProgress?.Invoke(this, $"错误: {directoryPath} 不是有效的目录");
                OnExtractionFailed($"错误: {directoryPath} 不是有效的目录");
                return;
            }

            string extractedFolder = Path.Combine(directoryPath, "Extracted");
            if (!Directory.Exists(extractedFolder))
            {
                Directory.CreateDirectory(extractedFolder);
            }

            var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            TotalFilesToExtract = files.Length;
            ExtractionProgress?.Invoke(this, $"开始处理 {files.Length} 个文件...");
            OnFileExtracted($"开始处理 {files.Length} 个文件...");

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await ProcessFileAsync(file, extractedFolder, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionProgress?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionProgress?.Invoke(this, $"处理文件 {file} 时出错: {ex.Message}");
                    OnExtractionFailed($"处理文件 {file} 时出错: {ex.Message}");
                }
            }

            ExtractionProgress?.Invoke(this, $"提取完成:共提取{ExtractedFileCount}个DDS文件");
        }

        private async Task ProcessFileAsync(string filePath, string extractedFolder, CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileName(filePath);

            using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var buffer = new byte[BufferSize];
            var leftoverBuffer = new List<byte>();
            int bytesRead;
            int fileCount = 0;
            long currentPosition = 0;

            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, BufferSize, cancellationToken)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentData = new List<byte>(leftoverBuffer);
                currentData.AddRange(buffer.Take(bytesRead));
                leftoverBuffer.Clear();

                int ddsPos = FindDdsMagic(currentData, 0);
                if (ddsPos != -1)
                {
                    if (currentData.Count - ddsPos >= 128)
                    {
                        byte[] headerData = currentData.GetRange(ddsPos, 128).ToArray();
                        var header = new DdsHeader(headerData);

                        if (header.IsValid())
                        {
                            bool hasDx10 = header.HasDx10Extension();
                            int dx10Size = hasDx10 ? DX10_HEADER_SIZE : 0;
                            int dataSize = header.CalculateDataSize();
                            int totalSize = 4 + DDS_HEADER_SIZE + dx10Size + dataSize;

                            if (currentData.Count - ddsPos >= totalSize)
                            {
                                byte[] ddsData = currentData.GetRange(ddsPos, totalSize).ToArray();
                                SaveDdsFile(ddsData, extractedFolder, fileName, fileCount++);
                                leftoverBuffer.AddRange(currentData.GetRange(ddsPos + totalSize, currentData.Count - (ddsPos + totalSize)));
                            }
                            else
                            {
                                leftoverBuffer.AddRange(currentData.GetRange(ddsPos, currentData.Count - ddsPos));
                            }
                        }
                        else
                        {
                            leftoverBuffer.AddRange(currentData.GetRange(ddsPos + 4, currentData.Count - (ddsPos + 4)));
                        }
                    }
                    else
                    {
                        leftoverBuffer.AddRange(currentData.GetRange(ddsPos, currentData.Count - ddsPos));
                    }
                }
                else
                {
                    leftoverBuffer.AddRange(currentData.GetRange(Math.Max(0, currentData.Count - 3), Math.Min(3, currentData.Count)));
                }

                currentPosition += bytesRead;
            }
        }

        private void SaveDdsFile(byte[] ddsData, string extractedFolder, string sourceFileName, int fileCount)
        {
            string baseName = Path.GetFileNameWithoutExtension(sourceFileName);
            string newFileName = $"{baseName}_{fileCount}.dds";
            string filePath = Path.Combine(extractedFolder, newFileName);

            try
            {
                File.WriteAllBytes(filePath, ddsData);

                var header = new DdsHeader(ddsData);
                string formatStr = header.GetDxgiFormat(ddsData.Length > 128 + 4 ? ddsData.Skip(128).Take(20).ToArray() : null);

                OnFileExtracted($"已提取: {newFileName} (尺寸: {header.Width}x{header.Height}, 格式: {formatStr})");
                ExtractionProgress?.Invoke(this, $"已提取: {newFileName} (尺寸: {header.Width}x{header.Height}, 格式: {formatStr})");
            }
            catch (Exception ex)
            {
                ExtractionProgress?.Invoke(this, $"保存文件 {newFileName} 时出错: {ex.Message}");
            }
        }

        private int FindDdsMagic(List<byte> data, int startIndex)
        {
            byte[] magicBytes = BitConverter.GetBytes(DDS_MAGIC);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(magicBytes);

            for (int i = startIndex; i <= data.Count - 4; i++)
            {
                if (data[i] == magicBytes[0] &&
                    data[i + 1] == magicBytes[1] &&
                    data[i + 2] == magicBytes[2] &&
                    data[i + 3] == magicBytes[3])
                {
                    return i;
                }
            }
            return -1;
        }
    }

    public class DdsHeader
    {
        public uint Size { get; }
        public uint Flags { get; }
        public uint Height { get; }
        public uint Width { get; }
        public uint PitchOrLinearSize { get; }
        public uint Depth { get; }
        public uint MipmapCount { get; }
        public uint[] Reserved1 { get; } = new uint[11];

        public uint PfSize { get; }
        public uint PfFlags { get; }
        public string PfFourCC { get; }
        public uint PfBitCount { get; }
        public uint PfRmask { get; }
        public uint PfGmask { get; }
        public uint PfBmask { get; }
        public uint PfAmask { get; }

        public uint Caps1 { get; }
        public uint Caps2 { get; }
        public uint Caps3 { get; }
        public uint Caps4 { get; }
        public uint Reserved2 { get; }

        public DdsHeader(byte[] data)
        {
            if (data.Length < 128)
                throw new ArgumentException("DDS头部数据必须至少128字节");

            Size = BitConverter.ToUInt32(data, 4);
            Flags = BitConverter.ToUInt32(data, 8);
            Height = BitConverter.ToUInt32(data, 12);
            Width = BitConverter.ToUInt32(data, 16);
            PitchOrLinearSize = BitConverter.ToUInt32(data, 20);
            Depth = BitConverter.ToUInt32(data, 24);
            MipmapCount = BitConverter.ToUInt32(data, 28);

            for (int i = 0; i < 11; i++)
            {
                Reserved1[i] = BitConverter.ToUInt32(data, 32 + i * 4);
            }

            int pfOffset = 76;
            PfSize = BitConverter.ToUInt32(data, pfOffset);
            PfFlags = BitConverter.ToUInt32(data, pfOffset + 4);
            PfFourCC = System.Text.Encoding.ASCII.GetString(data, pfOffset + 8, 4).TrimEnd('\0');
            PfBitCount = BitConverter.ToUInt32(data, pfOffset + 12);
            PfRmask = BitConverter.ToUInt32(data, pfOffset + 16);
            PfGmask = BitConverter.ToUInt32(data, pfOffset + 20);
            PfBmask = BitConverter.ToUInt32(data, pfOffset + 24);
            PfAmask = BitConverter.ToUInt32(data, pfOffset + 28);

            int capsOffset = 108;
            Caps1 = BitConverter.ToUInt32(data, capsOffset);
            Caps2 = BitConverter.ToUInt32(data, capsOffset + 4);
            Caps3 = BitConverter.ToUInt32(data, capsOffset + 8);
            Caps4 = BitConverter.ToUInt32(data, capsOffset + 12);
            Reserved2 = BitConverter.ToUInt32(data, capsOffset + 16);
        }

        private const int DDS_HEADER_SIZE = 124;
        public bool IsValid()
        {
            return (Size == DDS_HEADER_SIZE &&
                    (Flags & 0x1007) != 0 &&  // DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT
                    Width > 0 && Height > 0 &&
                    PfSize == 32);
        }

        public bool HasDx10Extension()
        {
            return PfFourCC == "DX10";
        }

        public string GetDxgiFormat(byte[]? dx10Data)
        {
            if (!HasDx10Extension())
                return PfFourCC;

            byte[] safeData = dx10Data ?? Array.Empty<byte>();

            if (safeData.Length < 4)
                return "未知格式";

            uint dxgiFormat = BitConverter.ToUInt32(safeData, 0);
            return DxgiFormatNames.TryGetValue(dxgiFormat, out string? format) ? format : $"未知格式({dxgiFormat})";
        }

        public int CalculateDataSize()
        {
            if ((Flags & 0x80000) != 0)  // DDSD_LINEARSIZE
                return (int)PitchOrLinearSize;

            int blockSize;
            switch (PfFourCC)
            {
                case "DXT1":
                case "BC1":
                case "ATI1":
                    blockSize = 8;
                    break;
                case "DXT3":
                case "DXT5":
                case "BC2":
                case "BC3":
                case "ATI2":
                    blockSize = 16;
                    break;
                case "BC4U":
                case "BC4S":
                    blockSize = 4;
                    break;
                case "BC5U":
                case "BC5S":
                    blockSize = 8;
                    break;
                default:
                    blockSize = (int)Math.Max(1, (PfBitCount + 7) / 8) * 4;
                    break;
            }

            int totalSize = 0;
            uint w = Width, h = Height;
            for (int i = 0; i < Math.Max(1, MipmapCount); i++)
            {
                uint blockW = (uint)Math.Max(1, (w + 3) / 4);
                uint blockH = (uint)Math.Max(1, (h + 3) / 4);
                totalSize += (int)(blockW * blockH * blockSize);
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }

            return totalSize;
        }

        private static readonly Dictionary<uint, string> DxgiFormatNames = new Dictionary<uint, string>
        {
            {0, "DXGI_FORMAT_UNKNOWN"},
            {1, "DXGI_FORMAT_R32G32B32A32_TYPELESS"},
            {2, "DXGI_FORMAT_R32G32B32A32_FLOAT"},
            {3, "DXGI_FORMAT_R32G32B32A32_UINT"},
            {4, "DXGI_FORMAT_R32G32B32A32_SINT"},
            {5, "DXGI_FORMAT_R32G32B32_TYPELESS"},
            {6, "DXGI_FORMAT_R32G32B32_FLOAT"},
            {7, "DXGI_FORMAT_R32G32B32_UINT"},
            {8, "DXGI_FORMAT_R32G32B32_SINT"},
            {9, "DXGI_FORMAT_R16G16B16A16_TYPELESS"},
            {10, "DXGI_FORMAT_R16G16B16A16_FLOAT"},
            {11, "DXGI_FORMAT_R16G16B16A16_UNORM"},
            {12, "DXGI_FORMAT_R16G16B16A16_UINT"},
            {13, "DXGI_FORMAT_R16G16B16A16_SNORM"},
            {14, "DXGI_FORMAT_R16G16B16A16_SINT"},
            {15, "DXGI_FORMAT_R32G32_TYPELESS"},
            {16, "DXGI_FORMAT_R32G32_FLOAT"},
            {17, "DXGI_FORMAT_R32G32_UINT"},
            {18, "DXGI_FORMAT_R32G32_SINT"},
            {19, "DXGI_FORMAT_R32G8X24_TYPELESS"},
            {20, "DXGI_FORMAT_D32_FLOAT_S8X24_UINT"},
            {21, "DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS"},
            {22, "DXGI_FORMAT_X32_TYPELESS_G8X24_UINT"},
            {23, "DXGI_FORMAT_R10G10B10A2_TYPELESS"},
            {24, "DXGI_FORMAT_R10G10B10A2_UNORM"},
            {25, "DXGI_FORMAT_R10G10B10A2_UINT"},
            {26, "DXGI_FORMAT_R11G11B10_FLOAT"},
            {27, "DXGI_FORMAT_R8G8B8A8_TYPELESS"},
            {28, "DXGI_FORMAT_R8G8B8A8_UNORM"},
            {29, "DXGI_FORMAT_R8G8B8A8_UNORM_SRGB"},
            {30, "DXGI_FORMAT_R8G8B8A8_UINT"},
            {31, "DXGI_FORMAT_R8G8B8A8_SNORM"},
            {32, "DXGI_FORMAT_R8G8B8A8_SINT"},
            {33, "DXGI_FORMAT_R16G16_TYPELESS"},
            {34, "DXGI_FORMAT_R16G16_FLOAT"},
            {35, "DXGI_FORMAT_R16G16_UNORM"},
            {36, "DXGI_FORMAT_R16G16_UINT"},
            {37, "DXGI_FORMAT_R16G16_SNORM"},
            {38, "DXGI_FORMAT_R16G16_SINT"},
            {39, "DXGI_FORMAT_R32_TYPELESS"},
            {40, "DXGI_FORMAT_D32_FLOAT"},
            {41, "DXGI_FORMAT_R32_FLOAT"},
            {42, "DXGI_FORMAT_R32_UINT"},
            {43, "DXGI_FORMAT_R32_SINT"},
            {44, "DXGI_FORMAT_R24G8_TYPELESS"},
            {45, "DXGI_FORMAT_D24_UNORM_S8_UINT"},
            {46, "DXGI_FORMAT_R24_UNORM_X8_TYPELESS"},
            {47, "DXGI_FORMAT_X24_TYPELESS_G8_UINT"},
            {48, "DXGI_FORMAT_R8G8_TYPELESS"},
            {49, "DXGI_FORMAT_R8G8_UNORM"},
            {50, "DXGI_FORMAT_R8G8_UINT"},
            {51, "DXGI_FORMAT_R8G8_SNORM"},
            {52, "DXGI_FORMAT_R8G8_SINT"},
            {53, "DXGI_FORMAT_R16_TYPELESS"},
            {54, "DXGI_FORMAT_R16_FLOAT"},
            {55, "DXGI_FORMAT_D16_UNORM"},
            {56, "DXGI_FORMAT_R16_UNORM"},
            {57, "DXGI_FORMAT_R16_UINT"},
            {58, "DXGI_FORMAT_R16_SNORM"},
            {59, "DXGI_FORMAT_R16_SINT"},
            {60, "DXGI_FORMAT_R8_TYPELESS"},
            {61, "DXGI_FORMAT_R8_UNORM"},
            {62, "DXGI_FORMAT_R8_UINT"},
            {63, "DXGI_FORMAT_R8_SNORM"},
            {64, "DXGI_FORMAT_R8_SINT"},
            {65, "DXGI_FORMAT_A8_UNORM"},
            {66, "DXGI_FORMAT_R1_UNORM"},
            {67, "DXGI_FORMAT_R9G9B9E5_SHAREDEXP"},
            {68, "DXGI_FORMAT_R8G8_B8G8_UNORM"},
            {69, "DXGI_FORMAT_G8R8_G8B8_UNORM"},
            {70, "DXGI_FORMAT_BC1_TYPELESS"},
            {71, "DXGI_FORMAT_BC1_UNORM"},
            {72, "DXGI_FORMAT_BC1_UNORM_SRGB"},
            {73, "DXGI_FORMAT_BC2_TYPELESS"},
            {74, "DXGI_FORMAT_BC2_UNORM"},
            {75, "DXGI_FORMAT_BC2_UNORM_SRGB"},
            {76, "DXGI_FORMAT_BC3_TYPELESS"},
            {77, "DXGI_FORMAT_BC3_UNORM"},
            {78, "DXGI_FORMAT_BC3_UNORM_SRGB"},
            {79, "DXGI_FORMAT_BC4_TYPELESS"},
            {80, "DXGI_FORMAT_BC4_UNORM"},
            {81, "DXGI_FORMAT_BC4_SNORM"},
            {82, "DXGI_FORMAT_BC5_TYPELESS"},
            {83, "DXGI_FORMAT_BC5_UNORM"},
            {84, "DXGI_FORMAT_BC5_SNORM"},
            {85, "DXGI_FORMAT_B5G6R5_UNORM"},
            {86, "DXGI_FORMAT_B5G5R5A1_UNORM"},
            {87, "DXGI_FORMAT_B8G8R8A8_UNORM"},
            {88, "DXGI_FORMAT_B8G8R8X8_UNORM"},
            {89, "DXGI_FORMAT_R10G10B10_XR_BIAS_A2_UNORM"},
            {90, "DXGI_FORMAT_B8G8R8A8_TYPELESS"},
            {91, "DXGI_FORMAT_B8G8R8A8_UNORM_SRGB"},
            {92, "DXGI_FORMAT_B8G8R8X8_TYPELESS"},
            {93, "DXGI_FORMAT_B8G8R8X8_UNORM_SRGB"},
            {94, "DXGI_FORMAT_BC6H_TYPELESS"},
            {95, "DXGI_FORMAT_BC6H_UF16"},
            {96, "DXGI_FORMAT_BC6H_SF16"},
            {97, "DXGI_FORMAT_BC7_TYPELESS"},
            {98, "DXGI_FORMAT_BC7_UNORM"},
            {99, "DXGI_FORMAT_BC7_UNORM_SRGB"},
            {100, "DXGI_FORMAT_AYUV"},
            {101, "DXGI_FORMAT_Y410"},
            {102, "DXGI_FORMAT_Y416"},
            {103, "DXGI_FORMAT_NV12"},
            {104, "DXGI_FORMAT_P010"},
            {105, "DXGI_FORMAT_P016"},
            {106, "DXGI_FORMAT_420_OPAQUE"},
            {107, "DXGI_FORMAT_YUY2"},
            {108, "DXGI_FORMAT_Y210"},
            {109, "DXGI_FORMAT_Y216"},
            {110, "DXGI_FORMAT_NV11"},
            {111, "DXGI_FORMAT_AI44"},
            {112, "DXGI_FORMAT_IA44"},
            {113, "DXGI_FORMAT_P8"},
            {114, "DXGI_FORMAT_A8P8"},
            {115, "DXGI_FORMAT_B4G4R4A4_UNORM"},
            {130, "DXGI_FORMAT_P208"},
            {131, "DXGI_FORMAT_V208"},
            {132, "DXGI_FORMAT_V408"},
            {0xffffffff, "DXGI_FORMAT_FORCE_UINT"}
        };
    }
}