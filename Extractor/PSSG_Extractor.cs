using System.Text;
using MiscUtil.Conversion;
using MiscUtil.IO;

namespace super_toolbox
{
    public class PSSG_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath} 不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var pssgFiles = Directory.EnumerateFiles(directoryPath, "*.pssg", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = pssgFiles.Count;

            try
            {
                foreach (var pssgFilePath in pssgFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    try
                    {
                        ExtractionProgress?.Invoke(this, $"正在处理PSSG文件:{Path.GetFileName(pssgFilePath)}");

                        using (FileStream fs = new FileStream(pssgFilePath, FileMode.Open))
                        {
                            CPSSGFile pssgFile = new CPSSGFile(fs);

                            List<CNode> textureNodes = pssgFile.FindNodes("TEXTURE");

                            if (textureNodes.Count == 0)
                            {
                                ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(pssgFilePath)}中未找到纹理节点");
                                continue;
                            }

                            string baseFileName = Path.GetFileNameWithoutExtension(pssgFilePath);
                            ExtractionProgress?.Invoke(this, $"在{Path.GetFileName(pssgFilePath)}中找到{textureNodes.Count}个纹理");

                            for (int i = 0; i < textureNodes.Count; i++)
                            {
                                ThrowIfCancellationRequested(cancellationToken);

                                try
                                {
                                    string textureName = $"{baseFileName}_{i + 1}";
                                    string fileName = CleanFileName(textureName) + ".dds";
                                    string filePath = Path.Combine(extractedDir, fileName);

                                    DDS dds = new DDS(textureNodes[i], false);

                                    using (FileStream ddsStream = new FileStream(filePath, FileMode.Create))
                                    {
                                        dds.Write(ddsStream, -1);
                                    }

                                    extractedFiles.Add(filePath);
                                    OnFileExtracted(filePath);
                                    ExtractionProgress?.Invoke(this, $"已提取纹理:{fileName}");
                                }
                                catch (Exception ex)
                                {
                                    ExtractionError?.Invoke(this, $"提取纹理失败:{ex.Message}");
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(pssgFilePath)}时出错:{ex.Message}");
                    }
                }

                if (extractedFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个DDS纹理");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，但未找到任何可提取的纹理");
                }

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取过程中出现错误:{ex.Message}");
                OnExtractionFailed($"提取过程中出现错误:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private static string CleanFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }
    }

    internal class CPSSGFile
    {
        public CPSSGFile(Stream fileStream)
        {
            using (EndianBinaryReaderEx endianBinaryReaderEx = new EndianBinaryReaderEx(new BigEndianBitConverter(), fileStream))
            {
                this.magic = endianBinaryReaderEx.ReadPSSGString(4);
                if (this.magic != "PSSG")
                {
                    throw new Exception("这不是一个PSSG文件!");
                }
                int num = endianBinaryReaderEx.ReadInt32();
                int num2 = endianBinaryReaderEx.ReadInt32();
                int num3 = endianBinaryReaderEx.ReadInt32();
                this.attributeInfo = new CAttributeInfo[num2];
                this.nodeInfo = new CNodeInfo[num3];
                for (int i = 0; i < num3; i++)
                {
                    this.nodeInfo[i] = new CNodeInfo(endianBinaryReaderEx, this);
                }
                long position = endianBinaryReaderEx.BaseStream.Position;
                this.rootNode = new CNode(endianBinaryReaderEx, this, null, true);
                if (endianBinaryReaderEx.BaseStream.Position < endianBinaryReaderEx.BaseStream.Length)
                {
                    endianBinaryReaderEx.BaseStream.Position = position;
                    this.rootNode = new CNode(endianBinaryReaderEx, this, null, false);
                    if (endianBinaryReaderEx.BaseStream.Position < endianBinaryReaderEx.BaseStream.Length)
                    {
                        throw new Exception("文件保存不正确，不被此版本的PSSG编辑器支持");
                    }
                }
            }
        }

        public List<CNode> FindNodes(string name)
        {
            return this.rootNode.FindNodes(name);
        }

        public List<CNode> FindNodes(string name, string attributeName, string attributeValue)
        {
            return this.rootNode.FindNodes(name, attributeName, attributeValue);
        }

        public string magic;
        public CNodeInfo[] nodeInfo;
        public CAttributeInfo[] attributeInfo;
        public CNode rootNode;
    }

    internal class CNode
    {
        public CNode(EndianBinaryReaderEx reader, CPSSGFile file, CNode? node, bool useDataNodeCheck)
        {
            this.attributes = new Dictionary<string, CAttribute>();
            this.subNodes = new CNode[0];
            this.data = new byte[0];

            this.file = file;
            this.ParentNode = node;
            this.id = reader.ReadInt32();
            this.size = reader.ReadInt32();
            long num = reader.BaseStream.Position + (long)this.size;
            this.attributeSize = reader.ReadInt32();
            long num2 = reader.BaseStream.Position + (long)this.attributeSize;

            if (num2 > reader.BaseStream.Length || num > reader.BaseStream.Length)
            {
                throw new Exception("文件保存不正确，不被此版本的PSSG编辑器支持");
            }

            this.attributes = new Dictionary<string, CAttribute>();
            while (reader.BaseStream.Position < num2)
            {
                CAttribute cattribute = new CAttribute(reader, file, this);
                this.attributes.Add(cattribute.Name, cattribute);
            }

            string name = this.Name;
            switch (name)
            {
                case "BOUNDINGBOX":
                case "DATA":
                case "DATABLOCKDATA":
                case "DATABLOCKBUFFERED":
                case "INDEXSOURCEDATA":
                case "INVERSEBINDMATRIX":
                case "MODIFIERNETWORKINSTANCEUNIQUEMODIFIERINPUT":
                case "NeAnimPacketData_B1":
                case "NeAnimPacketData_B4":
                case "RENDERINTERFACEBOUNDBUFFERED":
                case "SHADERINPUT":
                case "TEXTUREIMAGEBLOCKDATA":
                case "TRANSFORM":
                    this.isDataNode = true;
                    break;
            }

            if (!this.isDataNode && useDataNodeCheck)
            {
                long position = reader.BaseStream.Position;
                while (reader.BaseStream.Position < num)
                {
                    int num4 = reader.ReadInt32();
                    if (num4 > file.nodeInfo.Length || num4 < 0)
                    {
                        this.isDataNode = true;
                        break;
                    }
                    int num5 = reader.ReadInt32();
                    if (reader.BaseStream.Position + (long)num5 > num || (num5 == 0 && num4 == 0) || num5 < 0)
                    {
                        this.isDataNode = true;
                        break;
                    }
                    if (reader.BaseStream.Position + (long)num5 == num)
                    {
                        break;
                    }
                    reader.BaseStream.Position += (long)num5;
                }
                reader.BaseStream.Position = position;
            }

            if (this.isDataNode)
            {
                this.data = reader.ReadBytes((int)(num - reader.BaseStream.Position));
            }
            else
            {
                List<CNode> nodesList = new List<CNode>();
                while (reader.BaseStream.Position < num)
                {
                    nodesList.Add(new CNode(reader, file, this, useDataNodeCheck));
                }
                this.subNodes = nodesList.ToArray();
            }
        }

        public List<CNode> FindNodes(string nodeName)
        {
            return this.FindNodes(nodeName, "", "");
        }

        public List<CNode> FindNodes(string nodeName, string attributeName, string attributeValue)
        {
            List<CNode> list = new List<CNode>();
            if (this.Name == nodeName)
            {
                if (!string.IsNullOrEmpty(attributeName) && !string.IsNullOrEmpty(attributeValue))
                {
                    if (this.attributes.TryGetValue(attributeName, out CAttribute? cattribute) && cattribute.ToString() == attributeValue)
                    {
                        list.Add(this);
                    }
                }
                else
                {
                    list.Add(this);
                }
            }
            if (this.subNodes != null)
            {
                foreach (CNode cnode in this.subNodes)
                {
                    list.AddRange(cnode.FindNodes(nodeName, attributeName, attributeValue));
                }
            }
            return list;
        }

        public string Name
        {
            get
            {
                return this.file.nodeInfo[this.id - 1].name;
            }
        }

        public int id;
        private int size;
        private int attributeSize;
        public Dictionary<string, CAttribute> attributes;
        public CNode[] subNodes = new CNode[0];
        public bool isDataNode = false;
        public byte[] data = new byte[0];
        private CPSSGFile file;
        public CNode? ParentNode;
    }

    internal class CAttribute
    {
        public CAttribute(EndianBinaryReaderEx reader, CPSSGFile file, CNode node)
        {
            this.file = file ?? throw new ArgumentNullException(nameof(file));
            this.ParentNode = node ?? throw new ArgumentNullException(nameof(node));
            this.id = reader.ReadInt32();
            this.size = reader.ReadInt32();
            if (this.size == 4)
            {
                this.data = reader.ReadBytes(this.size);
            }
            else
            {
                if (this.size > 4)
                {
                    int num = reader.ReadInt32();
                    if (this.size - 4 == num)
                    {
                        this.data = reader.ReadPSSGString(num);
                        return;
                    }
                    reader.Seek(-4, SeekOrigin.Current);
                }
                this.data = reader.ReadBytes(this.size);
            }
        }

        public string Name
        {
            get
            {
                return this.file.attributeInfo[this.id - 1].name;
            }
        }

        public object Value
        {
            get
            {
                object obj = "data";
                object obj2;
                if (this.data is string strData)
                {
                    obj2 = strData;
                }
                else if (this.data is byte[] byteData && byteData.Length == 4)
                {
                    obj = EndianBitConverter.Big.ToInt32(byteData, 0);

                    if (this.ParentNode.Name == "FETEXTLAYOUT")
                    {
                        if (this.Name == "height" || this.Name == "depth" || this.Name == "tracking")
                        {
                            obj = EndianBitConverter.Big.ToSingle(byteData, 0);
                        }
                    }
                    obj2 = obj;
                }
                else
                {
                    obj2 = "data";
                }
                return obj2;
            }
        }

        public override string ToString()
        {
            return this.Value switch
            {
                string str => str,
                int intVal => intVal.ToString(),
                float floatVal => floatVal.ToString(),
                _ => "data"
            };
        }

        public int id;
        private int size;
        public object data;
        private CPSSGFile file;
        public CNode ParentNode;
    }

    internal class CNodeInfo
    {
        public CNodeInfo(EndianBinaryReaderEx reader, CPSSGFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            this.attributeInfo = new Dictionary<int, CAttributeInfo>();
            this.id = reader.ReadInt32();
            this.name = reader.ReadPSSGString();
            int num = reader.ReadInt32();
            for (int i = 0; i < num; i++)
            {
                CAttributeInfo cattributeInfo = new CAttributeInfo(reader);
                this.attributeInfo.Add(cattributeInfo.id, cattributeInfo);
                file.attributeInfo[cattributeInfo.id - 1] = cattributeInfo;
            }
        }

        public int id;
        public string name;
        public Dictionary<int, CAttributeInfo> attributeInfo;
    }

    internal class CAttributeInfo
    {
        public CAttributeInfo(EndianBinaryReaderEx reader)
        {
            this.id = reader.ReadInt32();
            this.name = reader.ReadPSSGString();
        }

        public int id;
        public string name;
    }

    internal class EndianBinaryReaderEx : EndianBinaryReader
    {
        public EndianBinaryReaderEx(EndianBitConverter bitConvertor, Stream stream)
            : base(bitConvertor ?? throw new ArgumentNullException(nameof(bitConvertor)),
                  stream ?? throw new ArgumentNullException(nameof(stream)))
        {
        }

        public string ReadPSSGString()
        {
            int num = base.ReadInt32();
            return this.ReadPSSGString(num);
        }

        public string ReadPSSGString(int length)
        {
            return Encoding.UTF8.GetString(base.ReadBytes(length));
        }
    }

    public struct DDS_PIXELFORMAT
    {
        public uint size;
        public DDS_PIXELFORMAT.Flags flags;
        public uint fourCC;
        public uint rGBBitCount;
        public uint rBitMask;
        public uint gBitMask;
        public uint bBitMask;
        public uint aBitMask;

        public enum Flags
        {
            DDPF_ALPHAPIXELS = 2,
            DDPF_ALPHA = 2,
            DDPF_FOURCC = 4,
            DDPF_RGB = 64,
            DDPF_YUV = 512,
            DDPF_LUMINANCE = 131072
        }
    }

    public struct DDS_HEADER
    {
        public uint size;
        public DDS_HEADER.Flags flags;
        public uint height;
        public uint width;
        public uint pitchOrLinearSize;
        public uint depth;
        public uint mipMapCount;
        public uint[] reserved1;
        public DDS_PIXELFORMAT ddspf;
        public DDS_HEADER.Caps caps;
        public DDS_HEADER.Caps2 caps2;
        public uint caps3;
        public uint caps4;
        public uint reserved2;

        public enum Flags
        {
            DDSD_CAPS = 1,
            DDSD_HEIGHT,
            DDSD_WIDTH = 4,
            DDSD_PITCH = 8,
            DDSD_PIXELFORMAT = 4096,
            DDSD_MIPMAPCOUNT = 131072,
            DDSD_LINEARSIZE = 524288,
            DDSD_DEPTH = 8388608
        }

        public enum Caps
        {
            DDSCAPS_COMPLEX = 8,
            DDSCAPS_MIPMAP = 4194304,
            DDSCAPS_TEXTURE = 4096
        }

        public enum Caps2
        {
            DDSCAPS2_CUBEMAP = 512,
            DDSCAPS2_CUBEMAP_POSITIVEX = 1024,
            DDSCAPS2_CUBEMAP_NEGATIVEX = 2048,
            DDSCAPS2_CUBEMAP_POSITIVEY = 4096,
            DDSCAPS2_CUBEMAP_NEGATIVEY = 8192,
            DDSCAPS2_CUBEMAP_POSITIVEZ = 16384,
            DDSCAPS2_CUBEMAP_NEGATIVEZ = 32768,
            DDSCAPS2_VOLUME = 2097152
        }
    }

    internal class DDS
    {
        public DDS(CNode node, bool cubePreview)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            this.magic = 542327876U;
            this.header.size = 124U;
            this.header.flags = this.header.flags | (DDS_HEADER.Flags)4103;

            if (!node.attributes.TryGetValue("height", out var heightAttr) || heightAttr.Value == null)
                throw new InvalidDataException("纹理节点缺少height属性");

            if (!node.attributes.TryGetValue("width", out var widthAttr) || widthAttr.Value == null)
                throw new InvalidDataException("纹理节点缺少width属性");

            this.header.height = (uint)((int)heightAttr.Value);
            this.header.width = (uint)((int)widthAttr.Value);

            string? text = node.attributes.TryGetValue("texelFormat", out var formatAttr) ?
                          formatAttr.Value?.ToString() : null;

            if (!string.IsNullOrEmpty(text))
            {
                if (text == "dxt1")
                {
                    this.header.flags = this.header.flags | DDS_HEADER.Flags.DDSD_LINEARSIZE;
                    this.header.pitchOrLinearSize = (uint)(Math.Max(1, ((int)widthAttr.Value + 3) / 4) * 8);
                    this.header.ddspf.flags = this.header.ddspf.flags | DDS_PIXELFORMAT.Flags.DDPF_FOURCC;
                    this.header.ddspf.fourCC = BitConverter.ToUInt32(Encoding.UTF8.GetBytes(text.ToUpper()), 0);
                }
                else if (text is "dxt2" or "dxt3" or "dxt4" or "dxt5")
                {
                    this.header.flags = this.header.flags | DDS_HEADER.Flags.DDSD_LINEARSIZE;
                    this.header.pitchOrLinearSize = (uint)(Math.Max(1, ((int)widthAttr.Value + 3) / 4) * 16);
                    this.header.ddspf.flags = this.header.ddspf.flags | DDS_PIXELFORMAT.Flags.DDPF_FOURCC;
                    this.header.ddspf.fourCC = BitConverter.ToUInt32(Encoding.UTF8.GetBytes(text.ToUpper()), 0);
                }
            }

            if (node.attributes.TryGetValue("numberMipMapLevels", out var mipAttr) && mipAttr.Value != null)
            {
                int mipCount = (int)mipAttr.Value;
                if (mipCount > 0)
                {
                    this.header.flags = this.header.flags | DDS_HEADER.Flags.DDSD_MIPMAPCOUNT;
                    this.header.mipMapCount = (uint)(mipCount + 1);
                    this.header.caps = this.header.caps | (DDS_HEADER.Caps)4194312;
                }
            }

            this.header.reserved1 = new uint[11];
            this.header.ddspf.size = 32U;
            this.header.caps = this.header.caps | DDS_HEADER.Caps.DDSCAPS_TEXTURE;

            List<CNode> list = node.FindNodes("TEXTUREIMAGEBLOCK");

            if (node.attributes.TryGetValue("imageBlockCount", out var blockCountAttr) &&
                blockCountAttr.Value != null && (int)blockCountAttr.Value > 1)
            {
                this.bdata2 = new Dictionary<int, byte[]>();
                for (int i = 0; i < list.Count; i++)
                {
                    text = list[i].attributes["typename"].ToString();
                    if (text != null)
                    {
                        var dataNodes = list[i].FindNodes("TEXTUREIMAGEBLOCKDATA");
                        if (dataNodes.Count == 0)
                            throw new InvalidDataException("未找到TEXTUREIMAGEBLOCKDATA节点");

                        byte[] blockData = dataNodes[0].data;

                        switch (text)
                        {
                            case "Raw":
                                this.header.caps2 |= DDS_HEADER.Caps2.DDSCAPS2_CUBEMAP_POSITIVEX;
                                this.bdata2.Add(0, blockData);
                                break;
                            case "RawNegativeX":
                                this.header.caps2 |= DDS_HEADER.Caps2.DDSCAPS2_CUBEMAP_NEGATIVEX;
                                this.bdata2.Add(1, blockData);
                                break;
                            case "RawPositiveY":
                                this.header.caps2 |= DDS_HEADER.Caps2.DDSCAPS2_CUBEMAP_POSITIVEY;
                                this.bdata2.Add(2, blockData);
                                break;
                            case "RawNegativeY":
                                this.header.caps2 |= DDS_HEADER.Caps2.DDSCAPS2_CUBEMAP_NEGATIVEY;
                                this.bdata2.Add(3, blockData);
                                break;
                            case "RawPositiveZ":
                                this.header.caps2 |= DDS_HEADER.Caps2.DDSCAPS2_CUBEMAP_POSITIVEZ;
                                this.bdata2.Add(4, blockData);
                                break;
                            case "RawNegativeZ":
                                this.header.caps2 |= DDS_HEADER.Caps2.DDSCAPS2_CUBEMAP_NEGATIVEZ;
                                this.bdata2.Add(5, blockData);
                                break;
                        }
                    }
                }

                if (!cubePreview)
                {
                    if (this.bdata2.Count != (int)blockCountAttr.Value)
                    {
                        throw new Exception("加载立方体贴图失败，因为未找到所有块");
                    }
                    this.header.caps2 |= DDS_HEADER.Caps2.DDSCAPS2_CUBEMAP;
                    this.header.flags &= ~DDS_HEADER.Flags.DDSD_LINEARSIZE;
                    this.header.pitchOrLinearSize = 0U;
                    this.header.caps |= DDS_HEADER.Caps.DDSCAPS_COMPLEX;
                }
                else
                {
                    this.header.caps2 = 0;
                }
            }
            else
            {
                if (list.Count == 0)
                    throw new InvalidDataException("未找到TEXTUREIMAGEBLOCK节点");

                var dataNode = list[0].FindNodes("TEXTUREIMAGEBLOCKDATA").FirstOrDefault();
                if (dataNode == null)
                    throw new InvalidDataException("未找到TEXTUREIMAGEBLOCKDATA节点");

                this.bdata = dataNode.data;
            }
        }

        public void Write(Stream fileStream, int cubeIndex)
        {
            if (fileStream == null) throw new ArgumentNullException(nameof(fileStream));

            using (BinaryWriter binaryWriter = new BinaryWriter(fileStream))
            {
                binaryWriter.Write(this.magic);
                binaryWriter.Write(this.header.size);
                binaryWriter.Write((uint)this.header.flags);
                binaryWriter.Write(this.header.height);
                binaryWriter.Write(this.header.width);
                binaryWriter.Write(this.header.pitchOrLinearSize);
                binaryWriter.Write(this.header.depth);
                binaryWriter.Write(this.header.mipMapCount);
                foreach (uint num in this.header.reserved1)
                {
                    binaryWriter.Write(num);
                }
                binaryWriter.Write(this.header.ddspf.size);
                binaryWriter.Write((uint)this.header.ddspf.flags);
                binaryWriter.Write(this.header.ddspf.fourCC);
                binaryWriter.Write(this.header.ddspf.rGBBitCount);
                binaryWriter.Write(this.header.ddspf.rBitMask);
                binaryWriter.Write(this.header.ddspf.gBitMask);
                binaryWriter.Write(this.header.ddspf.bBitMask);
                binaryWriter.Write(this.header.ddspf.aBitMask);
                binaryWriter.Write((uint)this.header.caps);
                binaryWriter.Write((uint)this.header.caps2);
                binaryWriter.Write(this.header.caps3);
                binaryWriter.Write(this.header.caps4);
                binaryWriter.Write(this.header.reserved2);

                if (cubeIndex != -1)
                {
                    if (this.bdata2 != null && this.bdata2.TryGetValue(cubeIndex, out byte[]? data))
                    {
                        binaryWriter.Write(data);
                    }
                }
                else if (this.bdata2 != null && this.bdata2.Count > 0)
                {
                    for (int j = 0; j < this.bdata2.Count; j++)
                    {
                        if (this.bdata2.TryGetValue(j, out byte[]? data))
                        {
                            binaryWriter.Write(data);
                        }
                    }
                }
                else
                {
                    binaryWriter.Write(this.bdata);
                }
            }
        }

        private uint magic;
        private DDS_HEADER header;
        private byte[] bdata = new byte[0];
        private Dictionary<int, byte[]>? bdata2;
    }
}