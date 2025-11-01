using System.ComponentModel;
using System.Text;

namespace super_toolbox
{
    public partial class SuperToolbox : Form
    {
        private int totalFileCount;
        private Dictionary<string, TreeNode> formatNodes = new Dictionary<string, TreeNode>();
        private Dictionary<string, TreeNode> categoryNodes = new Dictionary<string, TreeNode>();
        private readonly List<string>[] messageBuffers = new List<string>[2];
        private readonly object[] bufferLocks = { new object(), new object() };
        private int activeBufferIndex;
        private bool isUpdatingUI;
        private System.Windows.Forms.Timer updateTimer;
        private CancellationTokenSource extractionCancellationTokenSource;
        private const int UpdateInterval = 200;
        private const int MaxMessagesPerUpdate = 1000;
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
            { "索尼 - gxt提取器","图片" },
            { "直接绘制表面 - DDS", "图片" },
            { "超女神信仰诺瓦露 - pck","其他档案" },
            { "超女神信仰诺瓦露 - tex","图片" },
            { "SEGS binary data - bin","其他档案" }, //代表作：苍翼默示录_刻之幻影
            { "FPAC archives - pac","其他档案" },
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
            { "任天堂 - 3DS/WIIU sound", "音频" },
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
            { "Yaz0 - yaz0_compress","压缩" },
            { "Yaz0 - yaz0_decompress","解压" },
            { "Zlib - zlib_compress","压缩" },
            { "Zlib - zlib_decompress","解压" },
            { "Gzip - gzip_compress","压缩" },
            { "Gzip - gzip_decompress","解压" },
            { "Lz4 - lz4_compress","压缩" },
            { "Lz4 - lz4_decompress","解压" },
            { "Lz4c - lz4c_Compress","压缩" },
            { "Lz4c - lz4c_Decompress","解压" },
            { "LZ77 - lz77_compress","压缩" },
            { "LZ77 - lz77_decompress","解压" },
            { "LZMA - 7-zip_lzma_compress","压缩" },
            { "LZMA - 7-zip_lzma_decompress","解压" },
            { "LZSS - lzss_compress","压缩" },
            { "LZSS - lzss_decompress","解压" },
            { "ZSTD - zstd_compress","压缩" },
            { "ZSTD - zstd_decompress", "解压" },
            { "Brotli - brotli_compress","压缩" },
            { "Brotli - brotli_decompress","解压" },
            { "Oodle - oodle_compress","压缩" },
            { "Oodle - oodle_decompress","解压" },
            { "Huffman - huffman_compress","压缩" },
            { "Huffman - huffman_decompress","解压" },
            { "Minlz - minlz_compress","压缩" },
            { "Minlz - minlz_decompress","解压" },
            { "Lzham - lzham自定义压缩","压缩" },
            { "Lzham - lzham自定义解压","解压" },
            { "Lzham - Lzham标准压缩","压缩" },
            { "Lzham - Lzham标准解压","解压" },
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
            { "Filename - PS3DALpck1","其他档案" },
            { "Filename - PS3DALpck2","其他档案" },
            { "传颂之物二人的白皇 - sdat","其他档案" },
            { "CRI - afs打包器","其他档案" },
            { "Mages - mpk提取器","其他档案" },
            { "Mages - mpk打包器","其他档案" },
            { "PS4 - gnf转换器","图片" },
            { "wav2qoa - 转换qoa", "音频" },
            {"CMVS_Engine - cmv","其他档案" },
            { "SRPG_Studio - dts","其他档案" },
            { "XACT Wave Bank - xwb打包器","其他档案" },
            { "PNG编码ASTC","图片" },
            { "ASTC解码PNG","图片" }
        };

