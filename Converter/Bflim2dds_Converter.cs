namespace super_toolbox
{
    public class Bflim2dds_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;
        private static readonly Dictionary<uint, string> formats = new Dictionary<uint, string>
        {
            { 0x00000000, "GX2_SURFACE_FORMAT_INVALID" },
            { 0x0000001a, "GX2_SURFACE_FORMAT_TCS_R8_G8_B8_A8_UNORM" },
            { 0x0000041a, "GX2_SURFACE_FORMAT_TCS_R8_G8_B8_A8_SRGB" },
            { 0x00000019, "GX2_SURFACE_FORMAT_TCS_R10_G10_B10_A2_UNORM" },
            { 0x00000008, "GX2_SURFACE_FORMAT_TCS_R5_G6_B5_UNORM" },
            { 0x0000000a, "GX2_SURFACE_FORMAT_TC_R5_G5_B5_A1_UNORM" },
            { 0x0000000b, "GX2_SURFACE_FORMAT_TC_R4_G4_B4_A4_UNORM" },
            { 0x00000001, "GX2_SURFACE_FORMAT_TC_R8_UNORM" },
            { 0x00000007, "GX2_SURFACE_FORMAT_TC_R8_G8_UNORM" },
            { 0x00000002, "GX2_SURFACE_FORMAT_TC_R4_G4_UNORM" },
            { 0x00000031, "GX2_SURFACE_FORMAT_T_BC1_UNORM" },
            { 0x00000431, "GX2_SURFACE_FORMAT_T_BC1_SRGB" },
            { 0x00000032, "GX2_SURFACE_FORMAT_T_BC2_UNORM" },
            { 0x00000432, "GX2_SURFACE_FORMAT_T_BC2_SRGB" },
            { 0x00000033, "GX2_SURFACE_FORMAT_T_BC3_UNORM" },
            { 0x00000433, "GX2_SURFACE_FORMAT_T_BC3_SRGB" },
            { 0x00000034, "GX2_SURFACE_FORMAT_T_BC4_UNORM" },
            { 0x00000035, "GX2_SURFACE_FORMAT_T_BC5_UNORM" },
        };

        private static readonly List<uint> BCn_formats = new List<uint> { 0x31, 0x431, 0x32, 0x432, 0x33, 0x433, 0x34, 0x35 };

        public class FLIMData
        {
            public uint width;
            public uint height;
            public uint format;
            public uint format_;
            public uint imageSize;
            public uint swizzle;
            public uint tileMode;
            public uint alignment;
            public uint pitch;
            public byte[] data = Array.Empty<byte>();
            public SurfaceOut surfOut = new SurfaceOut();
            public int[] compSel = Array.Empty<int>();
            public uint realSize;
        }

        public class SurfaceOut
        {
            public uint size;
            public uint pitch;
            public uint height;
            public uint depth;
            public uint surfSize;
            public uint tileMode;
            public uint baseAlign;
            public uint pitchAlign;
            public uint heightAlign;
            public uint depthAlign;
            public uint bpp;
            public uint pixelPitch;
            public uint pixelHeight;
            public uint pixelBits;
            public uint sliceSize;
            public uint pitchTileMax;
            public uint heightTileMax;
            public uint sliceTileMax;
            public TileInfo? pTileInfo;
            public uint tileType;
            public uint tileIndex;
        }

        public class SurfaceIn
        {
            public uint size;
            public uint tileMode;
            public uint format;
            public uint bpp;
            public uint numSamples;
            public uint width;
            public uint height;
            public uint numSlices;
            public uint slice;
            public uint mipLevel;
            public Flags? flags;
            public uint numFrags;
            public TileInfo? pTileInfo;
            public uint tileIndex;
        }

        public class Flags
        {
            public uint value;
        }

        public class TileInfo
        {
            public uint banks;
            public uint bankWidth;
            public uint bankHeight;
            public uint macroAspectRatio;
            public uint tileSplitBytes;
            public uint pipeConfig;
        }

        private static readonly uint[] formatHwInfo = new uint[]
        {
            0x00, 0x00, 0x00, 0x01, 0x08, 0x03, 0x00, 0x01, 0x08, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x01, 0x10, 0x07, 0x00, 0x00, 0x10, 0x03, 0x00, 0x01, 0x10, 0x03, 0x00, 0x01,
            0x10, 0x0B, 0x00, 0x01, 0x10, 0x01, 0x00, 0x01, 0x10, 0x03, 0x00, 0x01, 0x10, 0x03, 0x00, 0x01,
            0x10, 0x03, 0x00, 0x01, 0x20, 0x03, 0x00, 0x00, 0x20, 0x07, 0x00, 0x00, 0x20, 0x03, 0x00, 0x00,
            0x20, 0x03, 0x00, 0x01, 0x20, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x03, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x20, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x01, 0x20, 0x0B, 0x00, 0x01, 0x20, 0x0B, 0x00, 0x01, 0x20, 0x0B, 0x00, 0x01,
            0x40, 0x05, 0x00, 0x00, 0x40, 0x03, 0x00, 0x00, 0x40, 0x03, 0x00, 0x00, 0x40, 0x03, 0x00, 0x00,
            0x40, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x80, 0x03, 0x00, 0x00, 0x80, 0x03, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x10, 0x01, 0x00, 0x00,
            0x10, 0x01, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00,
            0x00, 0x01, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x60, 0x01, 0x00, 0x00,
            0x60, 0x01, 0x00, 0x00, 0x40, 0x01, 0x00, 0x01, 0x80, 0x01, 0x00, 0x01, 0x80, 0x01, 0x00, 0x01,
            0x40, 0x01, 0x00, 0x01, 0x80, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        private static readonly uint[] formatExInfo = new uint[]
        {
            0x00, 0x01, 0x01, 0x03, 0x08, 0x01, 0x01, 0x03, 0x08, 0x01, 0x01, 0x03, 0x08, 0x01, 0x01, 0x03,
            0x00, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03,
            0x10, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03,
            0x10, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03,
            0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03,
            0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03,
            0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03,
            0x40, 0x01, 0x01, 0x03, 0x40, 0x01, 0x01, 0x03, 0x40, 0x01, 0x01, 0x03, 0x40, 0x01, 0x01, 0x03,
            0x40, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03, 0x80, 0x01, 0x01, 0x03, 0x80, 0x01, 0x01, 0x03,
            0x00, 0x01, 0x01, 0x03, 0x01, 0x08, 0x01, 0x05, 0x01, 0x08, 0x01, 0x06, 0x10, 0x01, 0x01, 0x07,
            0x10, 0x01, 0x01, 0x08, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03,
            0x18, 0x03, 0x01, 0x04, 0x30, 0x03, 0x01, 0x04, 0x30, 0x03, 0x01, 0x04, 0x60, 0x03, 0x01, 0x04,
            0x60, 0x03, 0x01, 0x04, 0x40, 0x04, 0x04, 0x09, 0x80, 0x04, 0x04, 0x0A, 0x80, 0x04, 0x04, 0x0B,
            0x40, 0x04, 0x04, 0x0C, 0x40, 0x04, 0x04, 0x0D, 0x40, 0x04, 0x04, 0x0D, 0x40, 0x04, 0x04, 0x0D,
            0x00, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03,
            0x00, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03, 0x40, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03,
        };

        private static readonly int[] bankSwapOrder = new int[] { 0, 1, 3, 2, 6, 7, 5, 4, 0, 0 };
        private uint expPitch = 0;
        private uint expHeight = 0;
        private uint expNumSlices = 0;   
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> convertedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            var bflimFiles = Directory.EnumerateFiles(directoryPath, "*.bflim", SearchOption.AllDirectories).ToList();

            TotalFilesToConvert = bflimFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var filePath in bflimFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                    string ddsFilePath = Path.Combine(fileDirectory, $"{fileName}.dds");

                    try
                    {
                        bool conversionSuccess = await ConvertBflimToDdsAsync(filePath, ddsFilePath, cancellationToken);

                        if (conversionSuccess && File.Exists(ddsFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(ddsFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(ddsFilePath)}");
                            OnFileConverted(ddsFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{Path.GetFileName(filePath)}转换失败");
                            OnConversionFailed($"{Path.GetFileName(filePath)}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{Path.GetFileName(filePath)} 处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                    OnConversionCompleted();
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");
                    OnConversionCompleted();
                }
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

        private async Task<bool> ConvertBflimToDdsAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] fileData = await File.ReadAllBytesAsync(inputPath, cancellationToken);

                if (fileData.Length < 0x28)
                {
                    ConversionError?.Invoke(this, "文件太小,不是有效的BFLIM文件");
                    return false;
                }

                FLIMData flim = ReadFLIM(fileData);

                object formatObj;

                if (flim.format == 0x31 || flim.format == 0x431)
                {
                    formatObj = flim.format_;
                }
                else if (flim.format == 0x32 || flim.format == 0x432)
                {
                    formatObj = "BC2";
                }
                else if (flim.format == 0x33 || flim.format == 0x433)
                {
                    formatObj = "BC3";
                }
                else if (flim.format == 0x34)
                {
                    formatObj = "BC4U";
                }
                else if (flim.format == 0x35)
                {
                    formatObj = "BC5U";
                }
                else if (flim.format == 0x01)
                {
                    formatObj = 61;
                }
                else if (flim.format == 0x02)
                {
                    formatObj = 112;
                }
                else if (flim.format == 0x07)
                {
                    formatObj = 49;
                }
                else if (flim.format == 0x08)
                {
                    formatObj = 85;
                }
                else if (flim.format == 0x0a)
                {
                    formatObj = 86;
                }
                else if (flim.format == 0x0b)
                {
                    formatObj = 115;
                }
                else if (flim.format == 0x1a || flim.format == 0x41a)
                {
                    formatObj = 28;
                }
                else if (flim.format == 0x19)
                {
                    formatObj = 24;
                }
                else
                {
                    throw new Exception($"不支持的格式: 0x{flim.format:X}");
                }

                byte[] result = Deswizzle(
                    (int)flim.width,
                    (int)flim.height,
                    1,
                    flim.format,
                    0,
                    1,
                    flim.surfOut.tileMode,
                    flim.swizzle,
                    flim.pitch,
                    flim.surfOut.bpp,
                    0,
                    0,
                    flim.data);

                uint size;
                if (BCn_formats.Contains(flim.format))
                {
                    size = (uint)((((flim.width + 3) >> 2) * ((flim.height + 3) >> 2) * (SurfaceGetBitsPerPixel(flim.format) >> 3)));
                }
                else
                {
                    size = (uint)(flim.width * flim.height * (SurfaceGetBitsPerPixel(flim.format) >> 3));
                }

                result = result.Take((int)size).ToArray();

                byte[] ddsHeader = GenerateDDSHeader(
                    1,
                    (int)flim.width,
                    (int)flim.height,
                    formatObj,
                    flim.compSel,
                    (int)size,
                    BCn_formats.Contains(flim.format));

                string? directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    await fs.WriteAsync(ddsHeader, 0, ddsHeader.Length, cancellationToken);
                    await fs.WriteAsync(result, 0, result.Length, cancellationToken);
                }

                return true;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { }
                }
                return false;
            }
        }

        private FLIMData ReadFLIM(byte[] data)
        {
            FLIMData flim = new FLIMData();

            int pos = data.Length - 0x28;

            if (data[pos + 4] != 0xFF || data[pos + 5] != 0xFE)
            {
                if (data[pos + 4] != 0xFE || data[pos + 5] != 0xFF)
                {
                    throw new Exception("无效的字节序标记");
                }
            }

            bool isLittleEndian = (data[pos + 4] == 0xFF && data[pos + 5] == 0xFE);

            byte[] magic = new byte[4];
            Array.Copy(data, pos, magic, 0, 4);
            if (!magic.SequenceEqual(new byte[] { (byte)'F', (byte)'L', (byte)'I', (byte)'M' }))
            {
                throw new Exception("无效的文件头");
            }

            ushort endian = (ushort)ReadUInt16(data, pos + 4, isLittleEndian);
            ushort size_ = (ushort)ReadUInt16(data, pos + 6, isLittleEndian);
            uint version = ReadUInt32(data, pos + 8, isLittleEndian);
            uint fileSize = ReadUInt32(data, pos + 12, isLittleEndian);
            uint numBlocks = ReadUInt32(data, pos + 16, isLittleEndian);

            pos += 20;

            byte[] imagMagic = new byte[4];
            Array.Copy(data, pos, imagMagic, 0, 4);
            if (!imagMagic.SequenceEqual(new byte[] { (byte)'i', (byte)'m', (byte)'a', (byte)'g' }))
            {
                throw new Exception("无效的imag头");
            }

            uint infoSize = ReadUInt32(data, pos + 4, isLittleEndian);
            ushort width = (ushort)ReadUInt16(data, pos + 8, isLittleEndian);
            ushort height = (ushort)ReadUInt16(data, pos + 10, isLittleEndian);
            ushort alignment = (ushort)ReadUInt16(data, pos + 12, isLittleEndian);
            byte format_ = data[pos + 14];
            byte swizzle_tileMode_byte = data[pos + 15];
            uint imageSize = ReadUInt32(data, pos + 16, isLittleEndian);

            flim.width = width;
            flim.height = height;

            uint swizzle_tileMode = swizzle_tileMode_byte;

            switch (format_)
            {
                case 0x00:
                    flim.format = 0x01;
                    flim.compSel = new int[] { 0, 0, 0, 5 };
                    break;
                case 0x01:
                    flim.format = 0x01;
                    flim.compSel = new int[] { 5, 5, 5, 0 };
                    break;
                case 0x02:
                    flim.format = 0x02;
                    flim.compSel = new int[] { 0, 0, 0, 1 };
                    break;
                case 0x03:
                    flim.format = 0x07;
                    flim.compSel = new int[] { 0, 0, 0, 1 };
                    break;
                case 0x05:
                case 0x19:
                    flim.format = 0x08;
                    flim.compSel = new int[] { 2, 1, 0, 5 };
                    break;
                case 0x06:
                    flim.format = 0x1a;
                    flim.compSel = new int[] { 0, 1, 2, 5 };
                    break;
                case 0x07:
                    flim.format = 0x0a;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x08:
                    flim.format = 0x0b;
                    flim.compSel = new int[] { 2, 1, 0, 3 };
                    break;
                case 0x09:
                    flim.format = 0x1a;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x0a:
                    flim.format = 0x31;
                    flim.format_ = 0x31;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x0C:
                    flim.format = 0x31;
                    flim.format_ = 0x31;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x0D:
                    flim.format = 0x32;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x0E:
                    flim.format = 0x33;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x0F:
                case 0x10:
                    flim.format = 0x34;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x11:
                    flim.format = 0x35;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x14:
                    flim.format = 0x41a;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x15:
                    flim.format = 0x431;
                    flim.format_ = 0x431;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x16:
                    flim.format = 0x432;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x17:
                    flim.format = 0x433;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                case 0x18:
                    flim.format = 0x19;
                    flim.compSel = new int[] { 0, 1, 2, 3 };
                    break;
                default:
                    throw new Exception($"不支持的纹理格式: 0x{format_:X}");
            }

            flim.imageSize = imageSize;

            uint[] swizzleResult = ComputeSwizzleTileMode(swizzle_tileMode);
            flim.swizzle = swizzleResult[0];
            flim.tileMode = swizzleResult[1];

            if (flim.tileMode < 1 || flim.tileMode > 16)
            {
                throw new Exception("无效的tileMode");
            }

            flim.alignment = alignment;

            SurfaceOut surfOut = GetSurfaceInfo(flim.format, (int)flim.width, (int)flim.height, 1, 1, (int)flim.tileMode, 0, 0);
            uint tilingDepth = surfOut.depth;

            if (surfOut.tileMode == 3)
            {
                tilingDepth /= 4;
            }

            if (tilingDepth != 1)
            {
                throw new Exception("不支持的深度");
            }

            flim.pitch = surfOut.pitch;
            flim.data = data.Take((int)imageSize).ToArray();
            flim.surfOut = surfOut;

            if (BCn_formats.Contains(flim.format))
            {
                flim.realSize = (uint)(((flim.width + 3) >> 2) * ((flim.height + 3) >> 2) * (SurfaceGetBitsPerPixel(flim.format) / 8));
            }
            else
            {
                flim.realSize = (uint)(flim.width * flim.height * (SurfaceGetBitsPerPixel(flim.format) / 8));
            }

            return flim;
        }

        private uint[] ComputeSwizzleTileMode(uint tileModeAndSwizzlePattern)
        {
            uint tileMode = tileModeAndSwizzlePattern & 0x1F;
            uint swizzlePattern = ((tileModeAndSwizzlePattern >> 5) & 7) << 8;

            if (tileMode != 1 && tileMode != 2 && tileMode != 3 && tileMode != 16)
            {
                swizzlePattern |= 0xD0000;
            }

            return new uint[] { swizzlePattern, tileMode };
        }

        private uint ComputeSwizzleTileMode(uint swizzle, uint tileMode)
        {
            return (swizzle << 5) | tileMode;
        }

        private uint ReadUInt16(byte[] data, int offset, bool isLittleEndian)
        {
            if (isLittleEndian)
            {
                return (uint)(data[offset] | (data[offset + 1] << 8));
            }
            else
            {
                return (uint)((data[offset] << 8) | data[offset + 1]);
            }
        }

        private uint ReadUInt32(byte[] data, int offset, bool isLittleEndian)
        {
            if (isLittleEndian)
            {
                return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
            }
            else
            {
                return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
            }
        }

        private uint SurfaceGetBitsPerPixel(uint surfaceFormat)
        {
            return formatHwInfo[(surfaceFormat & 0x3F) * 4];
        }

        private uint ComputeSurfaceThickness(uint tileMode)
        {
            if (tileMode == 3 || tileMode == 7 || tileMode == 11 || tileMode == 13 || tileMode == 15)
            {
                return 4;
            }
            else if (tileMode == 16 || tileMode == 17)
            {
                return 8;
            }
            return 1;
        }

        private uint GX2TileModeToAddrTileMode(uint tileMode)
        {
            if (tileMode == 0)
            {
                throw new Exception("Use tileMode from getDefaultGX2TileMode().");
            }

            if (tileMode == 16)
            {
                return 0;
            }

            return tileMode;
        }

        private uint ComputePixelIndexWithinMicroTile(int x, int y, int z, uint bpp, uint tileMode, bool isDepth)
        {
            uint pixelBit0 = 0;
            uint pixelBit1 = 0;
            uint pixelBit2 = 0;
            uint pixelBit3 = 0;
            uint pixelBit4 = 0;
            uint pixelBit5 = 0;
            uint pixelBit6 = 0;
            uint pixelBit7 = 0;
            uint pixelBit8 = 0;

            uint thickness = ComputeSurfaceThickness(tileMode);

            if (isDepth)
            {
                pixelBit0 = (uint)(x & 1);
                pixelBit1 = (uint)(y & 1);
                pixelBit2 = (uint)((x & 2) >> 1);
                pixelBit3 = (uint)((y & 2) >> 1);
                pixelBit4 = (uint)((x & 4) >> 2);
                pixelBit5 = (uint)((y & 4) >> 2);
            }
            else
            {
                if (bpp == 8)
                {
                    pixelBit0 = (uint)(x & 1);
                    pixelBit1 = (uint)((x & 2) >> 1);
                    pixelBit2 = (uint)((x & 4) >> 2);
                    pixelBit3 = (uint)((y & 2) >> 1);
                    pixelBit4 = (uint)(y & 1);
                    pixelBit5 = (uint)((y & 4) >> 2);
                }
                else if (bpp == 0x10)
                {
                    pixelBit0 = (uint)(x & 1);
                    pixelBit1 = (uint)((x & 2) >> 1);
                    pixelBit2 = (uint)((x & 4) >> 2);
                    pixelBit3 = (uint)(y & 1);
                    pixelBit4 = (uint)((y & 2) >> 1);
                    pixelBit5 = (uint)((y & 4) >> 2);
                }
                else if (bpp == 0x20 || bpp == 0x60)
                {
                    pixelBit0 = (uint)(x & 1);
                    pixelBit1 = (uint)((x & 2) >> 1);
                    pixelBit2 = (uint)(y & 1);
                    pixelBit3 = (uint)((x & 4) >> 2);
                    pixelBit4 = (uint)((y & 2) >> 1);
                    pixelBit5 = (uint)((y & 4) >> 2);
                }
                else if (bpp == 0x40)
                {
                    pixelBit0 = (uint)(x & 1);
                    pixelBit1 = (uint)(y & 1);
                    pixelBit2 = (uint)((x & 2) >> 1);
                    pixelBit3 = (uint)((x & 4) >> 2);
                    pixelBit4 = (uint)((y & 2) >> 1);
                    pixelBit5 = (uint)((y & 4) >> 2);
                }
                else if (bpp == 0x80)
                {
                    pixelBit0 = (uint)(y & 1);
                    pixelBit1 = (uint)(x & 1);
                    pixelBit2 = (uint)((x & 2) >> 1);
                    pixelBit3 = (uint)((x & 4) >> 2);
                    pixelBit4 = (uint)((y & 2) >> 1);
                    pixelBit5 = (uint)((y & 4) >> 2);
                }
                else
                {
                    pixelBit0 = (uint)(x & 1);
                    pixelBit1 = (uint)((x & 2) >> 1);
                    pixelBit2 = (uint)(y & 1);
                    pixelBit3 = (uint)((x & 4) >> 2);
                    pixelBit4 = (uint)((y & 2) >> 1);
                    pixelBit5 = (uint)((y & 4) >> 2);
                }
            }

            if (thickness > 1)
            {
                pixelBit6 = (uint)(z & 1);
                pixelBit7 = (uint)((z & 2) >> 1);
            }

            if (thickness == 8)
            {
                pixelBit8 = (uint)((z & 4) >> 2);
            }

            return (pixelBit8 << 8) | (pixelBit7 << 7) | (pixelBit6 << 6) | 32 * pixelBit5 | 16 * pixelBit4 | 8 * pixelBit3 | 4 * pixelBit2 | pixelBit0 | 2 * pixelBit1;
        }

        private uint ComputePipeFromCoordWoRotation(int x, int y)
        {
            return (uint)(((y >> 3) ^ (x >> 3)) & 1);
        }

        private uint ComputeBankFromCoordWoRotation(int x, int y)
        {
            return (uint)(((y >> 5) ^ (x >> 3)) & 1) | 2 * (uint)(((y >> 4) ^ (x >> 4)) & 1);
        }

        private uint ComputeSurfaceRotationFromTileMode(uint tileMode)
        {
            if (tileMode == 4 || tileMode == 5 || tileMode == 6 || tileMode == 7 || tileMode == 8 || tileMode == 9 || tileMode == 10 || tileMode == 11)
            {
                return 2;
            }
            else if (tileMode == 12 || tileMode == 13 || tileMode == 14 || tileMode == 15)
            {
                return 1;
            }
            return 0;
        }

        private uint IsThickMacroTiled(uint tileMode)
        {
            if (tileMode == 7 || tileMode == 11 || tileMode == 13 || tileMode == 15)
            {
                return 1;
            }
            return 0;
        }

        private uint IsBankSwappedTileMode(uint tileMode)
        {
            if (tileMode == 8 || tileMode == 9 || tileMode == 10 || tileMode == 11 || tileMode == 14 || tileMode == 15)
            {
                return 1;
            }
            return 0;
        }

        private uint ComputeMacroTileAspectRatio(uint tileMode)
        {
            if (tileMode == 5 || tileMode == 9)
            {
                return 2;
            }
            else if (tileMode == 6 || tileMode == 10)
            {
                return 4;
            }
            return 1;
        }

        private uint ComputeSurfaceBankSwappedWidth(uint tileMode, uint bpp, uint numSamples, uint pitch)
        {
            if (IsBankSwappedTileMode(tileMode) == 0)
            {
                return 0;
            }

            uint bytesPerSample = 8 * bpp;
            uint samplesPerTile = 0;
            uint slicesPerTile = 1;

            if (bytesPerSample != 0)
            {
                samplesPerTile = 2048 / bytesPerSample;
                slicesPerTile = Math.Max(1, numSamples / samplesPerTile);
            }

            if (IsThickMacroTiled(tileMode) != 0)
            {
                numSamples = 4;
            }

            uint bytesPerTileSlice = numSamples * bytesPerSample / slicesPerTile;

            uint factor = ComputeMacroTileAspectRatio(tileMode);
            uint swapTiles = Math.Max(1, 128 / bpp);

            uint swapWidth = swapTiles * 32;
            uint heightBytes = numSamples * factor * bpp * 2 / slicesPerTile;
            uint swapMax = 0x4000 / heightBytes;
            uint swapMin = 256 / bytesPerTileSlice;

            uint bankSwapWidth = Math.Min(swapMax, Math.Max(swapMin, swapWidth));
            while (bankSwapWidth >= 2 * pitch)
            {
                bankSwapWidth >>= 1;
            }

            return bankSwapWidth;
        }

        private uint ComputeSurfaceAddrFromCoordLinear(int x, int y, int slice, int sample, uint bpp, uint pitch, uint height, uint numSlices)
        {
            uint sliceOffset = pitch * height * (uint)(slice + sample * numSlices);
            uint addr = ((uint)y * pitch + (uint)x + sliceOffset) * bpp;

            return addr;
        }

        private uint ComputeSurfaceAddrFromCoordMicroTiled(int x, int y, int slice, uint bpp, uint pitch, uint height, uint tileMode, bool isDepth)
        {
            uint microTileThickness = 1;
            if (tileMode == 3)
            {
                microTileThickness = 4;
            }

            uint microTileBytes = (64 * microTileThickness * bpp + 7) / 8;
            uint microTilesPerRow = pitch >> 3;
            uint microTileIndexX = (uint)(x >> 3);
            uint microTileIndexY = (uint)(y >> 3);
            uint microTileIndexZ = (uint)slice / microTileThickness;

            uint microTileOffset = microTileBytes * (microTileIndexX + microTileIndexY * microTilesPerRow);
            uint sliceBytes = (pitch * height * microTileThickness * bpp + 7) / 8;
            uint sliceOffset = microTileIndexZ * sliceBytes;

            uint pixelIndex = ComputePixelIndexWithinMicroTile(x, y, slice, bpp, tileMode, isDepth);
            uint pixelOffset = (bpp * pixelIndex) >> 3;

            return pixelOffset + microTileOffset + sliceOffset;
        }

        private uint ComputeSurfaceAddrFromCoordMacroTiled(int x, int y, int slice, int sample, uint bpp, uint pitch, uint height, uint numSamples, uint tileMode, bool isDepth, uint pipeSwizzle, uint bankSwizzle)
        {
            uint microTileThickness = ComputeSurfaceThickness(tileMode);

            uint microTileBits = numSamples * bpp * (microTileThickness * 64);
            uint microTileBytes = (microTileBits + 7) / 8;

            uint pixelIndex = ComputePixelIndexWithinMicroTile(x, y, slice, bpp, tileMode, isDepth);
            uint bytesPerSample = microTileBytes / numSamples;
            uint sampleOffset;
            uint pixelOffset;

            if (isDepth)
            {
                sampleOffset = bpp * (uint)sample;
                pixelOffset = numSamples * bpp * pixelIndex;
            }
            else
            {
                sampleOffset = (uint)sample * (microTileBits / numSamples);
                pixelOffset = bpp * pixelIndex;
            }

            uint elemOffset = pixelOffset + sampleOffset;

            uint samplesPerSlice = numSamples;
            uint numSampleSplits = 1;
            uint sampleSlice = 0;

            if (numSamples <= 1 || microTileBytes <= 2048)
            {
                samplesPerSlice = numSamples;
                numSampleSplits = 1;
                sampleSlice = 0;
            }
            else
            {
                samplesPerSlice = 2048 / bytesPerSample;
                numSampleSplits = numSamples / samplesPerSlice;
                numSamples = samplesPerSlice;

                uint tileSliceBits = microTileBits / numSampleSplits;
                sampleSlice = elemOffset / tileSliceBits;
                elemOffset %= tileSliceBits;
            }

            elemOffset = (elemOffset + 7) / 8;

            uint pipe = ComputePipeFromCoordWoRotation(x, y);
            uint bank = ComputeBankFromCoordWoRotation(x, y);

            uint swizzle_ = pipeSwizzle + 2 * bankSwizzle;
            uint bankPipe = pipe + 2 * bank;
            uint rotation = ComputeSurfaceRotationFromTileMode(tileMode);
            int sliceIn = slice;

            if (IsThickMacroTiled(tileMode) != 0)
            {
                sliceIn >>= 2;
            }

            bankPipe ^= 2 * sampleSlice * 3 ^ (swizzle_ + (uint)sliceIn * rotation);
            bankPipe %= 8;
            pipe = bankPipe % 2;
            bank = bankPipe / 2;

            uint sliceBytes = (height * pitch * microTileThickness * bpp * numSamples + 7) / 8;
            uint sliceOffset = sliceBytes * ((sampleSlice + numSampleSplits * (uint)slice) / microTileThickness);

            uint macroTilePitch = 32;
            uint macroTileHeight = 16;

            if (tileMode == 5 || tileMode == 9)
            {
                macroTilePitch = 16;
                macroTileHeight = 32;
            }
            else if (tileMode == 6 || tileMode == 10)
            {
                macroTilePitch = 8;
                macroTileHeight = 64;
            }

            uint macroTilesPerRow = pitch / macroTilePitch;
            uint macroTileBytes = (numSamples * microTileThickness * bpp * macroTileHeight * macroTilePitch + 7) / 8;
            uint macroTileIndexX = (uint)x / macroTilePitch;
            uint macroTileIndexY = (uint)y / macroTileHeight;
            uint macroTileOffset = (macroTileIndexX + macroTilesPerRow * macroTileIndexY) * macroTileBytes;

            if (IsBankSwappedTileMode(tileMode) != 0)
            {
                uint bankSwapWidth = ComputeSurfaceBankSwappedWidth(tileMode, bpp, numSamples, pitch);
                uint swapIndex = macroTilePitch * macroTileIndexX / bankSwapWidth;
                bank ^= (uint)bankSwapOrder[swapIndex & 3];
            }

            uint totalOffset = elemOffset + ((macroTileOffset + sliceOffset) >> 3);
            return (bank << 9) | (pipe << 8) | (totalOffset & 255) | ((totalOffset & 0xFFFFFF00) << 3);
        }

        private byte[] SwizzleSurf(int width, int height, int depth, uint format_, uint aa, uint use, uint tileMode, uint swizzle_, uint pitch, uint bpp, int slice, int sample, byte[] data, bool swizzle)
        {
            uint bytesPerPixel = bpp / 8;
            byte[] result = new byte[data.Length];

            uint actualWidth = (uint)width;
            uint actualHeight = (uint)height;

            if (BCn_formats.Contains(format_))
            {
                actualWidth = (uint)((width + 3) / 4);
                actualHeight = (uint)((height + 3) / 4);
            }

            uint pipeSwizzle = (swizzle_ >> 8) & 1;
            uint bankSwizzle = (swizzle_ >> 9) & 3;

            uint convertedTileMode = GX2TileModeToAddrTileMode(tileMode);

            for (int y = 0; y < actualHeight; y++)
            {
                for (int x = 0; x < actualWidth; x++)
                {
                    uint pos;
                    if (convertedTileMode == 0 || convertedTileMode == 1)
                    {
                        pos = ComputeSurfaceAddrFromCoordLinear(x, y, slice, sample, bytesPerPixel, pitch, actualHeight, (uint)depth);
                    }
                    else if (convertedTileMode == 2 || convertedTileMode == 3)
                    {
                        pos = ComputeSurfaceAddrFromCoordMicroTiled(x, y, slice, bpp, pitch, actualHeight, convertedTileMode, (use & 4) != 0);
                    }
                    else
                    {
                        pos = ComputeSurfaceAddrFromCoordMacroTiled(x, y, slice, sample, bpp, pitch, actualHeight, (uint)(1 << (int)aa), convertedTileMode, (use & 4) != 0, pipeSwizzle, bankSwizzle);
                    }

                    uint pos_ = (uint)((y * actualWidth + x) * bytesPerPixel);

                    if (pos_ + bytesPerPixel <= data.Length && pos + bytesPerPixel <= data.Length)
                    {
                        if (!swizzle)
                        {
                            Array.Copy(data, (int)pos, result, (int)pos_, (int)bytesPerPixel);
                        }
                        else
                        {
                            Array.Copy(data, (int)pos_, result, (int)pos, (int)bytesPerPixel);
                        }
                    }
                }
            }

            return result;
        }

        private byte[] Deswizzle(int width, int height, int depth, uint format_, uint aa, uint use, uint tileMode, uint swizzle_, uint pitch, uint bpp, int slice, int sample, byte[] data)
        {
            return SwizzleSurf(width, height, depth, format_, aa, use, tileMode, swizzle_, pitch, bpp, slice, sample, data, false);
        }

        private SurfaceOut GetSurfaceInfo(uint surfaceFormat, int surfaceWidth, int surfaceHeight, int surfaceDepth, int surfaceDim, int surfaceTileMode, int surfaceAA, int level)
        {
            uint hwFormat = surfaceFormat & 0x3F;

            if (surfaceTileMode == 16)
            {
                uint numSamples = (uint)(1 << surfaceAA);
                uint blockSize = 1;

                if (hwFormat >= 0x31 && hwFormat <= 0x35)
                {
                    blockSize = 4;
                }

                uint width = (uint)((~(blockSize - 1)) & (Math.Max(1, surfaceWidth >> level) + (int)blockSize - 1));

                SurfaceOut pSurfOut = new SurfaceOut();
                pSurfOut.bpp = formatHwInfo[hwFormat * 4];
                pSurfOut.size = 96;
                pSurfOut.pitch = width / blockSize;
                pSurfOut.pixelBits = formatHwInfo[hwFormat * 4];
                pSurfOut.baseAlign = 1;
                pSurfOut.pitchAlign = 1;
                pSurfOut.heightAlign = 1;
                pSurfOut.depthAlign = 1;

                if (surfaceDim == 0)
                {
                    pSurfOut.height = 1;
                    pSurfOut.depth = 1;
                }
                else if (surfaceDim == 1 || surfaceDim == 6)
                {
                    pSurfOut.height = (uint)Math.Max(1, surfaceHeight >> level);
                    pSurfOut.depth = 1;
                }
                else if (surfaceDim == 2)
                {
                    pSurfOut.height = (uint)Math.Max(1, surfaceHeight >> level);
                    pSurfOut.depth = (uint)Math.Max(1, surfaceDepth >> level);
                }
                else if (surfaceDim == 3)
                {
                    pSurfOut.height = (uint)Math.Max(1, surfaceHeight >> level);
                    pSurfOut.depth = (uint)Math.Max(6, surfaceDepth);
                }
                else if (surfaceDim == 4)
                {
                    pSurfOut.height = 1;
                    pSurfOut.depth = (uint)surfaceDepth;
                }
                else if (surfaceDim == 5 || surfaceDim == 7)
                {
                    pSurfOut.height = (uint)Math.Max(1, surfaceHeight >> level);
                    pSurfOut.depth = (uint)surfaceDepth;
                }

                pSurfOut.pixelPitch = width;
                pSurfOut.pixelHeight = (uint)((~(blockSize - 1)) & (pSurfOut.height + blockSize - 1));
                pSurfOut.height = pSurfOut.pixelHeight / blockSize;
                pSurfOut.surfSize = pSurfOut.bpp * numSamples * pSurfOut.depth * pSurfOut.height * pSurfOut.pitch >> 3;

                if (surfaceDim == 2)
                {
                    pSurfOut.sliceSize = pSurfOut.surfSize;
                }
                else
                {
                    pSurfOut.sliceSize = pSurfOut.surfSize / pSurfOut.depth;
                }

                pSurfOut.pitchTileMax = (pSurfOut.pitch >> 3) - 1;
                pSurfOut.heightTileMax = (pSurfOut.height >> 3) - 1;
                pSurfOut.sliceTileMax = (pSurfOut.height * pSurfOut.pitch >> 6) - 1;

                if (pSurfOut.tileMode == 0)
                {
                    pSurfOut.tileMode = 16;
                }

                return pSurfOut;
            }
            else
            {
                SurfaceIn aSurfIn = new SurfaceIn();
                aSurfIn.size = 60;
                aSurfIn.tileMode = (uint)(surfaceTileMode & 0xF);
                aSurfIn.format = hwFormat;
                aSurfIn.bpp = formatHwInfo[hwFormat * 4];
                aSurfIn.numSamples = (uint)(1 << surfaceAA);
                aSurfIn.numFrags = aSurfIn.numSamples;
                aSurfIn.width = (uint)Math.Max(1, surfaceWidth >> level);

                if (surfaceDim == 0)
                {
                    aSurfIn.height = 1;
                    aSurfIn.numSlices = 1;
                }
                else if (surfaceDim == 1 || surfaceDim == 6)
                {
                    aSurfIn.height = (uint)Math.Max(1, surfaceHeight >> level);
                    aSurfIn.numSlices = 1;
                }
                else if (surfaceDim == 2)
                {
                    aSurfIn.height = (uint)Math.Max(1, surfaceHeight >> level);
                    aSurfIn.numSlices = (uint)Math.Max(1, surfaceDepth >> level);
                }
                else if (surfaceDim == 3)
                {
                    aSurfIn.height = (uint)Math.Max(1, surfaceHeight >> level);
                    aSurfIn.numSlices = (uint)Math.Max(6, surfaceDepth);
                    aSurfIn.flags = new Flags();
                    aSurfIn.flags.value |= 0x10;
                }
                else if (surfaceDim == 4)
                {
                    aSurfIn.height = 1;
                    aSurfIn.numSlices = (uint)surfaceDepth;
                }
                else if (surfaceDim == 5 || surfaceDim == 7)
                {
                    aSurfIn.height = (uint)Math.Max(1, surfaceHeight >> level);
                    aSurfIn.numSlices = (uint)surfaceDepth;
                }

                aSurfIn.slice = 0;
                aSurfIn.mipLevel = (uint)level;

                if (surfaceDim == 2)
                {
                    if (aSurfIn.flags == null) aSurfIn.flags = new Flags();
                    aSurfIn.flags.value |= 0x20;
                }

                if (level == 0)
                {
                    if (aSurfIn.flags == null) aSurfIn.flags = new Flags();
                    aSurfIn.flags.value = (1 << 12) | (aSurfIn.flags.value & 0xFFFFEFFF);
                }
                else
                {
                    if (aSurfIn.flags != null)
                    {
                        aSurfIn.flags.value = aSurfIn.flags.value & 0xFFFFEFFF;
                    }
                    else
                    {
                        aSurfIn.flags = new Flags();
                    }
                }

                SurfaceOut pSurfOut = new SurfaceOut();
                pSurfOut.size = 96;

                ComputeSurfaceInfo(aSurfIn, pSurfOut);

                if (pSurfOut.tileMode == 0)
                {
                    pSurfOut.tileMode = 16;
                }

                return pSurfOut;
            }
        }

        private void ComputeSurfaceInfo(SurfaceIn aSurfIn, SurfaceOut pSurfOut)
        {
            SurfaceIn pIn = aSurfIn;
            SurfaceOut pOut = pSurfOut;

            int returnCode = 0;
            uint elemMode = 0;

            if (pIn.bpp > 0x80)
            {
                returnCode = 3;
            }

            if (returnCode == 0)
            {
                ComputeMipLevel(pIn);

                uint width = pIn.width;
                uint height = pIn.height;
                uint bpp = pIn.bpp;
                uint expandX = 1;
                uint expandY = 1;

                pOut.pixelBits = pIn.bpp;

                if (pIn.format != 0)
                {
                    uint[] bits = GetBitsPerPixel(pIn.format);
                    bpp = bits[0];
                    expandX = bits[1];
                    expandY = bits[2];
                    elemMode = bits[3];

                    if (elemMode == 4 && expandX == 3 && pIn.tileMode == 1)
                    {
                        if (pIn.flags == null) pIn.flags = new Flags();
                        pIn.flags.value |= 0x200;
                    }

                    bpp = AdjustSurfaceInfo(pIn, elemMode, expandX, expandY, bpp, width, height);
                }
                else if (pIn.bpp != 0)
                {
                    pIn.width = Math.Max(1, pIn.width);
                    pIn.height = Math.Max(1, pIn.height);
                }
                else
                {
                    returnCode = 3;
                }

                if (returnCode == 0)
                {
                    returnCode = (int)ComputeSurfaceInfoEx(pIn, pOut);
                }

                if (returnCode == 0)
                {
                    pOut.bpp = pIn.bpp;
                    pOut.pixelPitch = pOut.pitch;
                    pOut.pixelHeight = pOut.height;

                    if (pIn.format != 0 && ((pIn.flags == null || ((pIn.flags.value >> 9) & 1) == 0) || pIn.mipLevel == 0))
                    {
                        bpp = RestoreSurfaceInfo(elemMode, expandX, expandY, bpp, pOut);
                    }

                    if (pIn.flags != null && ((pIn.flags.value >> 5) & 1) != 0)
                    {
                        pOut.sliceSize = pOut.surfSize;
                    }
                    else
                    {
                        pOut.sliceSize = pOut.surfSize / pOut.depth;

                        if (pIn.slice == (pIn.numSlices - 1) && pIn.numSlices > 1)
                        {
                            pOut.sliceSize += pOut.sliceSize * (pOut.depth - pIn.numSlices);
                        }
                    }

                    pOut.pitchTileMax = (pOut.pitch >> 3) - 1;
                    pOut.heightTileMax = (pOut.height >> 3) - 1;
                    pOut.sliceTileMax = (pOut.height * pOut.pitch >> 6) - 1;
                }
            }
        }

        private uint[] GetBitsPerPixel(uint format_)
        {
            uint fmtIdx = format_ * 4;
            return new uint[]
            {
                formatExInfo[fmtIdx],
                formatExInfo[fmtIdx + 1],
                formatExInfo[fmtIdx + 2],
                formatExInfo[fmtIdx + 3]
            };
        }

        private uint AdjustSurfaceInfo(SurfaceIn pIn, uint elemMode, uint expandX, uint expandY, uint bpp, uint width, uint height)
        {
            uint bBCnFormat = 0;
            if (bpp != 0 && (elemMode == 9 || elemMode == 10 || elemMode == 11 || elemMode == 12 || elemMode == 13))
            {
                bBCnFormat = 1;
            }

            if (width != 0 && height != 0)
            {
                if (expandX > 1 || expandY > 1)
                {
                    uint widtha;
                    uint heighta;

                    if (elemMode == 4)
                    {
                        widtha = expandX * width;
                        heighta = expandY * height;
                    }
                    else if (bBCnFormat != 0)
                    {
                        widtha = width / expandX;
                        heighta = height / expandY;
                    }
                    else
                    {
                        widtha = (width + expandX - 1) / expandX;
                        heighta = (height + expandY - 1) / expandY;
                    }

                    pIn.width = Math.Max(1, widtha);
                    pIn.height = Math.Max(1, heighta);
                }
            }

            if (bpp != 0)
            {
                if (elemMode == 4)
                {
                    pIn.bpp = bpp / expandX / expandY;
                }
                else if (elemMode == 5 || elemMode == 6)
                {
                    pIn.bpp = expandY * expandX * bpp;
                }
                else if (elemMode == 7 || elemMode == 8)
                {
                    pIn.bpp = bpp;
                }
                else if (elemMode == 9 || elemMode == 12)
                {
                    pIn.bpp = 64;
                }
                else if (elemMode == 10 || elemMode == 11 || elemMode == 13)
                {
                    pIn.bpp = 128;
                }
                else if (elemMode >= 0 && elemMode <= 3)
                {
                    pIn.bpp = bpp;
                }
                else
                {
                    pIn.bpp = bpp;
                }

                return pIn.bpp;
            }

            return 0;
        }

        private uint RestoreSurfaceInfo(uint elemMode, uint expandX, uint expandY, uint bpp, SurfaceOut pOut)
        {
            if (pOut.pixelPitch != 0 && pOut.pixelHeight != 0)
            {
                uint width = pOut.pixelPitch;
                uint height = pOut.pixelHeight;

                if (expandX > 1 || expandY > 1)
                {
                    if (elemMode == 4)
                    {
                        width /= expandX;
                        height /= expandY;
                    }
                    else
                    {
                        width *= expandX;
                        height *= expandY;
                    }
                }

                pOut.pixelPitch = Math.Max(1, width);
                pOut.pixelHeight = Math.Max(1, height);
            }

            if (bpp != 0)
            {
                if (elemMode == 4)
                {
                    return expandY * expandX * bpp;
                }
                else if (elemMode == 5 || elemMode == 6)
                {
                    return bpp / expandX / expandY;
                }
                else if (elemMode == 9 || elemMode == 12)
                {
                    return 64;
                }
                else if (elemMode == 10 || elemMode == 11 || elemMode == 13)
                {
                    return 128;
                }
                return bpp;
            }

            return 0;
        }

        private uint ComputeSurfaceInfoEx(SurfaceIn pIn, SurfaceOut pOut)
        {
            uint tileMode = pIn.tileMode;
            uint bpp = pIn.bpp;
            uint numSamples = Math.Max(1, pIn.numSamples);
            uint pitch = pIn.width;
            uint height = pIn.height;
            uint numSlices = pIn.numSlices;
            uint mipLevel = pIn.mipLevel;
            uint padDims = 0;
            uint baseTileMode = tileMode;

            if (pIn.flags != null && ((pIn.flags.value >> 4) & 1) != 0 && mipLevel == 0)
            {
                padDims = 2;
            }

            if (pIn.flags != null && ((pIn.flags.value >> 6) & 1) != 0)
            {
                tileMode = ConvertToNonBankSwappedMode(tileMode);
            }
            else
            {
                tileMode = ComputeSurfaceMipLevelTileMode(
                    tileMode,
                    bpp,
                    mipLevel,
                    (int)pitch,
                    (int)height,
                    (int)numSlices,
                    numSamples,
                    (pIn.flags != null && ((pIn.flags.value >> 1) & 1) != 0),
                    0);
            }

            uint valid;

            if (tileMode == 0 || tileMode == 1)
            {
                valid = ComputeSurfaceInfoLinear(tileMode, bpp, numSamples, pitch, height, numSlices, mipLevel, padDims, pIn.flags, pOut);
                pOut.tileMode = tileMode;
            }
            else if (tileMode == 2 || tileMode == 3)
            {
                valid = ComputeSurfaceInfoMicroTiled(tileMode, bpp, numSamples, pitch, height, numSlices, mipLevel, padDims, pIn.flags, pOut);
            }
            else if (tileMode >= 4 && tileMode <= 15)
            {
                valid = ComputeSurfaceInfoMacroTiled(tileMode, baseTileMode, bpp, numSamples, pitch, height, numSlices, mipLevel, padDims, pIn.flags, pOut);
            }
            else
            {
                valid = 0;
            }

            if (valid == 0)
            {
                return 3;
            }

            return 0;
        }

        private uint ConvertToNonBankSwappedMode(uint tileMode)
        {
            if (tileMode == 8)
            {
                return 4;
            }
            else if (tileMode == 9)
            {
                return 5;
            }
            else if (tileMode == 10)
            {
                return 6;
            }
            else if (tileMode == 11)
            {
                return 7;
            }
            else if (tileMode == 14)
            {
                return 12;
            }
            else if (tileMode == 15)
            {
                return 13;
            }
            return tileMode;
        }

        private uint ComputeSurfaceMipLevelTileMode(uint baseTileMode, uint bpp, uint level, int width, int height, int numSlices, uint numSamples, bool isDepth, uint noRecursive)
        {
            uint widthAlignFactor = 1;
            uint macroTileWidth = 32;
            uint macroTileHeight = 16;
            uint tileSlices = ComputeSurfaceTileSlices(baseTileMode, bpp, numSamples);
            uint expTileMode = baseTileMode;

            if (numSamples > 1 || tileSlices > 1 || isDepth)
            {
                if (baseTileMode == 7)
                {
                    expTileMode = 4;
                }
                else if (baseTileMode == 13)
                {
                    expTileMode = 12;
                }
                else if (baseTileMode == 11)
                {
                    expTileMode = 8;
                }
                else if (baseTileMode == 15)
                {
                    expTileMode = 14;
                }
            }

            if (baseTileMode == 2 && numSamples > 1)
            {
                expTileMode = 4;
            }
            else if (baseTileMode == 3)
            {
                if (numSamples > 1 || isDepth)
                {
                    expTileMode = 2;
                }
                if (numSamples == 2 || numSamples == 4)
                {
                    expTileMode = 7;
                }
            }

            if (noRecursive != 0 || level == 0)
            {
                return expTileMode;
            }

            uint tempBpp = bpp;
            if (tempBpp == 24 || tempBpp == 48 || tempBpp == 96)
            {
                tempBpp /= 3;
            }

            uint widtha = NextPow2((uint)width);
            uint heighta = NextPow2((uint)height);
            uint numSlicesa = NextPow2((uint)numSlices);

            expTileMode = ConvertToNonBankSwappedMode(expTileMode);
            uint thickness = ComputeSurfaceThickness(expTileMode);
            uint microTileBytes = (numSamples * tempBpp * (thickness << 6) + 7) >> 3;

            if (microTileBytes < 256)
            {
                widthAlignFactor = Math.Max(1, 256 / microTileBytes);
            }

            if (expTileMode == 4 || expTileMode == 12)
            {
                if (widtha < widthAlignFactor * macroTileWidth || heighta < macroTileHeight)
                {
                    expTileMode = 2;
                }
            }
            else if (expTileMode == 5)
            {
                macroTileWidth = 16;
                macroTileHeight = 32;

                if (widtha < widthAlignFactor * macroTileWidth || heighta < macroTileHeight)
                {
                    expTileMode = 2;
                }
            }
            else if (expTileMode == 6)
            {
                macroTileWidth = 8;
                macroTileHeight = 64;

                if (widtha < widthAlignFactor * macroTileWidth || heighta < macroTileHeight)
                {
                    expTileMode = 2;
                }
            }

            if (expTileMode == 7 || expTileMode == 13)
            {
                if (widtha < widthAlignFactor * macroTileWidth || heighta < macroTileHeight)
                {
                    expTileMode = 3;
                }
            }

            if (numSlicesa < 4)
            {
                if (expTileMode == 3)
                {
                    expTileMode = 2;
                }
                else if (expTileMode == 7)
                {
                    expTileMode = 4;
                }
                else if (expTileMode == 13)
                {
                    expTileMode = 12;
                }
            }

            return ComputeSurfaceMipLevelTileMode(
                expTileMode,
                tempBpp,
                level,
                (int)widtha,
                (int)heighta,
                (int)numSlicesa,
                numSamples,
                isDepth,
                1);
        }

        private uint ComputeSurfaceTileSlices(uint tileMode, uint bpp, uint numSamples)
        {
            uint bytePerSample = ((bpp << 6) + 7) >> 3;
            uint tileSlices = 1;

            if (ComputeSurfaceThickness(tileMode) > 1)
            {
                numSamples = 4;
            }

            if (bytePerSample != 0)
            {
                uint samplePerTile = 2048 / bytePerSample;
                if (samplePerTile != 0)
                {
                    tileSlices = Math.Max(1, numSamples / samplePerTile);
                }
            }

            return tileSlices;
        }

        private uint NextPow2(uint dim)
        {
            uint newDim = 1;
            if (dim <= 0x7FFFFFFF)
            {
                while (newDim < dim)
                {
                    newDim *= 2;
                }
            }
            else
            {
                newDim = 0x80000000;
            }
            return newDim;
        }

        private uint PowTwoAlign(uint x, uint align)
        {
            return ~(align - 1) & (x + align - 1);
        }

        private void ComputeMipLevel(SurfaceIn pIn)
        {
            if (pIn.format >= 49 && pIn.format <= 55)
            {
                pIn.width = PowTwoAlign(pIn.width, 4);
                pIn.height = PowTwoAlign(pIn.height, 4);
            }

            bool hwlHandled = HwlComputeMipLevel(pIn) != 0;

            if (!hwlHandled && pIn.mipLevel != 0 && (pIn.flags != null && ((pIn.flags.value >> 12) & 1) != 0))
            {
                uint width = Math.Max(1, pIn.width >> (int)pIn.mipLevel);
                uint height = Math.Max(1, pIn.height >> (int)pIn.mipLevel);
                uint slices = Math.Max(1, pIn.numSlices);

                if (pIn.flags != null && ((pIn.flags.value >> 4) & 1) == 0)
                {
                    slices = Math.Max(1, slices >> (int)pIn.mipLevel);
                }

                if (pIn.format != 47 && pIn.format != 48)
                {
                    width = NextPow2(width);
                    height = NextPow2(height);
                    slices = NextPow2(slices);
                }

                pIn.width = width;
                pIn.height = height;
                pIn.numSlices = slices;
            }
        }

        private uint HwlComputeMipLevel(SurfaceIn pIn)
        {
            uint handled = 0;

            if (pIn.format >= 49 && pIn.format <= 55)
            {
                if (pIn.mipLevel != 0)
                {
                    uint width = pIn.width;
                    uint height = pIn.height;
                    uint slices = pIn.numSlices;

                    if (pIn.flags != null && ((pIn.flags.value >> 12) & 1) != 0)
                    {
                        uint widtha = width >> (int)pIn.mipLevel;
                        uint heighta = height >> (int)pIn.mipLevel;

                        if (pIn.flags == null || ((pIn.flags.value >> 4) & 1) == 0)
                        {
                            slices >>= (int)pIn.mipLevel;
                        }

                        width = Math.Max(1, widtha);
                        height = Math.Max(1, heighta);
                        slices = Math.Max(1, slices);
                    }

                    pIn.width = NextPow2(width);
                    pIn.height = NextPow2(height);
                    pIn.numSlices = slices;
                }

                handled = 1;
            }

            return handled;
        }

        private (uint, uint, uint) PadDimensions(uint tileMode, uint padDims, bool isCube, uint pitchAlign, uint heightAlign, uint sliceAlign)
        {
            uint thickness = ComputeSurfaceThickness(tileMode);

            if (padDims == 0)
            {
                padDims = 3;
            }

            if ((pitchAlign & (pitchAlign - 1)) != 0)
            {
                expPitch += pitchAlign - 1;
                expPitch /= pitchAlign;
                expPitch *= pitchAlign;
            }
            else
            {
                expPitch = PowTwoAlign(expPitch, pitchAlign);
            }

            if (padDims > 1)
            {
                expHeight = PowTwoAlign(expHeight, heightAlign);
            }

            if (padDims > 2 || thickness > 1)
            {
                if (isCube)
                {
                    expNumSlices = NextPow2(expNumSlices);
                }

                if (thickness > 1)
                {
                    expNumSlices = PowTwoAlign(expNumSlices, sliceAlign);
                }
            }

            return (expPitch, expHeight, expNumSlices);
        }

        private uint AdjustPitchAlignment(Flags? flags, uint pitchAlign)
        {
            if (flags != null && ((flags.value >> 13) & 1) != 0)
            {
                pitchAlign = PowTwoAlign(pitchAlign, 0x20);
            }
            return pitchAlign;
        }

        private (uint, uint, uint) ComputeSurfaceAlignmentsLinear(uint tileMode, uint bpp, Flags? flags)
        {
            uint baseAlign;
            uint pitchAlign;
            uint heightAlign;

            if (tileMode == 0)
            {
                baseAlign = 1;
                pitchAlign = (bpp != 1) ? 1u : 8u;
                heightAlign = 1;
            }
            else if (tileMode == 1)
            {
                uint pixelsPerPipeInterleave = 2048 / bpp;
                baseAlign = 256;
                pitchAlign = Math.Max(0x40, pixelsPerPipeInterleave);
                heightAlign = 1;
            }
            else
            {
                baseAlign = 1;
                pitchAlign = 1;
                heightAlign = 1;
            }

            pitchAlign = AdjustPitchAlignment(flags, pitchAlign);

            return (baseAlign, pitchAlign, heightAlign);
        }

        private uint ComputeSurfaceInfoLinear(uint tileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, Flags? flags, SurfaceOut pOut)
        {
            expPitch = pitch;
            expHeight = height;
            expNumSlices = numSlices;

            uint microTileThickness = ComputeSurfaceThickness(tileMode);

            (uint baseAlign, uint pitchAlign, uint heightAlign) = ComputeSurfaceAlignmentsLinear(tileMode, bpp, flags);

            if (flags != null && ((flags.value >> 9) & 1) != 0 && mipLevel == 0)
            {
                expPitch /= 3;
                expPitch = NextPow2(expPitch);
            }

            if (mipLevel != 0)
            {
                expPitch = NextPow2(expPitch);
                expHeight = NextPow2(expHeight);

                if (flags != null && ((flags.value >> 4) & 1) != 0)
                {
                    expNumSlices = numSlices;

                    if (numSlices <= 1)
                    {
                        padDims = 2;
                    }
                    else
                    {
                        padDims = 0;
                    }
                }
                else
                {
                    expNumSlices = NextPow2(numSlices);
                }
            }

            (expPitch, expHeight, expNumSlices) = PadDimensions(
                tileMode,
                padDims,
                (flags != null && ((flags.value >> 4) & 1) != 0),
                pitchAlign,
                heightAlign,
                microTileThickness);

            if (flags != null && ((flags.value >> 9) & 1) != 0 && mipLevel == 0)
            {
                expPitch *= 3;
            }

            uint slices = expNumSlices * numSamples / microTileThickness;
            pOut.pitch = expPitch;
            pOut.height = expHeight;
            pOut.depth = expNumSlices;
            pOut.surfSize = (expHeight * expPitch * slices * bpp * numSamples + 7) / 8;
            pOut.baseAlign = baseAlign;
            pOut.pitchAlign = pitchAlign;
            pOut.heightAlign = heightAlign;
            pOut.depthAlign = microTileThickness;

            return 1;
        }

        private (uint, uint, uint) ComputeSurfaceAlignmentsMicroTiled(uint tileMode, uint bpp, Flags? flags, uint numSamples)
        {
            if (bpp == 24 || bpp == 48 || bpp == 96)
            {
                bpp /= 3;
            }

            uint thickness = ComputeSurfaceThickness(tileMode);
            uint baseAlign = 256;
            uint pitchAlign = Math.Max(8, 256 / bpp / numSamples / thickness);
            uint heightAlign = 8;

            pitchAlign = AdjustPitchAlignment(flags, pitchAlign);

            return (baseAlign, pitchAlign, heightAlign);
        }

        private uint ComputeSurfaceInfoMicroTiled(uint tileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, Flags? flags, SurfaceOut pOut)
        {
            uint expTileMode = tileMode;
            expPitch = pitch;
            expHeight = height;
            expNumSlices = numSlices;

            uint microTileThickness = ComputeSurfaceThickness(tileMode);

            if (mipLevel != 0)
            {
                expPitch = NextPow2(pitch);
                expHeight = NextPow2(height);

                if (flags != null && ((flags.value >> 4) & 1) != 0)
                {
                    expNumSlices = numSlices;

                    if (numSlices <= 1)
                    {
                        padDims = 2;
                    }
                    else
                    {
                        padDims = 0;
                    }
                }
                else
                {
                    expNumSlices = NextPow2(numSlices);
                }

                if (expTileMode == 3 && expNumSlices < 4)
                {
                    expTileMode = 2;
                    microTileThickness = 1;
                }
            }

            (uint baseAlign, uint pitchAlign, uint heightAlign) = ComputeSurfaceAlignmentsMicroTiled(
                expTileMode,
                bpp,
                flags,
                numSamples);

            (expPitch, expHeight, expNumSlices) = PadDimensions(
                expTileMode,
                padDims,
                (flags != null && ((flags.value >> 4) & 1) != 0),
                pitchAlign,
                heightAlign,
                microTileThickness);

            pOut.pitch = expPitch;
            pOut.height = expHeight;
            pOut.depth = expNumSlices;
            pOut.surfSize = (expHeight * expPitch * expNumSlices * bpp * numSamples + 7) / 8;
            pOut.tileMode = expTileMode;
            pOut.baseAlign = baseAlign;
            pOut.pitchAlign = pitchAlign;
            pOut.heightAlign = heightAlign;
            pOut.depthAlign = microTileThickness;

            return 1;
        }

        private (uint, uint, uint, uint, uint) ComputeSurfaceAlignmentsMacroTiled(uint tileMode, uint bpp, Flags? flags, uint numSamples)
        {
            uint aspectRatio = ComputeMacroTileAspectRatio(tileMode);
            uint thickness = ComputeSurfaceThickness(tileMode);

            if (bpp == 24 || bpp == 48 || bpp == 96)
            {
                bpp /= 3;
            }

            if (bpp == 3)
            {
                bpp = 1;
            }

            uint macroTileWidth = 32 / aspectRatio;
            uint macroTileHeight = aspectRatio * 16;

            uint pitchAlign = Math.Max(macroTileWidth, macroTileWidth * (256 / bpp / (8 * thickness) / numSamples));
            pitchAlign = AdjustPitchAlignment(flags, pitchAlign);

            uint heightAlign = macroTileHeight;
            uint macroTileBytes = numSamples * ((bpp * macroTileHeight * macroTileWidth + 7) >> 3);

            uint baseAlign;
            if (thickness == 1)
            {
                baseAlign = Math.Max(macroTileBytes, (numSamples * heightAlign * bpp * pitchAlign + 7) >> 3);
            }
            else
            {
                baseAlign = Math.Max(256, (4 * heightAlign * bpp * pitchAlign + 7) >> 3);
            }

            uint microTileBytes = (thickness * numSamples * (bpp << 6) + 7) >> 3;
            uint numSlicesPerMicroTile = (microTileBytes < 2048) ? 1u : microTileBytes / 2048;
            baseAlign /= numSlicesPerMicroTile;

            return (baseAlign, pitchAlign, heightAlign, macroTileWidth, macroTileHeight);
        }

        private uint ComputeSurfaceInfoMacroTiled(uint tileMode, uint baseTileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, Flags? flags, SurfaceOut pOut)
        {
            expPitch = pitch;
            expHeight = height;
            expNumSlices = numSlices;

            uint expTileMode = tileMode;
            uint microTileThickness = ComputeSurfaceThickness(tileMode);
            uint result = 0;

            if (mipLevel != 0)
            {
                expPitch = NextPow2(pitch);
                expHeight = NextPow2(height);

                if (flags != null && ((flags.value >> 4) & 1) != 0)
                {
                    expNumSlices = numSlices;
                    padDims = (numSlices <= 1) ? 2u : 0u;
                }
                else
                {
                    expNumSlices = NextPow2(numSlices);
                }

                if (expTileMode == 7 && expNumSlices < 4)
                {
                    expTileMode = 4;
                    microTileThickness = 1;
                }
            }

            if (tileMode == baseTileMode || mipLevel == 0 || IsThickMacroTiled(baseTileMode) == 0 || IsThickMacroTiled(tileMode) != 0)
            {
                (uint baseAlign, uint pitchAlign, uint heightAlign, uint macroWidth, uint macroHeight) = ComputeSurfaceAlignmentsMacroTiled(
                    tileMode,
                    bpp,
                    flags,
                    numSamples);

                uint bankSwappedWidth = ComputeSurfaceBankSwappedWidth(tileMode, bpp, numSamples, pitch);

                if (bankSwappedWidth > pitchAlign)
                {
                    pitchAlign = bankSwappedWidth;
                }

                (expPitch, expHeight, expNumSlices) = PadDimensions(
                    tileMode,
                    padDims,
                    (flags != null && ((flags.value >> 4) & 1) != 0),
                    pitchAlign,
                    heightAlign,
                    microTileThickness);

                pOut.pitch = expPitch;
                pOut.height = expHeight;
                pOut.depth = expNumSlices;
                pOut.surfSize = (expHeight * expPitch * expNumSlices * bpp * numSamples + 7) / 8;
                pOut.tileMode = expTileMode;
                pOut.baseAlign = baseAlign;
                pOut.pitchAlign = pitchAlign;
                pOut.heightAlign = heightAlign;
                pOut.depthAlign = microTileThickness;
                result = 1;
            }
            else
            {
                (uint baseAlign, uint pitchAlign, uint heightAlign, uint macroWidth, uint macroHeight) = ComputeSurfaceAlignmentsMacroTiled(
                    baseTileMode,
                    bpp,
                    flags,
                    numSamples);

                uint pitchAlignFactor = Math.Max(1, 32 / bpp);

                if (expPitch < pitchAlign * pitchAlignFactor || expHeight < heightAlign)
                {
                    expTileMode = 2;

                    result = ComputeSurfaceInfoMicroTiled(
                        2,
                        bpp,
                        numSamples,
                        pitch,
                        height,
                        numSlices,
                        mipLevel,
                        padDims,
                        flags,
                        pOut);
                }
                else
                {
                    (baseAlign, pitchAlign, heightAlign, macroWidth, macroHeight) = ComputeSurfaceAlignmentsMacroTiled(
                        tileMode,
                        bpp,
                        flags,
                        numSamples);

                    uint bankSwappedWidth = ComputeSurfaceBankSwappedWidth(tileMode, bpp, numSamples, pitch);
                    if (bankSwappedWidth > pitchAlign)
                    {
                        pitchAlign = bankSwappedWidth;
                    }

                    (expPitch, expHeight, expNumSlices) = PadDimensions(
                        tileMode,
                        padDims,
                        (flags != null && ((flags.value >> 4) & 1) != 0),
                        pitchAlign,
                        heightAlign,
                        microTileThickness);

                    pOut.pitch = expPitch;
                    pOut.height = expHeight;
                    pOut.depth = expNumSlices;
                    pOut.surfSize = (expHeight * expPitch * expNumSlices * bpp * numSamples + 7) / 8;
                    pOut.tileMode = expTileMode;
                    pOut.baseAlign = baseAlign;
                    pOut.pitchAlign = pitchAlign;
                    pOut.heightAlign = heightAlign;
                    pOut.depthAlign = microTileThickness;
                    result = 1;
                }
            }

            return result;
        }

        private byte[] GenerateDDSHeader(int numMipmaps, int w, int h, object format_, int[] compSel, int size, bool compressed)
        {
            byte[] hdr = new byte[128];
            Array.Fill<byte>(hdr, 0);

            bool luminance = false;
            bool RGB = false;
            Dictionary<int, uint> compSels = new Dictionary<int, uint>();
            int fmtbpp = 0;
            byte[] fourcc = new byte[4];
            bool has_alpha = true;

            if (format_ is int intFormat)
            {
                if (intFormat == 28)
                {
                    RGB = true;
                    compSels = new Dictionary<int, uint> { { 0, 0x000000ff }, { 1, 0x0000ff00 }, { 2, 0x00ff0000 }, { 3, 0xff000000 }, { 5, 0 } };
                    fmtbpp = 4;
                }
                else if (intFormat == 24)
                {
                    RGB = true;
                    compSels = new Dictionary<int, uint> { { 0, 0x3ff00000 }, { 1, 0x000ffc00 }, { 2, 0x000003ff }, { 3, 0xc0000000 }, { 5, 0 } };
                    fmtbpp = 4;
                }
                else if (intFormat == 85)
                {
                    RGB = true;
                    compSels = new Dictionary<int, uint> { { 0, 0x0000001f }, { 1, 0x000007e0 }, { 2, 0x0000f800 }, { 3, 0 }, { 5, 0 } };
                    fmtbpp = 2;
                    has_alpha = false;
                }
                else if (intFormat == 86)
                {
                    RGB = true;
                    compSels = new Dictionary<int, uint> { { 0, 0x0000001f }, { 1, 0x000003e0 }, { 2, 0x00007c00 }, { 3, 0x00008000 }, { 5, 0 } };
                    fmtbpp = 2;
                }
                else if (intFormat == 115)
                {
                    RGB = true;
                    compSels = new Dictionary<int, uint> { { 0, 0x0000000f }, { 1, 0x000000f0 }, { 2, 0x00000f00 }, { 3, 0x0000f000 }, { 5, 0 } };
                    fmtbpp = 2;
                }
                else if (intFormat == 61)
                {
                    luminance = true;
                    compSels = new Dictionary<int, uint> { { 0, 0x000000ff }, { 1, 0 }, { 2, 0 }, { 3, 0 }, { 5, 0 } };
                    fmtbpp = 1;

                    if (compSel[3] != 0)
                    {
                        has_alpha = false;
                    }
                }
                else if (intFormat == 49)
                {
                    luminance = true;
                    compSels = new Dictionary<int, uint> { { 0, 0x000000ff }, { 1, 0x0000ff00 }, { 2, 0 }, { 3, 0 }, { 5, 0 } };
                    fmtbpp = 2;
                }
                else if (intFormat == 112)
                {
                    luminance = true;
                    compSels = new Dictionary<int, uint> { { 0, 0x0000000f }, { 1, 0x000000f0 }, { 2, 0 }, { 3, 0 }, { 5, 0 } };
                    fmtbpp = 1;
                }
            }
            else if (format_ is string strFormat)
            {
                if (strFormat == "BC1" || strFormat == "BC2" || strFormat == "BC3" || strFormat == "BC4U" || strFormat == "BC4S" || strFormat == "BC5U" || strFormat == "BC5S")
                {
                    compressed = true;
                }
            }

            uint flags = 0x00000001 | 0x00001000 | 0x00000004 | 0x00000002;
            uint caps = 0x00001000;

            if (numMipmaps == 0)
            {
                numMipmaps = 1;
            }
            else if (numMipmaps != 1)
            {
                flags |= 0x00020000;
                caps |= 0x00000008 | 0x00400000;
            }

            uint pflags = 0;
            uint finalSize = (uint)size;

            if (!compressed)
            {
                flags |= 0x00000008;

                bool a = false;

                if (compSel[0] != 0 && compSel[1] != 0 && compSel[2] != 0 && compSel[3] == 0)
                {
                    a = true;
                    pflags = 0x00000002;
                }
                else if (luminance)
                {
                    pflags = 0x00020000;
                }
                else if (RGB)
                {
                    pflags = 0x00000040;
                }
                else
                {
                    return new byte[0];
                }

                if (has_alpha && !a)
                {
                    pflags |= 0x00000001;
                }

                finalSize = (uint)(w * fmtbpp);
            }
            else
            {
                flags |= 0x00080000;
                pflags = 0x00000004;

                if (format_ is string strFmt)
                {
                    if (strFmt == "BC1")
                    {
                        fourcc = new byte[] { (byte)'D', (byte)'X', (byte)'T', (byte)'1' };
                    }
                    else if (strFmt == "BC2")
                    {
                        fourcc = new byte[] { (byte)'D', (byte)'X', (byte)'T', (byte)'3' };
                    }
                    else if (strFmt == "BC3")
                    {
                        fourcc = new byte[] { (byte)'D', (byte)'X', (byte)'T', (byte)'5' };
                    }
                    else if (strFmt == "BC4U" || strFmt == "BC4S" || strFmt == "BC5U" || strFmt == "BC5S")
                    {
                        fourcc = new byte[] { (byte)'D', (byte)'X', (byte)'1', (byte)'0' };
                    }
                }
            }

            Buffer.BlockCopy(new byte[] { (byte)'D', (byte)'D', (byte)'S', (byte)' ' }, 0, hdr, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(124), 0, hdr, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(flags), 0, hdr, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(h), 0, hdr, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(w), 0, hdr, 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(finalSize), 0, hdr, 20, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(numMipmaps), 0, hdr, 28, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(32), 0, hdr, 76, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(pflags), 0, hdr, 80, 4);

            if (compressed)
            {
                Buffer.BlockCopy(fourcc, 0, hdr, 84, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(fmtbpp << 3), 0, hdr, 88, 4);

                uint val0 = compSels.ContainsKey(compSel[0]) ? compSels[compSel[0]] : compSels[0];
                uint val1 = compSels.ContainsKey(compSel[1]) ? compSels[compSel[1]] : compSels[1];
                uint val2 = compSels.ContainsKey(compSel[2]) ? compSels[compSel[2]] : compSels[2];
                uint val3 = compSels.ContainsKey(compSel[3]) ? compSels[compSel[3]] : compSels[3];

                Buffer.BlockCopy(BitConverter.GetBytes(val0), 0, hdr, 92, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(val1), 0, hdr, 96, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(val2), 0, hdr, 100, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(val3), 0, hdr, 104, 4);
            }

            Buffer.BlockCopy(BitConverter.GetBytes(caps), 0, hdr, 108, 4);

            if (format_ is string strFmt2)
            {
                if (strFmt2 == "BC4U")
                {
                    byte[] extra = new byte[] { 0x50, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                    byte[] newHdr = new byte[hdr.Length + extra.Length];
                    Buffer.BlockCopy(hdr, 0, newHdr, 0, hdr.Length);
                    Buffer.BlockCopy(extra, 0, newHdr, hdr.Length, extra.Length);
                    hdr = newHdr;
                }
                else if (strFmt2 == "BC4S")
                {
                    byte[] extra = new byte[] { 0x51, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                    byte[] newHdr = new byte[hdr.Length + extra.Length];
                    Buffer.BlockCopy(hdr, 0, newHdr, 0, hdr.Length);
                    Buffer.BlockCopy(extra, 0, newHdr, hdr.Length, extra.Length);
                    hdr = newHdr;
                }
                else if (strFmt2 == "BC5U")
                {
                    byte[] extra = new byte[] { 0x53, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                    byte[] newHdr = new byte[hdr.Length + extra.Length];
                    Buffer.BlockCopy(hdr, 0, newHdr, 0, hdr.Length);
                    Buffer.BlockCopy(extra, 0, newHdr, hdr.Length, extra.Length);
                    hdr = newHdr;
                }
                else if (strFmt2 == "BC5S")
                {
                    byte[] extra = new byte[] { 0x54, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                    byte[] newHdr = new byte[hdr.Length + extra.Length];
                    Buffer.BlockCopy(hdr, 0, newHdr, 0, hdr.Length);
                    Buffer.BlockCopy(extra, 0, newHdr, hdr.Length, extra.Length);
                    hdr = newHdr;
                }
            }

            return hdr;
        }

        protected new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}