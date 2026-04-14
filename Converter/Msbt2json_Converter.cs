using System.Text;
using System.Text.Json;

namespace super_toolbox
{
    public class Msbt2json_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private const int MSBT_HEADER_LEN = 0x20;
        private const int LBL1_HEADER_LEN = 0x14;
        private const int ATR1_HEADER_LEN = 0x14;
        private const int TXT2_HEADER_LEN = 0x14;

        private const string MSBT_MAGIC = "MsgStdBn";
        private const string LBL1_MAGIC = "LBL1";
        private const string ATR1_MAGIC = "ATR1";
        private const string TXT2_MAGIC = "TXT2";

        private const byte SECTION_END_MAGIC = 0xAB;

        private class MsbtData
        {
            public string? order { get; set; }
            public bool invalid { get; set; }
            public long file_size { get; set; }
            public List<object> header_unknowns { get; set; } = new List<object>();
            public Dictionary<string, SectionData> sections { get; set; } = new Dictionary<string, SectionData>();
            public List<string> section_order { get; set; } = new List<string>();
            public int section_count { get; set; }
            public int encoding { get; set; }
        }

        private class SectionData
        {
            public Dictionary<string, object> header { get; set; } = new Dictionary<string, object>();
            public object? data { get; set; }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var msbtFiles = Directory.GetFiles(directoryPath, "*.msbt", SearchOption.AllDirectories)
                    .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                    .ToArray();