        public SuperToolbox()
        {
            InitializeComponent();

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

        private async void btnExtract_Click(object sender, EventArgs e)
        {
            if (isExtracting)
            {
                EnqueueMessage("正在进行提取操作，请等待...");
                return;
            }

            string dirPath = txtFolderPath.Text;
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            {
                EnqueueMessage($"错误: {dirPath} 不是一个有效的目录。");
                return;
            }

            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Tag as string == "category")
            {
                EnqueueMessage("请选择一个具体的文件格式。");
                return;
            }

            string formatName = selectedNode.Text;
            totalFileCount = 0;
            isExtracting = true;
            UpdateUIState(true);

            try
            {
                var extractor = CreateExtractor(formatName);
                if (extractor == null)
                {
                    EnqueueMessage($"错误: 不支持的格式 {formatName}");
                    isExtracting = false;
                    UpdateUIState(false);
                    return;
                }

                EnqueueMessage($"开始提取 {formatName} 格式的文件...");

                SubscribeToExtractorEvents(extractor);

                await Task.Run(async () =>
                {
                    try
                    {
                        await extractor.ExtractAsync(dirPath, extractionCancellationTokenSource.Token);
                        this.Invoke(new Action(() =>
                        {
                            UpdateFileCountDisplay();
                            EnqueueMessage($"提取操作完成，总共提取了 {totalFileCount} 个文件");
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        this.Invoke(new Action(() =>
                        {
                            EnqueueMessage("提取操作已取消");
                        }));
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(new Action(() =>
                        {
                            EnqueueMessage($"提取过程中出现错误: {ex.Message}");
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
                EnqueueMessage($"提取过程中出现错误: {ex.Message}");
                isExtracting = false;
                UpdateUIState(false);
            }
        }
        private void SubscribeToExtractorEvents(BaseExtractor extractor)
        {

            extractor.FileExtracted += (s, fileName) =>
            {
                Interlocked.Increment(ref totalFileCount);
                EnqueueMessage($"已提取: {Path.GetFileName(fileName)}");
                UpdateFileCountDisplay();
            };

            extractor.ProgressUpdated += (s, progress) =>
            {
            };

            extractor.ExtractionCompleted += (s, count) =>
            {
                EnqueueMessage($"提取完成，共提取 {count} 个文件");
            };

            extractor.ExtractionFailed += (s, error) =>
            {
                EnqueueMessage($"提取失败: {error}");
            };

            var type = extractor.GetType();

            var extractionStartedEvent = type.GetEvent("ExtractionStarted");
            if (extractionStartedEvent != null)
            {
                extractionStartedEvent.AddEventHandler(extractor, new EventHandler<string>((s, message) =>
                {
                    EnqueueMessage(message);
                }));
            }

            var extractionProgressEvent = type.GetEvent("ExtractionProgress");
            if (extractionProgressEvent != null)
            {
                extractionProgressEvent.AddEventHandler(extractor, new EventHandler<string>((s, message) =>
                {
                    EnqueueMessage(message);
                }));
            }

            var extractionErrorEvent = type.GetEvent("ExtractionError");
            if (extractionErrorEvent != null)
            {
                extractionErrorEvent.AddEventHandler(extractor, new EventHandler<string>((s, message) =>
                {
                    EnqueueMessage($"错误: {message}");
                }));
            }

            var extractionMessageEvent = type.GetEvent("ExtractionMessage");
            if (extractionMessageEvent != null)
            {
                extractionMessageEvent.AddEventHandler(extractor, new EventHandler<string>((s, message) =>
                {
                    EnqueueMessage(message);
                }));
            }

            var filesExtractedEvent = type.GetEvent("FilesExtracted");
            if (filesExtractedEvent != null)
            {
                filesExtractedEvent.AddEventHandler(extractor, new EventHandler<List<string>>((s, fileNames) =>
                {
                    foreach (var fileName in fileNames)
                    {
                        Interlocked.Increment(ref totalFileCount);
                        EnqueueMessage($"已提取: {Path.GetFileName(fileName)}");
                    }
                    UpdateFileCountDisplay();
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
                case "第七史诗 - sct": return new SctExtractor();
                case "万代南梦宫 - bnsf": return new Bnsf_Extractor();
                case "索尼 - gxt提取器": return new SonyGxtExtractor();
                case "直接绘制表面 - DDS": return new DdsExtractor();
                case "超女神信仰诺瓦露 - pck": return new StingPckExtractor();
                case "超女神信仰诺瓦露 - tex": return new StingTexExtractor();
                case "SEGS binary data - bin": return new SEGS_BinExtractor();
                case "FPAC archives - pac": return new FPAC_Extractor();
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
                case "任天堂 - 3DS/WIIU sound": return new Wiiu3dsSound_Extractor();
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
                case "Yaz0 - yaz0_compress": return new Yaz0_Compressor();
                case "Yaz0 - yaz0_decompress": return new Yaz0_Decompressor();
                case "Zlib - zlib_compress": return new Zlib_Compressor();
                case "Zlib - zlib_decompress": return new Zlib_Decompressor();
                case "Gzip - gzip_compress": return new Gzip_Compressor();
                case "Gzip - gzip_decompress": return new Gzip_Decompressor();
                case "Lz4 - lz4_compress": return new Lz4_Compressor();
                case "Lz4 - lz4_decompress": return new Lz4_Decompressor();
                case "Lz4c - lz4c_Compress": return new Lz4c_Compressor();
                case "Lz4c - lz4c_Decompress": return new Lz4c_Decompressor();
                case "LZ77 - lz77_compress": return new Lz77_Compressor();
                case "LZ77 - lz77_decompress": return new Lz77_Decompressor();
                case "LZMA - 7-zip_lzma_compress": return new Lzma_Compressor();
                case "LZMA - 7-zip_lzma_decompress": return new Lzma_Decompressor();
                case "LZSS - lzss_compress": return new Lzss_Compressor();
                case "LZSS - lzss_decompress": return new Lzss_Decompressor();
                case "ZSTD - zstd_compress": return new Zstd_Compressor();
                case "ZSTD - zstd_decompress": return new Zstd_Decompressor();
                case "Brotli - brotli_compress": return new Brotli_Compressor();
                case "Brotli - brotli_decompress": return new Brotli_Decompressor();
                case "Oodle - oodle_compress": return new Oodle_Compressor();
                case "Oodle - oodle_decompress": return new Oodle_Decompressor();
                case "Huffman - huffman_compress": return new Huffman_Compressor();
                case "Huffman - huffman_decompress": return new Huffman_Decompressor();
                case "Minlz - minlz_compress": return new Minlz_Compressor();
                case "Minlz - minlz_decompress": return new Minlz_Decompressor();
                case "Lzham - lzham自定义压缩": return new LzhamCustom_Compressor();               
                case "Lzham - lzham自定义解压": return new lzhamCustom_Decompressor();
                case "Lzham - Lzham标准压缩": return new LzhamStandard_Compressor();
                case "Lzham - Lzham标准解压": return new LzhamStandard_Decompressor();
                case "Wiiu - gtx转换器": return new Wiiu_gtxConvertor();
                case "Wiiu - h3/app": return new Wiiu_h3appExtractor();
                case "Nds - nds提取器": return new Nds_Extractor();
                case "Nds - nds打包器": return new Nds_Repacker();
                case "3ds - darc提取器": return new Darc_Extractor();
                case "3ds - darc打包器": return new Darc_Repacker();
                case "Nds - narc提取器": return new NarcExtractor();
                case "PS3 - psarc提取器": return new PsarcExtractor();
                case "PS3 - psarc打包器": return new PsarcRepacker();
                case "PS3 - NPDRM - sdat": return new NPD_Extractor();
                case "Filename - PS3DALpck1": return new FilenameExtractor();
                case "Filename - PS3DALpck2": return new Filename2Extractor();
                case "传颂之物二人的白皇 - sdat": return new SdatExtractor();
                case "CRI - afs打包器": return new AfsRepacker();
                case "Mages - mpk提取器": return new MagesMpkExtractor();
                case "Mages - mpk打包器": return new MagesMpkRepacker();
                case "PS4 - gnf转换器": return new GNF2PNG_Converter();
                case "wav2qoa - 转换qoa": return new Wav2Qoa_Converter();
                case "CMVS_Engine - cmv": return new CmvDecoder();
                case "SRPG_Studio - dts": return new DtsExtractor();
                case "XACT Wave Bank - xwb打包器": return new XWBPacker();
                case "PNG编码ASTC": return new Png2Astc_Converter();
                case "ASTC解码PNG": return new Astc2Png_Converter();
                default: throw new NotSupportedException($"不支持的格式: {formatName}");
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            lock (bufferLocks[0]) { messageBuffers[0].Clear(); }
            lock (bufferLocks[1]) { messageBuffers[1].Clear(); richTextBox1.Clear(); }
            totalFileCount = 0;
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
            if (statusStrip1 == null || lblFileCount == null) return;
            try
            {
                richTextBox1.SuspendLayout();
                StringBuilder sb = new StringBuilder();
                foreach (string message in messages) sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
                int scrollPosition = richTextBox1.SelectionStart;
                bool isAtBottom = scrollPosition >= richTextBox1.TextLength - 10;
                richTextBox1.AppendText(sb.ToString());
                if (isAtBottom) richTextBox1.ScrollToCaret();
                else { richTextBox1.SelectionStart = scrollPosition; richTextBox1.SelectionLength = 0; }
            }
            catch { }
            finally { richTextBox1.ResumeLayout(); isUpdatingUI = false; }
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
            EnqueueMessage($"已将 {selectedNode.Text} 移动到 {category} 分组");
        }

        private void UpdateUIState(bool isExtracting)
        {
            btnExtract.Enabled = !isExtracting;
            btnSelectFolder.Enabled = !isExtracting;
            treeView1.Enabled = !isExtracting;
            if (lblStatus != null) lblStatus.Text = isExtracting ? "正在提取..." : "就绪";
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
            if (lblFileCount != null) lblFileCount.Text = $"已提取: {totalFileCount} 个文件";
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
                    MessageBox.Show("分组名称不能为空！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (categoryNodes.ContainsKey(categoryName))
                {
                    MessageBox.Show($"分组 '{categoryName}' 已存在！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                TreeNode newCategory = AddCategory(categoryName);
                treeView1.SelectedNode = newCategory;
                treeView1.ExpandAll();
                EnqueueMessage($"已添加新分组: {categoryName}");
            }
        }

        private void renameCategoryMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Tag as string != "category")
            {
                MessageBox.Show("请选择一个分组进行编辑！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (defaultCategories.Values.Contains(selectedNode.Text))
            {
                MessageBox.Show("不能编辑默认分组！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string newName = ShowInputDialog("编辑分组", "请输入新的分组名称:", selectedNode.Text);
            if (!string.IsNullOrEmpty(newName))
            {
                if (string.IsNullOrEmpty(newName.Trim()))
                {
                    MessageBox.Show("分组名称不能为空！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (categoryNodes.ContainsKey(newName))
                {
                    MessageBox.Show($"分组 '{newName}' 已存在！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                string oldName = selectedNode.Text;
                categoryNodes.Remove(oldName);
                selectedNode.Text = newName;
                categoryNodes[newName] = selectedNode;
                EnqueueMessage($"已将分组 '{oldName}' 重命名为: {newName}");
            }
        }

        private void deleteCategoryMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Tag as string != "category")
            {
                MessageBox.Show("请选择一个分组进行删除！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (selectedNode.Nodes.Count > 0)
            {
                MessageBox.Show("无法删除非空分组，请先将其中的提取器移至其他分组！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (defaultCategories.Values.Contains(selectedNode.Text))
            {
                MessageBox.Show("不能删除默认分组！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (MessageBox.Show($"确定要删除分组 '{selectedNode.Text}' 吗？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                string categoryName = selectedNode.Text;
                selectedNode.Remove();
                categoryNodes.Remove(categoryName);
                EnqueueMessage($"已删除分组: {categoryName}");
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
        }
    }
}
