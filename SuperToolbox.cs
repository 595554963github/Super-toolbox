using System.ComponentModel;
using System.Text;

namespace super_toolbox
{
    public partial class SuperToolbox : Form
    {
        private int totalFileCount;
        private int totalConvertedCount;
        private Dictionary<string, TreeNode> formatNodes = new Dictionary<string, TreeNode>();
        private Dictionary<string, TreeNode> categoryNodes = new Dictionary<string, TreeNode>();
        private readonly List<string>[] messageBuffers = new List<string>[2];
        private readonly object[] bufferLocks = { new object(), new object() };
        private int activeBufferIndex;
        private bool isUpdatingUI;
        private System.Windows.Forms.Timer updateTimer;
        private CancellationTokenSource extractionCancellationTokenSource;
        private const int UpdateInterval = 100;
        private const int MaxMessagesPerUpdate = 20;
        private bool isExtracting;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel lblStatus;
        private ToolStripStatusLabel lblFileCount;
        private readonly Dictionary<string, string> defaultCategories = new Dictionary<string, string>
        {
            { "RIFF - wave系列[需要ffmpeg]", "音频" },
            { "RIFF - Fmod - bank", "音频" },
            { "RIFF - wmav2 - xwma", "音频" },
            { "RIFX - BigEndian - wem", "音频" },
            { "RIFF - cdxa - xa", "音频" },
            { "CRI - adpcm_adx - adx", "音频" },
            { "CRI - adpcm_adx - ahx", "音频" },
            { "Fmod - fsb5", "音频" },
            { "Xiph.Org - Ogg", "音频" },
            { "CRI - HCA - hca", "音频" },
            { "任天堂 - libopus - lopus", "音频" },
            { "光荣特库摩 - kvs/ktss", "音频" },
            { "RIFF - Google - webp", "图片" },
            { "联合图像专家组 - JPEG/JPG", "图片" },
            { "便携式网络图形 - PNG", "图片" },
            { "索尼 - gxt转换器", "图片" },
            { "ENDILTLE - APK - apk", "其他档案" },
            { "东方天空竞技场 - GPK - gpk", "其他档案" },
            { "GxArchivedFile - dat", "其他档案" },
            { "苍之彼方的四重奏EXTRA2 - dat", "其他档案" },
            { "Lightvn galgame engine - mcdat/vndat", "其他档案" },
            { "CRI - afs archives - afs", "其他档案" },
            { "CRI - package - cpk", "其他档案" },
            { "IdeaFactory - tid","图片" },
            { "第七史诗 - sct","图片" },
            { "万代南梦宫 - bnsf","音频" },//代表作：情热传说，英文名<Tales of Zestiria>
            { "索尼 - gxt提取器","其他档案" },
            { "直接绘制表面 - DDS", "图片" },
            { "超女神信仰诺瓦露 - pck","其他档案" },
            { "超女神信仰诺瓦露 - tex","图片" },
            { "SEGS binary data - bin","其他档案" }, //代表作：苍翼默示录_刻之幻影
            { "苍翼默示录_刻之幻影 - pac","其他档案" },
            { "苍翼默示录_神观之梦 - pac","其他档案" },
            { "断罪的玛利亚 - dat", "其他档案" },
            { "进击的巨人_自由之翼 - bin", "其他档案" },
            { "PlayStation 4 bit ADPCM - vag", "音频" },
            { "零：濡鸦之巫女 - fmsg", "其他档案" },
            { "零：濡鸦之巫女 - kscl", "图片" },
            { "PhyreEngine Texture - phyre", "图片" },
            { "PhyreEngine package - pkg", "其他档案" },
            { "女神异闻录5对决：幽灵先锋 - bin", "其他档案" },
            { "MPEG-4 - mp4", "其他档案" },
            { "IdeaFactory - bra","其他档案"},
            { "任天堂 - 3DS/WII/WIIU sound", "音频" },
            { "Binary Audio Archive - baa","其他档案" },
            { "Audio Archive - aw","音频" },
            { "反恐精英OL - pak","其他档案" },
            { "IdeaFactory - pac提取器","其他档案" },
            { "IdeaFactory - pac打包器","其他档案" },
            { "光荣特库摩 - gz/exlilr", "其他档案" },
            { "光荣特库摩 - ebm", "其他档案" },
            { "光荣特库摩 - g1t", "图片" },
            { "光荣特库摩 - gmpk", "其他档案" },
            { "光荣特库摩 - pak", "其他档案" },
            { "PowerVR转换png","图片" },
            { "逆战 - upk","其他档案" },
            { "战争传说 - pak","其他档案" },
            { "IdeaFactory - cl3","其他档案" },
            { "5pb - LNK4 archives - dat","其他档案" },
            { "万代南梦宫 - 情热传说 - dat","其他档案" },
            { "Brotli - brotli_compress","压缩" },
            { "Brotli - brotli_decompress","解压" },
            { "Gzip - gzip_compress","压缩" },
            { "Gzip - gzip_decompress","解压" },
            { "Huffman - huffman_compress","压缩" },
            { "Huffman - huffman_decompress","解压" },
            { "Lz4 - lz4_compress","压缩" },
            { "Lz4 - lz4_decompress","解压" },
            { "Lz4c - lz4c_compress","压缩" },
            { "Lz4c - lz4c_decompress","解压" },
            { "LZ77 - lz77_compress","压缩" },
            { "LZ77 - lz77_decompress","解压" },
            { "LZMA - 7-zip_lzma_compress","压缩" },
            { "LZMA - 7-zip_lzma_decompress","解压" },
            { "LZSS - lzss自定义压缩","压缩" },
            { "LZSS - lzss自定义解压","解压" },
            { "Lzham - lzham自定义压缩","压缩" },
            { "Lzham - lzham自定义解压","解压" },
            { "Lzham - Lzham标准压缩","压缩" },
            { "Lzham - Lzham标准解压","解压" },           
            { "Minlz - minlz_compress","压缩" },
            { "Minlz - minlz_decompress","解压" },
            { "Mio0 - mio0_compress","压缩" },
            { "Mio0 - mio0_decompress", "解压" },
            { "Oodle - oodle_compress","压缩" },
            { "Oodle - oodle_decompress","解压" },
            { "Wflz - wflz_compress", "压缩" },
            { "Wflz - wflz_decompress", "解压" },
            { "Yay0 - yay0_compress","压缩" },
            { "Yay0 - yay0_decompress","解压" },
            { "Yaz0 - yaz0_compress","压缩" },
            { "Yaz0 - yaz0_decompress","解压" },
            { "Zlib - zlib_compress","压缩" },
            { "Zlib - zlib_decompress","解压" },
            { "ZSTD - zstd_compress","压缩" },
            { "ZSTD - zstd_decompress", "解压" },
            { "Wiiu - gtx转换器","图片" },
            { "Wiiu - h3/app","其他档案" },
            { "Nds - nds提取器","其他档案" },
            { "Nds - nds打包器","其他档案" },
            { "3ds - darc提取器","其他档案" },
            { "3ds - darc打包器","其他档案" },
            { "Nds - narc提取器","其他档案" },
            { "PS3 - psarc提取器","其他档案" },
            { "PS3 - psarc打包器","其他档案" },
            { "PS3 - NPDRM - sdat","其他档案" },
            { "Filename - PS3DALpck","其他档案" },
            { "CRI - afs打包器","其他档案" },
            { "Mages - mpk提取器","其他档案" },
            { "Mages - mpk打包器","其他档案" },
            { "Gnf2Png","图片" },
            { "wav2qoa - 转换qoa", "音频" },
            {"CMVS_Engine - cmv","其他档案" },
            { "SRPG_Studio - dts","其他档案" },
            { "XACT Wave Bank - xwb打包器","其他档案" },
            { "PNG编码ASTC","图片" },
            { "ASTC解码PNG","图片" },
            { "hip2png","图片" },
            { "双截龙彩虹pak提取器","其他档案" },
            { "CFSI - cfsi提取器", "其他档案" },
            { "CFSI - cfsi打包器", "其他档案" },
            { "消逝的光芒 - rpack","其他档案" },
            { "传颂之物二人的白皇 - sdat","其他档案" },
            { "PlayStation MultiStream File - msf","音频" },
        };
        public SuperToolbox()
        {
            InitializeComponent();
            txtFolderPath.AllowDrop = true;
            txtFolderPath.DragEnter += TxtFolderPath_DragEnter;
            txtFolderPath.DragDrop += TxtFolderPath_DragDrop;
            txtFolderPath.DragLeave += TxtFolderPath_DragLeave;
            statusStrip1 = new StatusStrip();
            lblStatus = new ToolStripStatusLabel { Text = "就绪" };
            lblFileCount = new ToolStripStatusLabel { Text = "已提取:0个文件" };
            statusStrip1.Items.Add(lblStatus);
            statusStrip1.Items.Add(lblFileCount);
            statusStrip1.Dock = DockStyle.Bottom;
            this.Controls.Add(statusStrip1);
            InitializeTreeView();
            messageBuffers[0] = new List<string>(MaxMessagesPerUpdate);
            messageBuffers[1] = new List<string>(MaxMessagesPerUpdate);
            updateTimer = new System.Windows.Forms.Timer { Interval = UpdateInterval };
            updateTimer.Tick += UpdateUITimerTick;
            updateTimer.Start();
            extractionCancellationTokenSource = new CancellationTokenSource();
        }
        private void InitializeTreeView()
        {
            foreach (string category in defaultCategories.Values.Distinct())
            {
                AddCategory(category);
            }
            foreach (var item in defaultCategories)
            {
                string extractorName = item.Key;
                string categoryName = item.Value;
                TreeNode categoryNode = categoryNodes[categoryName];
                TreeNode extractorNode = categoryNode.Nodes.Add(extractorName);
                formatNodes[extractorName] = extractorNode;
                extractorNode.Tag = extractorName;
            }
            treeView1.ExpandAll();
        }
        private TreeNode AddCategory(string categoryName)
        {
            if (categoryNodes.ContainsKey(categoryName)) return categoryNodes[categoryName];
            TreeNode categoryNode = treeView1.Nodes.Add(categoryName);
            categoryNode.Tag = "category";
            categoryNodes[categoryName] = categoryNode;
            return categoryNode;
        }
        private void btnSelectFolder_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                folderBrowserDialog.Description = "选择要提取的文件夹";
                folderBrowserDialog.ShowNewFolderButton = false;
                string inputPath = txtFolderPath.Text;
                if (!string.IsNullOrEmpty(inputPath) && Directory.Exists(inputPath))
                {
                    folderBrowserDialog.SelectedPath = inputPath;
                }
                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {
                    txtFolderPath.Text = folderBrowserDialog.SelectedPath;
                    EnqueueMessage($"已选择文件夹: {folderBrowserDialog.SelectedPath}");
                }
            }
        }
        private readonly HashSet<string> _converters = new HashSet<string>
        {
         "PNG编码ASTC", "ASTC解码PNG", "Gnf2Png", "PowerVR转换png",
         "第七史诗 - sct", "索尼 - gxt转换器", "超女神信仰诺瓦露 - tex",
         "wav2qoa - 转换qoa", "Wiiu - gtx转换器", "hip2png"
        };
        private bool IsConverter(string formatName) => _converters.Contains(formatName);
        private async void btnExtract_Click(object sender, EventArgs e)
        {
            if (isExtracting)
            {
                EnqueueMessage("正在进行操作，请等待...");
                return;
            }
            string dirPath = txtFolderPath.Text;
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            {
                EnqueueMessage($"错误:{dirPath}不是一个有效的目录。");
                return;
            }
            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Tag as string == "category")
            {
                EnqueueMessage("请选择你的操作");
                return;
            }
            string formatName = selectedNode.Text;
            bool isConverter = IsConverter(formatName);
            bool isPacker = formatName.EndsWith("打包器") ||
                           formatName.Contains("_pack") ||
                           formatName.Contains("_repack") ||
                           formatName.Contains("packer") ||
                           formatName.Contains("repacker");
            bool isCompressor = formatName.EndsWith("_compress") ||
                               formatName.Contains("压缩") ||
                               formatName.Contains("Compressor");
            bool isDecompressor = formatName.EndsWith("_decompress") ||
                                 formatName.Contains("解压") ||
                                 formatName.Contains("Decompressor");
            if (isConverter)
            {
                totalConvertedCount = 0;
            }
            else
            {
                totalFileCount = 0;
            }
            isExtracting = true;
            UpdateUIState(true);
            try
            {
                var extractor = CreateExtractor(formatName);
                if (extractor == null)
                {
                    EnqueueMessage($"错误:不支持{formatName}");
                    isExtracting = false;
                    UpdateUIState(false);
                    return;
                }
                string operationType = "提取";
                if (isConverter) operationType = "转换";
                else if (isPacker) operationType = "打包";
                else if (isCompressor) operationType = "压缩";
                else if (isDecompressor) operationType = "解压";
                EnqueueMessage($"开始{operationType}{formatName}...");
                SubscribeToExtractorEvents(extractor);
                await Task.Run(async () =>
                {
                    try
                    {
                        await extractor.ExtractAsync(dirPath, extractionCancellationTokenSource.Token);
                        this.Invoke(new Action(() =>
                        {
                            UpdateFileCountDisplay();
                            int count = isConverter ? totalConvertedCount : totalFileCount;
                            EnqueueMessage($"{operationType}操作完成，总共{operationType}了{count}个文件");
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        this.Invoke(new Action(() =>
                        {
                            EnqueueMessage($"{operationType}操作已取消");
                        }));
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(new Action(() =>
                        {
                            EnqueueMessage($"{operationType}过程中出现错误: {ex.Message}");
                        }));
                    }
                    finally
                    {
                        this.Invoke(new Action(() =>
                        {
                            isExtracting = false;
                            UpdateUIState(false);
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                EnqueueMessage($"操作初始化失败: {ex.Message}");
                isExtracting = false;
                UpdateUIState(false);
            }
        }
        private void SubscribeToExtractorEvents(BaseExtractor extractor)
        {
            TreeNode selectedNode = treeView1.SelectedNode;
            string selectedText = selectedNode?.Text ?? "";
            bool isConverter = IsConverter(selectedText);
            bool isPacker = selectedText.EndsWith("打包器") || selectedText.Contains("_repack");
            bool isCompressor = selectedText.EndsWith("_compress") || selectedText.Contains("压缩");
            bool isDecompressor = selectedText.EndsWith("_decompress") || selectedText.Contains("解压");
            if (isConverter)
            {
                extractor.FileConverted += (s, fileName) =>
                {
                    Interlocked.Increment(ref totalConvertedCount);
                    EnqueueMessage($"已转换:{Path.GetFileName(fileName)}");
                    UpdateFileCountDisplay();
                };
                extractor.ConversionCompleted += (s, count) =>
                {
                    EnqueueMessage($"转换完成，共转换{count}个文件");
                };
                extractor.ConversionFailed += (s, error) =>
                {
                    EnqueueMessage($"转换失败:{error}");
                };
            }
            else if (isPacker)
            {
                extractor.FilePacked += (s, fileName) =>
                {
                    Interlocked.Increment(ref totalFileCount);
                    EnqueueMessage($"已打包:{Path.GetFileName(fileName)}");
                    UpdateFileCountDisplay();
                };
                extractor.PackingCompleted += (s, count) =>
                {
                    EnqueueMessage($"打包完成，共打包{count}个文件");
                };
                extractor.PackingFailed += (s, error) =>
                {
                    EnqueueMessage($"打包失败: {error}");
                };
            }
            else if (isCompressor)
            {
                extractor.FileCompressed += (s, fileName) =>
                {
                    Interlocked.Increment(ref totalFileCount);
                    EnqueueMessage($"已压缩:{Path.GetFileName(fileName)}");
                    UpdateFileCountDisplay();
                };
                extractor.CompressionCompleted += (s, count) =>
                {
                    EnqueueMessage($"压缩完成，共压缩{count}个文件");
                };

                extractor.CompressionFailed += (s, error) =>
                {
                    EnqueueMessage($"压缩失败: {error}");
                };
            }
            else if (isDecompressor)
            {
                extractor.FileDecompressed += (s, fileName) =>
                {
                    Interlocked.Increment(ref totalFileCount);
                    EnqueueMessage($"已解压:{Path.GetFileName(fileName)}");
                    UpdateFileCountDisplay();
                };
                extractor.DecompressionCompleted += (s, count) =>
                {
                    EnqueueMessage($"解压完成，共解压{count}个文件");
                };
                extractor.DecompressionFailed += (s, error) =>
                {
                    EnqueueMessage($"解压失败: {error}");
                };
            }
            else
            {
                extractor.FileExtracted += (s, fileName) =>
                {
                    Interlocked.Increment(ref totalFileCount);
                    EnqueueMessage($"已提取:{Path.GetFileName(fileName)}");
                    UpdateFileCountDisplay();
                };
                extractor.ExtractionCompleted += (s, count) =>
                {
                    EnqueueMessage($"提取完成，共处理{count}个源文件");
                };
                extractor.ExtractionFailed += (s, error) =>
                {
                    EnqueueMessage($"提取失败: {error}");
                };
            }
            extractor.ProgressUpdated += (s, progress) => { };
            var type = extractor.GetType();
            BindDynamicEvent(type, extractor, "ExtractionStarted", "提取开始");
            BindDynamicEvent(type, extractor, "ExtractionProgress", "提取进度");
            BindDynamicEvent(type, extractor, "ConversionStarted", "转换开始");
            BindDynamicEvent(type, extractor, "ConversionProgress", "转换进度");
            BindDynamicEvent(type, extractor, "PackingStarted", "打包开始");
            BindDynamicEvent(type, extractor, "PackingProgress", "打包进度");
            BindDynamicEvent(type, extractor, "CompressionStarted", "压缩开始");
            BindDynamicEvent(type, extractor, "CompressionProgress", "压缩进度");
            BindDynamicEvent(type, extractor, "DecompressionStarted", "解压开始");
            BindDynamicEvent(type, extractor, "DecompressionProgress", "解压进度");
            BindDynamicEvent(type, extractor, "ExtractionError", "错误", true);
            BindDynamicEvent(type, extractor, "ConversionError", "错误", true);
            BindDynamicEvent(type, extractor, "PackingError", "错误", true);
            BindDynamicEvent(type, extractor, "CompressionError", "错误", true);
            BindDynamicEvent(type, extractor, "DecompressionError", "错误", true);
        }
        private void BindDynamicEvent(Type type, BaseExtractor extractor, string eventName, string prefix, bool isError = false)
        {
            var eventInfo = type.GetEvent(eventName);
            if (eventInfo != null)
            {
                eventInfo.AddEventHandler(extractor, new EventHandler<string>((s, message) =>
                {
                    string formattedMessage = isError ? $"错误: {message}" : $"{prefix}: {message}";
                    EnqueueMessage(formattedMessage);
                }));
            }
        }
        private BaseExtractor CreateExtractor(string formatName)
        {
            switch (formatName)
            {
                case "RIFF - wave系列[需要ffmpeg]": return new WaveExtractor();
                case "RIFF - Fmod - bank": return new BankExtractor();
                case "RIFF - Google - webp": return new WebpExtractor();
                case "RIFF - wmav2 - xwma": return new XwmaExtractor();
                case "RIFX - BigEndian - wem": return new RifxExtractor();
                case "RIFF - cdxa - xa": return new CdxaExtractor();
                case "CRI - adpcm_adx - adx": return new AdxExtractor();
                case "CRI - adpcm_adx - ahx": return new AhxExtractor();
                case "Fmod - fsb5": return new Fsb5Extractor();
                case "任天堂 - libopus - lopus": return new LopusExtractor();
                case "光荣特库摩 - kvs/ktss": return new Kvs_Kns_Extractor();
                case "Xiph.Org - Ogg": return new OggExtractor();
                case "联合图像专家组 - JPEG/JPG": return new JpgExtractor();
                case "便携式网络图形 - PNG": return new PngExtractor();
                case "CRI - HCA - hca": return new HcaExtractor();
                case "ENDILTLE - APK - apk": return new ApkExtractor();
                case "东方天空竞技场 - GPK - gpk": return new GpkExtractor();
                case "GxArchivedFile - dat": return new GDAT_Extractor();
                case "苍之彼方的四重奏EXTRA2 - dat": return new Aokana2Extractor();
                case "Lightvn galgame engine - mcdat/vndat": return new LightvnExtractor();
                case "CRI - afs archives - afs": return new AfsExtractor();
                case "CRI - package - cpk": return new CpkExtractor();
                case "IdeaFactory - tid": return new TidExtractor();
                case "第七史诗 - sct": return new Sct2Png_Converter();
                case "万代南梦宫 - bnsf": return new Bnsf_Extractor();
                case "索尼 - gxt提取器": return new SonyGxtExtractor();
                case "直接绘制表面 - DDS": return new DdsExtractor();
                case "超女神信仰诺瓦露 - pck": return new StingPckExtractor();
                case "超女神信仰诺瓦露 - tex": return new StingTexConverter();
                case "SEGS binary data - bin": return new SEGS_BinExtractor();
                case "苍翼默示录_刻之幻影 - pac": return new FPAC_CP_Extractor();
                case "苍翼默示录_神观之梦 - pac": return new FPAC_CF_Extractor();
                case "PlayStation 4 bit ADPCM - vag": return new VagExtractor();
                case "断罪的玛利亚 - dat": return new DataDatExtractor();
                case "进击的巨人_自由之翼 - bin": return new Attack_on_Titan_Wings_Extractor();
                case "索尼 - gxt转换器": return new SonyGxtConverter();
                case "零：濡鸦之巫女 - fmsg": return new FMSG_Extractor();
                case "零：濡鸦之巫女 - kscl": return new KSCL_Extractor();
                case "PhyreEngine Texture - phyre": return new PhyreTexture_Extractor();
                case "PhyreEngine package - pkg": return new PhyrePKG_Extractor();
                case "女神异闻录5对决：幽灵先锋 - bin": return new P5S_WMV_Extractor();
                case "MPEG-4 - mp4": return new MP4_Extractor();
                case "IdeaFactory - bra": return new BraExtractor();
                case "任天堂 - 3DS/WII/WIIU sound": return new NintendoSound_Extractor();
                case "Binary Audio Archive - baa": return new BaaExtractor();
                case "Audio Archive - aw": return new AwExtractor();
                case "反恐精英OL - pak": return new CSO_PakExtractor();
                case "IdeaFactory - pac提取器": return new IdeaFactory_PacExtractor();
                case "IdeaFactory - pac打包器": return new IdeaFactory_PacRepacker();
                case "光荣特库摩 - gz/exlilr": return new GustElixir_Extractor();
                case "光荣特库摩 - ebm": return new GustEbm_Extractor();
                case "光荣特库摩 - g1t": return new GustG1t_Extractor();
                case "光荣特库摩 - gmpk": return new GustGmpk_Extractor();
                case "光荣特库摩 - pak": return new GustPak_Extractor();
                case "PowerVR转换png": return new PVR2PNG_Converter();
                case "逆战 - upk": return new AFUpkExtractor();
                case "战争传说 - pak": return new WarTales_PakExtractor();
                case "IdeaFactory - cl3": return new IdeaFactory_CL3Extractor();
                case "5pb - LNK4 archives - dat": return new LNK4Extractor();
                case "万代南梦宫 - 情热传说 - dat": return new TalesDat_Extractor();
                case "Brotli - brotli_compress": return new Brotli_Compressor();
                case "Brotli - brotli_decompress": return new Brotli_Decompressor();
                case "Gzip - gzip_compress": return new Gzip_Compressor();
                case "Gzip - gzip_decompress": return new Gzip_Decompressor();
                case "Huffman - huffman_compress": return new Huffman_Compressor();
                case "Huffman - huffman_decompress": return new Huffman_Decompressor();
                case "Lz4 - lz4_compress": return new Lz4_Compressor();
                case "Lz4 - lz4_decompress": return new Lz4_Decompressor();
                case "Lz4c - lz4c_compress": return new Lz4c_Compressor();
                case "Lz4c - lz4c_decompress": return new Lz4c_Decompressor();
                case "LZ77 - lz77_compress": return new Lz77_Compressor();
                case "LZ77 - lz77_decompress": return new Lz77_Decompressor();
                case "LZMA - 7-zip_lzma_compress": return new Lzma_Compressor();
                case "LZMA - 7-zip_lzma_decompress": return new Lzma_Decompressor();
                case "LZSS - lzss自定义压缩": return new LzssCustom_Compressor();
                case "LZSS - lzss自定义解压": return new LzssCustom_Decompressor();
                case "Lzham - lzham自定义压缩": return new LzhamCustom_Compressor();
                case "Lzham - lzham自定义解压": return new lzhamCustom_Decompressor();
                case "Lzham - Lzham标准压缩": return new LzhamStandard_Compressor();
                case "Lzham - Lzham标准解压": return new LzhamStandard_Decompressor();
                case "Minlz - minlz_compress": return new Minlz_Compressor();
                case "Minlz - minlz_decompress": return new Minlz_Decompressor();
                case "Mio0 - mio0_compress": return new Mio0_Compressor();
                case "Mio0 - mio0_decompress": return new Mio0_Decompressor();
                case "Oodle - oodle_compress": return new Oodle_Compressor();
                case "Oodle - oodle_decompress": return new Oodle_Decompressor();
                case "Wflz - wflz_compress": return new Wflz_Compressor();
                case "Wflz - wflz_decompress": return new Wflz_Decompressor();
                case "Yay0 - yay0_compress":return new Yay0_Compressor();
                case "Yay0 - yay0_decompress": return new Yay0_Decompressor();
                case "Yaz0 - yaz0_compress": return new Yaz0_Compressor();
                case "Yaz0 - yaz0_decompress": return new Yaz0_Decompressor();
                case "Zlib - zlib_compress": return new Zlib_Compressor();
                case "Zlib - zlib_decompress": return new Zlib_Decompressor();
                case "ZSTD - zstd_compress": return new Zstd_Compressor();
                case "ZSTD - zstd_decompress": return new Zstd_Decompressor();
                case "Wiiu - gtx转换器": return new Wiiu_gtxConverter();
                case "Wiiu - h3/app": return new Wiiu_h3appExtractor();
                case "Nds - nds提取器": return new Nds_Extractor();
                case "Nds - nds打包器": return new Nds_Repacker();
                case "3ds - darc提取器": return new Darc_Extractor();
                case "3ds - darc打包器": return new Darc_Repacker();
                case "Nds - narc提取器": return new NarcExtractor();
                case "PS3 - psarc提取器": return new PsarcExtractor();
                case "PS3 - psarc打包器": return new PsarcRepacker();
                case "PS3 - NPDRM - sdat": return new NPD_Extractor();
                case "Filename - PS3DALpck": return new FilenameExtractor();
                case "CRI - afs打包器": return new AfsRepacker();
                case "Mages - mpk提取器": return new MagesMpkExtractor();
                case "Mages - mpk打包器": return new MagesMpkRepacker();
                case "Gnf2Png": return new GNF2PNG_Converter();
                case "wav2qoa - 转换qoa": return new Wav2Qoa_Converter();
                case "CMVS_Engine - cmv": return new CmvDecoder();
                case "SRPG_Studio - dts": return new DtsExtractor();
                case "XACT Wave Bank - xwb打包器": return new XWBPacker();
                case "PNG编码ASTC": return new Png2Astc_Converter();
                case "ASTC解码PNG": return new Astc2Png_Converter();
                case "hip2png": return new Hip2Png_Converter();
                case "双截龙彩虹pak提取器": return new DoubleDragonNeon_PakExtractor();
                case "CFSI - cfsi提取器": return new Cfsi_Extractor();
                case "CFSI - cfsi打包器": return new Cfsi_Repacker();
                case "消逝的光芒 - rpack": return new DyingLightExtractor();
                case "传颂之物二人的白皇 - sdat": return new Sdat_Extractor();
                case "PlayStation MultiStream File - msf": return new Msf_Extractor();
                default: throw new NotSupportedException($"不支持的格式: {formatName}");
            }
        }
        private void btnClear_Click(object sender, EventArgs e)
        {
            lock (bufferLocks[0]) { messageBuffers[0].Clear(); }
            lock (bufferLocks[1]) { messageBuffers[1].Clear(); richTextBox1.Clear(); }
            totalFileCount = 0;
            totalConvertedCount = 0;
            UpdateFileCountDisplay();
        }
        private void EnqueueMessage(string message)
        {
            int bufferIndex = activeBufferIndex;
            lock (bufferLocks[bufferIndex])
            {
                if (messageBuffers[bufferIndex].Count >= MaxMessagesPerUpdate && !isUpdatingUI)
                {
                    activeBufferIndex = (activeBufferIndex + 1) % 2;
                    bufferIndex = activeBufferIndex;
                }
                messageBuffers[bufferIndex].Add(message);
            }
        }
        private void UpdateUITimerTick(object? sender, EventArgs e)
        {
            if (isUpdatingUI) return;
            int inactiveBufferIndex = (activeBufferIndex + 1) % 2;
            object bufferLock = bufferLocks[inactiveBufferIndex];
            List<string>? messagesToUpdate = null;
            lock (bufferLock)
            {
                if (messageBuffers[inactiveBufferIndex].Count > 0)
                {
                    isUpdatingUI = true;
                    messagesToUpdate = new List<string>(messageBuffers[inactiveBufferIndex]);
                    messageBuffers[inactiveBufferIndex].Clear();
                }
            }
            if (messagesToUpdate != null && messagesToUpdate.Count > 0) UpdateRichTextBox(messagesToUpdate);
            else isUpdatingUI = false;
        }
        private void UpdateRichTextBox(List<string> messages)
        {
            if (richTextBox1.IsDisposed || richTextBox1.Disposing) { isUpdatingUI = false; return; }
            if (richTextBox1.InvokeRequired)
            {
                try { richTextBox1.Invoke(new Action(() => UpdateRichTextBoxInternal(messages))); }
                catch { isUpdatingUI = false; return; }
            }
            else UpdateRichTextBoxInternal(messages);
        }
        private void UpdateRichTextBoxInternal(List<string> messages)
        {
            if (statusStrip1 == null || lblFileCount == null || richTextBox1.IsDisposed)
            {
                isUpdatingUI = false;
                return;
            }

            try
            {
                richTextBox1.SuspendLayout();

                int currentSelectionStart = richTextBox1.SelectionStart;
                int currentSelectionLength = richTextBox1.SelectionLength;

                bool isAtBottom = IsRichTextBoxAtBottom();

                StringBuilder sb = new StringBuilder();
                foreach (string message in messages)
                {
                    sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
                }

                richTextBox1.AppendText(sb.ToString());

                if (isAtBottom)
                {
                    richTextBox1.ScrollToCaret();
                }
                else
                {
                    richTextBox1.SelectionStart = currentSelectionStart;
                    richTextBox1.SelectionLength = currentSelectionLength;
                    richTextBox1.ScrollToCaret();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateRichTextBoxInternal错误:{ex.Message}");
            }
            finally
            {
                richTextBox1.ResumeLayout();
                isUpdatingUI = false;
            }
        }

        private bool IsRichTextBoxAtBottom()
        {
            int firstVisibleCharIndex = richTextBox1.GetCharIndexFromPosition(new Point(0, 0));
            int lastVisibleCharIndex = richTextBox1.GetCharIndexFromPosition(new Point(0, richTextBox1.ClientSize.Height));

            return lastVisibleCharIndex >= richTextBox1.TextLength - 50;
        }
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node != null && lblStatus != null)
            {
                lblStatus.Text = e.Node.Tag as string == "category"
                    ? $"已选择: {e.Node.Text} (分组)"
                    : $"已选择: {e.Node.Text}";
            }
        }
        private void treeViewContextMenu_Opening(object sender, CancelEventArgs e)
        {
            if (treeView1.SelectedNode == null)
            {
                e.Cancel = false;
                moveToCategoryMenuItem.Visible = false;
                renameCategoryMenuItem.Visible = false;
                deleteCategoryMenuItem.Visible = false;
                addNewCategoryMenuItem.Visible = true;
                return;
            }
            bool isCategory = treeView1.SelectedNode.Tag as string == "category";
            moveToCategoryMenuItem.Visible = !isCategory;
            renameCategoryMenuItem.Visible = isCategory;
            deleteCategoryMenuItem.Visible = isCategory && treeView1.SelectedNode.Nodes.Count == 0 &&
                !defaultCategories.Values.Contains(treeView1.SelectedNode.Text);
            addNewCategoryMenuItem.Visible = true;
            moveToCategoryMenuItem.DropDownItems.Clear();
            if (!isCategory)
            {
                foreach (string category in categoryNodes.Keys)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(category);
                    item.Click += (s, args) => MoveSelectedNodeToCategory(category);
                    moveToCategoryMenuItem.DropDownItems.Add(item);
                }
            }
        }
        private void MoveSelectedNodeToCategory(string category)
        {
            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Parent == null || selectedNode.Tag as string == "category") return;
            TreeNode? targetCategory = categoryNodes.ContainsKey(category) ? categoryNodes[category] : null;
            if (targetCategory == null || selectedNode.Parent == targetCategory) return;
            selectedNode.Remove();
            targetCategory.Nodes.Add(selectedNode);
            treeView1.SelectedNode = selectedNode;
            EnqueueMessage($"已将{selectedNode.Text}移动到{category}分组");
        }
        private void UpdateUIState(bool isExtracting)
        {
            btnExtract.Enabled = !isExtracting;
            treeView1.Enabled = !isExtracting;
            if (lblStatus != null) lblStatus.Text = isExtracting ? "正在处理..." : "就绪";
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                updateTimer?.Stop();
                updateTimer?.Dispose();
                extractionCancellationTokenSource?.Cancel();
                extractionCancellationTokenSource?.Dispose();
            }
            catch { }
            base.OnFormClosing(e);
        }
        private void UpdateFileCountDisplay()
        {
            if (lblFileCount != null)
            {
                TreeNode selectedNode = treeView1.SelectedNode;
                if (selectedNode != null)
                {
                    string selectedText = selectedNode.Text;
                    bool isConverter = IsConverter(selectedText);
                    bool isPacker = selectedText.EndsWith("打包器") ||
                                   selectedText.Contains("_pack") ||
                                   selectedText.Contains("_repack") ||
                                   selectedText.Contains("packer") ||
                                   selectedText.Contains("repacker");
                    bool isCompressor = selectedText.EndsWith("_compress") ||
                                       selectedText.Contains("压缩") ||
                                       selectedText.Contains("Compressor");
                    bool isDecompressor = selectedText.EndsWith("_decompress") ||
                                         selectedText.Contains("解压") ||
                                         selectedText.Contains("Decompressor");
                    if (isConverter)
                    {
                        lblFileCount.Text = $"已转换:{totalConvertedCount}个文件";
                    }
                    else if (isPacker)
                    {
                        lblFileCount.Text = $"已打包:{totalFileCount}个文件";
                    }
                    else if (isCompressor)
                    {
                        lblFileCount.Text = $"已压缩:{totalFileCount}个文件";
                    }
                    else if (isDecompressor)
                    {
                        lblFileCount.Text = $"已解压:{totalFileCount}个文件";
                    }
                    else
                    {
                        lblFileCount.Text = $"已提取:{totalFileCount}个文件";
                    }
                }
                else
                {
                    lblFileCount.Text = $"已提取:{totalFileCount}个文件";
                }
            }
        }
        private string ShowInputDialog(string title, string prompt, string initialValue = "")
        {
            string result = string.Empty;

            Form dialog = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(330, 130)
            };
            Label label = new Label
            {
                Text = prompt,
                Location = new Point(20, 20),
                AutoSize = true
            };
            TextBox textBox = new TextBox
            {
                Text = initialValue,
                Location = new Point(20, 45),
                Size = new Size(285, 23),
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            Button okButton = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                Location = new Point(140, 80),
                Size = new Size(75, 23)
            };
            Button cancelButton = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(230, 80),
                Size = new Size(75, 23)
            };
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;
            dialog.Controls.AddRange(new Control[] { label, textBox, okButton, cancelButton });
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                result = textBox.Text ?? string.Empty;
            }
            return result;
        }
        private void addNewCategoryMenuItem_Click(object sender, EventArgs e)
        {
            string categoryName = ShowInputDialog("添加新分组", "请输入分组名称:");
            if (!string.IsNullOrEmpty(categoryName))
            {
                if (string.IsNullOrEmpty(categoryName.Trim()))
                {
                    MessageBox.Show("分组名称不能为空!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (categoryNodes.ContainsKey(categoryName))
                {
                    MessageBox.Show($"分组'{categoryName}'已存在!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                TreeNode newCategory = AddCategory(categoryName);
                treeView1.SelectedNode = newCategory;
                treeView1.ExpandAll();
                EnqueueMessage($"已添加新分组:{categoryName}");
            }
        }
        private void renameCategoryMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Tag as string != "category")
            {
                MessageBox.Show("请选择一个分组进行编辑!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (defaultCategories.Values.Contains(selectedNode.Text))
            {
                MessageBox.Show("不能编辑默认分组!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string newName = ShowInputDialog("编辑分组", "请输入新的分组名称:", selectedNode.Text);
            if (!string.IsNullOrEmpty(newName))
            {
                if (string.IsNullOrEmpty(newName.Trim()))
                {
                    MessageBox.Show("分组名称不能为空!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (categoryNodes.ContainsKey(newName))
                {
                    MessageBox.Show($"分组 '{newName}' 已存在!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                string oldName = selectedNode.Text;
                categoryNodes.Remove(oldName);
                selectedNode.Text = newName;
                categoryNodes[newName] = selectedNode;
                EnqueueMessage($"已将分组'{oldName}'重命名为:{newName}");
            }
        }
        private void deleteCategoryMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Tag as string != "category")
            {
                MessageBox.Show("请选择一个分组进行删除!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (selectedNode.Nodes.Count > 0)
            {
                MessageBox.Show("无法删除非空分组，请先将其中的提取器移至其他分组!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (defaultCategories.Values.Contains(selectedNode.Text))
            {
                MessageBox.Show("不能删除默认分组!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (MessageBox.Show($"确定要删除分组'{selectedNode.Text}'吗？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                string categoryName = selectedNode.Text;
                selectedNode.Remove();
                categoryNodes.Remove(categoryName);
                EnqueueMessage($"已删除分组:{categoryName}");
            }
        }
        private void TxtFolderPath_DragEnter(object? sender, DragEventArgs e)
        {
            if (txtFolderPath == null) return;

            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];

                if (files != null && files.Length == 1 && Directory.Exists(files[0]))
                {
                    e.Effect = DragDropEffects.Copy;
                    txtFolderPath.BackColor = Color.Green;
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void TxtFolderPath_DragDrop(object? sender, DragEventArgs e)
        {
            if (txtFolderPath == null) return;

            txtFolderPath.BackColor = SystemColors.Window;

            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];

                if (files != null && files.Length == 1 && Directory.Exists(files[0]))
                {
                    txtFolderPath.Text = files[0];
                    EnqueueMessage($"已通过拖放选择文件夹:{files[0]}");
                }
                else
                {
                    EnqueueMessage("错误:请拖放单个文件夹");
                }
            }
        }

        private void TxtFolderPath_DragLeave(object? sender, EventArgs e)
        {
            if (txtFolderPath == null) return;

            txtFolderPath.BackColor = SystemColors.Window;
        }
        private void btnHelp_Click(object sender, EventArgs e)
        {
            HelpGuideForm helpForm = new HelpGuideForm();
            helpForm.ShowDialog(this);
        }
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
        }
    }
}