            TotalFilesToConvert = msbtFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var msbtFilePath in msbtFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(msbtFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.msbt");

                    string fileDirectory = Path.GetDirectoryName(msbtFilePath) ?? string.Empty;

                    try
                    {
                        string jsonFile = Path.Combine(fileDirectory, $"{fileName}.json");

                        if (File.Exists(jsonFile))
                            File.Delete(jsonFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertMsbtToJson(msbtFilePath, jsonFile, cancellationToken));

                        if (conversionSuccess && File.Exists(jsonFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(jsonFile)}");
                            OnFileConverted(jsonFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.msbt转换失败");
                            OnConversionFailed($"{fileName}.msbt转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.msbt处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");
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

        private bool ConvertMsbtToJson(string msbtFilePath, string jsonFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取MSBT文件:{Path.GetFileName(msbtFilePath)}");

                var msbt = new MsbtData();
                msbt.file_size = new FileInfo(msbtFilePath).Length;

                using (var fs = new FileStream(msbtFilePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] headerData = new byte[MSBT_HEADER_LEN];
                    fs.Read(headerData, 0, MSBT_HEADER_LEN);
                    ParseHeader(headerData, msbt);

                    if (msbt.invalid)
                        return false;

                    int position = MSBT_HEADER_LEN;
                    int sectionsLeft = msbt.section_count;

                    while (sectionsLeft > 0 && position < msbt.file_size)
                    {
                        fs.Seek(position, SeekOrigin.Begin);
                        byte[] magicBytes = new byte[4];
                        fs.Read(magicBytes, 0, 4);
                        string magic = Encoding.ASCII.GetString(magicBytes);

                        if (magic == LBL1_MAGIC)
                        {
                            byte[] lbl1Header = new byte[LBL1_HEADER_LEN];
                            fs.Seek(position, SeekOrigin.Begin);
                            fs.Read(lbl1Header, 0, LBL1_HEADER_LEN);
                            ParseLbl1Header(lbl1Header, msbt);
                            position += LBL1_HEADER_LEN;

                            if (msbt.invalid)
                                return false;

                            SectionData? section = null;
                            if (msbt.sections.TryGetValue("LBL1", out section) &&
                                section.header.TryGetValue("size", out object? sizeObj))
                            {
                                int sectionSize = Convert.ToInt32(sizeObj);
                                byte[] lbl1Data = new byte[sectionSize];
                                fs.Read(lbl1Data, 0, sectionSize);
                                ParseLbl1Data(lbl1Data, msbt);
                                position += sectionSize;
                            }
                        }
                        else if (magic == ATR1_MAGIC)
                        {
                            byte[] atr1Header = new byte[ATR1_HEADER_LEN];
                            fs.Seek(position, SeekOrigin.Begin);
                            fs.Read(atr1Header, 0, ATR1_HEADER_LEN);
                            ParseAtr1Header(atr1Header, msbt);
                            position += ATR1_HEADER_LEN;

                            if (msbt.invalid)
                                return false;

                            SectionData? section = null;
                            if (msbt.sections.TryGetValue("ATR1", out section) &&
                                section.header.TryGetValue("size", out object? sizeObj))
                            {
                                int sectionSize = Convert.ToInt32(sizeObj);
                                fs.Seek(sectionSize, SeekOrigin.Current);
                                position += sectionSize;
                            }
                        }
                        else if (magic == TXT2_MAGIC)
                        {
                            byte[] txt2Header = new byte[TXT2_HEADER_LEN];
                            fs.Seek(position, SeekOrigin.Begin);
                            fs.Read(txt2Header, 0, TXT2_HEADER_LEN);
                            ParseTxt2Header(txt2Header, msbt);
                            position += TXT2_HEADER_LEN;

                            if (msbt.invalid)
                                return false;

                            SectionData? section = null;
                            if (msbt.sections.TryGetValue("TXT2", out section) &&
                                section.header.TryGetValue("size", out object? sizeObj))
                            {
                                int sectionSize = Convert.ToInt32(sizeObj);
                                byte[] txt2Data = new byte[sectionSize];
                                fs.Read(txt2Data, 0, sectionSize);
                                ParseTxt2Data(txt2Data, msbt);
                                position += sectionSize;
                            }
                        }
                        else
                        {
                            byte[] sizeBytes = new byte[4];
                            fs.Seek(position + 4, SeekOrigin.Begin);
                            fs.Read(sizeBytes, 0, 4);
                            int sectionSize = msbt.order == ">" ?
                                (sizeBytes[0] << 24) | (sizeBytes[1] << 16) | (sizeBytes[2] << 8) | sizeBytes[3] :
                                (sizeBytes[3] << 24) | (sizeBytes[2] << 16) | (sizeBytes[1] << 8) | sizeBytes[0];
                            position += sectionSize + TXT2_HEADER_LEN;
                        }

                        sectionsLeft--;
                        msbt.section_order.Add(magic);

                        fs.Seek(position, SeekOrigin.Begin);
                        while (position < msbt.file_size)
                        {
                            int nextByte = fs.ReadByte();
                            if (nextByte != SECTION_END_MAGIC)
                            {
                                fs.Seek(-1, SeekOrigin.Current);
                                break;
                            }
                            position++;
                        }
                    }
                }

                var output = new Dictionary<string, object>
                {
                    ["strings"] = new Dictionary<string, object>(),
                    ["structure"] = new Dictionary<string, object>()
                };

                var strings = (Dictionary<string, object>)output["strings"];
                var structure = (Dictionary<string, object>)output["structure"];

                SectionData? lbl1Section = null;
                if (msbt.sections.TryGetValue("LBL1", out lbl1Section) && lbl1Section.data != null)
                {
                    var labelLists = lbl1Section.data as List<List<object>>;
                    if (labelLists != null)
                    {
                        foreach (var labelList in labelLists)
                        {
                            if (labelList.Count > 0)
                            {
                                var labels = labelList[0] as List<Tuple<int, byte[]>>;
                                if (labels != null)
                                {
                                    foreach (var label in labels)
                                    {
                                        int id = label.Item1;
                                        string name = Encoding.UTF8.GetString(label.Item2);

                                        SectionData? txt2Section = null;
                                        if (msbt.sections.TryGetValue("TXT2", out txt2Section) && txt2Section.data != null)
                                        {
                                            var txt2Data = txt2Section.data as List<List<string>>;
                                            if (txt2Data != null && id < txt2Data.Count)
                                            {
                                                var value = txt2Data[id];
                                                strings[name] = value;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                var msbtHeader = new Dictionary<string, object>
                {
                    ["byte_order"] = msbt.order ?? "",
                    ["encoding"] = msbt.encoding == 0 ? "UTF-8" : "UTF-16",
                    ["sections"] = msbt.section_count,
                    ["section_order"] = msbt.section_order,
                    ["unknowns"] = msbt.header_unknowns
                };

                structure["MSBT"] = new Dictionary<string, object>
                {
                    ["header"] = msbtHeader
                };

                foreach (var sectionKey in msbt.sections.Keys)
                {
                    if (msbt.sections.TryGetValue(sectionKey, out SectionData? sectionValue))
                    {
                        var sectionDict = new Dictionary<string, object>();

                        if (sectionValue != null)
                        {
                            sectionDict["header"] = sectionValue.header;

                            if (sectionKey == "LBL1" && sectionValue.data != null)
                            {
                                sectionDict["lists"] = sectionValue.data;
                            }

                            structure[sectionKey] = sectionDict;
                        }
                    }
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string jsonString = JsonSerializer.Serialize(output, options);
                File.WriteAllText(jsonFilePath, jsonString, Encoding.UTF8);

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
        }

        private void ParseHeader(byte[] data, MsbtData msbt)
        {
            string magic = Encoding.ASCII.GetString(data, 0, 8);
            if (magic != MSBT_MAGIC)
            {
                msbt.invalid = true;
                return;
            }

            ushort bom = BitConverter.ToUInt16(data, 8);
            if (bom == 0xFFFE)
                msbt.order = ">";
            else if (bom == 0xFEFF)
                msbt.order = "<";
            else
            {
                msbt.invalid = true;
                return;
            }

            msbt.encoding = data[0x0A];
            msbt.section_count = BitConverter.ToUInt16(data, 0x0E);

            msbt.header_unknowns = new List<object>
            {
                data[0x0C],
                data[0x0D],
                BitConverter.ToUInt16(data, 0x10),
                Encoding.ASCII.GetString(data, 0x14, 10)
            };
        }

        private void ParseLbl1Header(byte[] data, MsbtData msbt)
        {
            string magic = Encoding.ASCII.GetString(data, 0, 4);
            if (magic != LBL1_MAGIC)
            {
                msbt.invalid = true;
                return;
            }

            int size = msbt.order == ">" ?
                (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7] :
                (data[7] << 24) | (data[6] << 16) | (data[5] << 8) | data[4];

            byte[] unknown = new byte[8];
            Array.Copy(data, 8, unknown, 0, 8);

            int entries = msbt.order == ">" ?
                (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19] :
                (data[19] << 24) | (data[18] << 16) | (data[17] << 8) | data[16];

            var section = new SectionData();
            section.header["size"] = size - 4;
            section.header["entries"] = entries;
            section.header["unknown"] = unknown;
            msbt.sections["LBL1"] = section;
        }

        private void ParseLbl1Data(byte[] data, MsbtData msbt)
        {
            SectionData? section = null;
            if (!msbt.sections.TryGetValue("LBL1", out section) ||
                !section.header.TryGetValue("entries", out object? entriesObj))
            {
                return;
            }

            int entries = Convert.ToInt32(entriesObj);
            int position = 0;
            var lists = new List<List<object>>();

            for (int entry = 0; entry < entries; entry++)
            {
                if (position + 8 > data.Length) break;

                int count = msbt.order == ">" ?
                    (data[position] << 24) | (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3] :
                    (data[position + 3] << 24) | (data[position + 2] << 16) | (data[position + 1] << 8) | data[position];

                int offset = msbt.order == ">" ?
                    (data[position + 4] << 24) | (data[position + 5] << 16) | (data[position + 6] << 8) | data[position + 7] :
                    (data[position + 7] << 24) | (data[position + 6] << 16) | (data[position + 5] << 8) | data[position + 4];

                position += 8;
                offset -= 4;

                var list = new List<Tuple<int, byte[]>>();

                for (int i = 0; i < count; i++)
                {
                    if (offset >= data.Length) break;

                    byte length = data[offset];
                    if (length == 0 || offset + 1 + length + 4 > data.Length) break;

                    int nameEnd = offset + length + 1;
                    byte[] name = new byte[length];
                    Array.Copy(data, offset + 1, name, 0, length);

                    int idOffset = nameEnd;
                    int id = msbt.order == ">" ?
                        (data[idOffset] << 24) | (data[idOffset + 1] << 16) | (data[idOffset + 2] << 8) | data[idOffset + 3] :
                        (data[idOffset + 3] << 24) | (data[idOffset + 2] << 16) | (data[idOffset + 1] << 8) | data[idOffset];

                    list.Add(new Tuple<int, byte[]>(id, name));
                    offset = idOffset + 4;
                }

                lists.Add(new List<object> { list, offset });
            }

            section.data = lists;
        }

        private void ParseAtr1Header(byte[] data, MsbtData msbt)
        {
            string magic = Encoding.ASCII.GetString(data, 0, 4);
            if (magic != ATR1_MAGIC)
            {
                msbt.invalid = true;
                return;
            }

            int size = msbt.order == ">" ?
                (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7] :
                (data[7] << 24) | (data[6] << 16) | (data[5] << 8) | data[4];

            int unknown1 = msbt.order == ">" ?
                (data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11] :
                (data[11] << 24) | (data[10] << 16) | (data[9] << 8) | data[8];

            int unknown2 = msbt.order == ">" ?
                (data[12] << 24) | (data[13] << 16) | (data[14] << 8) | data[15] :
                (data[15] << 24) | (data[14] << 16) | (data[13] << 8) | data[12];

            int entries = msbt.order == ">" ?
                (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19] :
                (data[19] << 24) | (data[18] << 16) | (data[17] << 8) | data[16];

            var section = new SectionData();
            section.header["size"] = size - 4;
            section.header["entries"] = entries;
            section.header["unknown1"] = unknown1;
            section.header["unknown2"] = unknown2;
            msbt.sections["ATR1"] = section;
        }

        private void ParseTxt2Header(byte[] data, MsbtData msbt)
        {
            string magic = Encoding.ASCII.GetString(data, 0, 4);
            if (magic != TXT2_MAGIC)
            {
                msbt.invalid = true;
                return;
            }

            int size = msbt.order == ">" ?
                (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7] :
                (data[7] << 24) | (data[6] << 16) | (data[5] << 8) | data[4];

            int unknown1 = msbt.order == ">" ?
                (data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11] :
                (data[11] << 24) | (data[10] << 16) | (data[9] << 8) | data[8];

            int unknown2 = msbt.order == ">" ?
                (data[12] << 24) | (data[13] << 16) | (data[14] << 8) | data[15] :
                (data[15] << 24) | (data[14] << 16) | (data[13] << 8) | data[12];

            int entries = msbt.order == ">" ?
                (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19] :
                (data[19] << 24) | (data[18] << 16) | (data[17] << 8) | data[16];

            var section = new SectionData();
            section.header["size"] = size - 4;
            section.header["entries"] = entries;
            section.header["unknown1"] = unknown1;
            section.header["unknown2"] = unknown2;
            msbt.sections["TXT2"] = section;
        }

        private void ParseTxt2Data(byte[] data, MsbtData msbt)
        {
            SectionData? section = null;
            if (!msbt.sections.TryGetValue("TXT2", out section) ||
                !section.header.TryGetValue("entries", out object? entriesObj))
            {
                return;
            }

            int entries = Convert.ToInt32(entriesObj);
            int dataLen = data.Length;

            var offsets = new List<int>();

            for (int i = 0; i < entries; i++)
            {
                int start = i * 4;
                if (start + 4 > dataLen) break;

                int offset = msbt.order == ">" ?
                    (data[start] << 24) | (data[start + 1] << 16) | (data[start + 2] << 8) | data[start + 3] :
                    (data[start + 3] << 24) | (data[start + 2] << 16) | (data[start + 1] << 8) | data[start];
                offsets.Add(offset - 4);
            }

            var strings = new List<List<string>>();

            for (int i = 0; i < offsets.Count; i++)
            {
                int start = offsets[i];
                int end = i < offsets.Count - 1 ? offsets[i + 1] : dataLen;

                if (start >= end || start < 0 || start >= dataLen)
                {
                    strings.Add(new List<string>());
                    continue;
                }

                var substrings = new List<string>();
                var currentString = new List<byte>();
                int position = start;

                while (position + 1 < end && position + 1 < dataLen)
                {
                    byte b1 = data[position];
                    byte b2 = data[position + 1];

                    if (b1 == 0 && b2 == 0)
                    {
                        if (currentString.Count > 0)
                        {
                            string str = msbt.order == ">" ?
                                Encoding.BigEndianUnicode.GetString(currentString.ToArray()) :
                                Encoding.Unicode.GetString(currentString.ToArray());
                            substrings.Add(str);
                            currentString.Clear();
                        }
                    }
                    else
                    {
                        currentString.Add(b1);
                        currentString.Add(b2);
                    }
                    position += 2;
                }

                if (currentString.Count > 0)
                {
                    string str = msbt.order == ">" ?
                        Encoding.BigEndianUnicode.GetString(currentString.ToArray()) :
                        Encoding.Unicode.GetString(currentString.ToArray());
                    substrings.Add(str);
                }

                strings.Add(substrings);
            }

            section.data = strings;
        }
    }
}