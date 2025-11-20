namespace super_toolbox
{
    public partial class HelpGuideForm : Form
    {
        private System.ComponentModel.IContainer? components = null;

        public HelpGuideForm()
        {
            InitializeComponent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }
        private int lastHighlightedLine = -1;
        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.Text = "超级工具箱使用帮助";
            this.Size = new Size(1200, 720);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;

            RichTextBox rtbGuide = new RichTextBox();
            rtbGuide.Dock = DockStyle.Fill;
            rtbGuide.ReadOnly = true;
            rtbGuide.BorderStyle = BorderStyle.None;
            rtbGuide.Margin = new Padding(10);
            rtbGuide.BackColor = Color.White;

            string guideText = "超级工具箱使用帮助\r\n\r\n基本操作流程：\r\n1. 选择文件夹 - 点击\"选择文件夹\"按钮或者拖放文件夹到路径输入框\r\n" +
                "2. 选择操作 - 在左侧树形菜单中选择要执行的具体操作\r\n3. 开始执行 - 点击\"开始\"按钮执行选定的操作\r\n\r\n" +"功能分类说明：\r\n\r\n" +
                "📁 音频处理\r\n  - 支持多种音频格式的提取和转换，包括WAV、OGG、HCA、ADX等格式，部分功能需要ffmpeg支持\r\n" +
                "🖼️ 图片处理\r\n  - 支持多种图片格式的提取和转换，包括PNG、JPEG、DDS、GXT等格式\r\n📦 档案处理\r\n  - 支持多种游戏档案格式的提取和打包，" +
                "包括AFS、PAC、DAT等格式\r\n🗜️ 压缩解压\r\n  - 支持多种压缩算法的压缩和解压，包括Brotli、Gzip、LZ4、LZMA等\r\n" +
                "平台支持：\r\n  - PC游戏文件、主机游戏文件（PS3、PS4、WiiU、3DS等），移动端游戏文件\r\n\r\n注意事项：\r\n处理前请备份重要文件，确保有足够的磁盘空间，" +
                "部分功能需要额外的依赖，比如ffmpeg\r\n\r\n日志功能：\r\n- 实时显示操作进度和结果，可通过清空日志按钮清除记录，日志包含时间戳便于追踪\r\n\r\n" +
                "分组管理：\r\n- 右键点击树形菜单可管理分组，可以添加、编辑、删除自定义分组。可以将提取器移动到不同的分组\r\n\r\n" +
                "以下是具体功能的详细说明\r\n" +
                "--------------------------------------------------------------------------------------------------------------------------------------------------\r\n" +
                "<提取器>\r\n"+
                "AdxExtractor——以二进制形式从cpk、afs、awb、acb甚至uasset、uexp等文件里面提取adx文件\r\n" +
                "AfsExtractor——用于解包criware的afs格式的档案，常用于PlayStation和xbox360平台\r\n"+
                "AFUpkExtractor——逆战upk提取器，一个个手动解包太麻烦，批量解包是最好的选择\r\n" +
                "AhxExtractor——以二进制形式从cpk、afs等文件里面提取ahx文件\r\n" +
                "Aokana2Extractor——苍之彼方的四重奏2 Extra的专用提取器 \r\n" +
                "ApkExtractor——解包ENDILTLE文件头的apk文件，代表游戏有Artdink开发的龙珠Z：终极之战、万代南梦宫的刀剑神域失落之歌和加速世界vs刀剑神域千年的黄昏和科乐美开发的伪恋出嫁\r\n" +
                "Attack_on_Titan_Wings_Extractor——进击的巨人_自由之翼提取器，可从LINKDATA.bin文件里面提取出g1t、g1m等文件\r\n" +
                "AwExtractor——一种未知的音频档案格式，如果没有baa索引文件，那么aw文件将没有任何意义，只因为aw文件不包含任何头部信息、文件大小或其他标识数据，代表作有wiiu平台的塞尔达传说黄昏公主HD\r\n" +
                "BaaExtractor——baa文件提取器，用于从baa文件里面提取出wsys文件，没有这个文件无法解包aw文件，并且要把wsys文件放到aw文件夹才可提取出wav，在我的努力下vgmstream已经支持解码aw/baa\r\n" +
                "BankExtractor——从游戏文件里面提取Fmod的bank音频包文件，如国产手游白荆回廊、云裳羽衣\r\n" +
                "Bnsf_Extractor——万代南梦宫的bnsf音频提取器，以二进制形式从TLFILE.TLDAT文件里面提取，可解情热传说和ps3平台的狂战传说，狂战传说steam版加密了无法提取\r\n" +
                "BraExtractor——地雷社游戏常见的bra文件提取器，适用于地雷社妖精剑士F和Falcome的东京幻都EX\r\n" +
                "CdxaExtractor——从PlayStation游戏的XA文件里面提取xa音频文件，注意区分大小写，XA是打包了一堆xa音频的包文件\r\n" +
                "Cfsi_Extractor——cfsi文件专用提取器，适用于极限脱出3：零时困境和Re：从零开始的异世界生活虚假的王选候补\r\n" +
                "CmvDecoder——CMVS引擎的cmv视频解码器，代表作：天津罪\r\n" +
                "CpkExtractor——用于解包CRIWARE的cpk格式的档案，很多平台常见的打包格式，这玩意啥也存，只有你不知道的，没有他不存的\r\n" +
                "CSO_PakExtractor——反恐精英ol的pak提取器，csol的打包格式分nar和pak两种，本提取器是用来解pak格式的\r\n" +
                "Darc_Extractor——任天堂3ds平台的darc解包工具，代表作：美妙旋律七彩演唱会闪耀设计\r\n" +
                "DataDatExtractor——ps3游戏断罪的玛利亚专用提取器，可解包data.dat文件，提取出里面的视频、音频和图片\r\n" +
                "DdsExtractor——以二进制形式从游戏文件里面提取dds图片的提取器，亲测战场女武神4，一般的dds和DX10纹理的dds都可以提取出来\r\n" +
                "DoubleDragonNeon_PakExtractor——双截龙彩虹的专用pak提取器\r\n" +
                "DtsExtractor——SRPG Studio的dts提取器，代表作：刻印战记2：七圣英雄\r\n" +
                "DyingLightExtractor——消逝的光芒rpack专用提取器\r\n" +
                "FilenameExtractor——ps3约会大作战的pck文件提取器\r\n" +
                "FMSG_Extractor——零濡鸦之巫女的fmsg转换器，可以把fmsg转换成txt文本\r\n" +
                "FPAC_CF_Extractor——苍翼默示录：神观之梦的专用解包工具，可以解包压缩的pac文件和普通pac文件\r\n" +
                "FPAC_CP_Extractor——苍翼默示录：刻之幻影的专用解包工具，无法用于苍翼默示录：神观之梦，从bin文件里提取出的pac文件可以用此提取器提取音频和图片\r\n" +
                "Fsb5Extractor——首先可以从Fmod的bank里面提取fsb文件，也可以从steam游戏永恒轮回和女神异闻录5对决：幽灵先锋的resources.resource提取出fsb，对于正当防卫4的arc文件有专用方法提取fsb\r\n" +
                "GDAT_Extractor——Mizuchi engine开发的游戏档案专用dat解包器，广泛用于地雷社游戏，如死亡终局轮回试炼系列、神狱塔断罪玛丽最终篇、新次元游戏海王星Ⅶr,大家也可以使用我的python脚本去解\r\n" +
                "GpkExtractor——东方天空竞技场：幻想乡空战姬的专用解包器\r\n" +
                "GustEbm_Extractor——光荣特库摩的ebm文件提取器，适用于蓝色反射：帝、幻舞少女之剑、无夜之国\r\n" +
                "GustElixir_Extractor——光荣特库摩的gz和elixir文件提取器，适用于蓝色反射：帝、幻舞少女之剑、无夜之国\r\n" +
                "GustG1t_Extractor——光荣特库摩的g1t文件提取器，可以提取出dds图片，如fate系列、无夜之国2\r\n" +
                "GustGmpk_Extractor——光荣特库摩的gmpk文件提取器，适用于零系列的濡鸦之巫女等游戏\r\n" +
                "GustPak_Extractor ——光荣特库摩的pak文件提取器，适用于蓝色反射：帝、幻舞少女之剑、苏菲的炼金工房2\r\n" +
                "HcaExtractor——以二进制形式从cpk、awb、acb或者uexp等文件里面提取打包的hca音频文件，如果是加密的hca文件提取出来会自带enc后缀名，比如enc.hca\r\n" +
                "IdeaFactory_CL3Extractor——地雷社游戏的CL3提取器，可从CL3文件里提取dat、tid等文件，代表作妖精剑士F,主要因为stcm-editor.exe这个工具太反人类，实在受不了\r\n" +
                "IdeaFactory_PacExtractor——地雷社游戏的pac提取器，pac常用于海王星系列游戏\r\n" +
                "JpgExtractor——以二进制形式从游戏文件里面提取jpg图片，jpg是标准的计算机文件，有固定文件头和文件尾\r\n" +
                "KSCL_Extractor——零濡鸦之巫女的kscl转换器，可以把kscl转换成dds图片\r\n" +
                "Kvs_Kns_Extractor——提取光荣特库摩游戏专用音频提取器，主要从switch平台提取kns和steam平台的kvs格式，也支持ps4平台的at3格式\r\n" +
                "LightvnExtractor——Light.vn galgame引擎的提取器，可解宝石少女1st的mcdat和爱你，我的英雄！的vndat\r\n" +
                "Lnk4Extractor——5pb的LNK4文件头的dat解包工具，可解xbox360游戏11只眼：交错的视线\r\n" +
                "LopusExtractor——任天堂switch平台的音频提取器，代表作：月姬重制版(Switch)，文件头为OPUS，实际上是libopus编码的音频文件，和正常的opus不一样，叫lopus格式，使用此提取器可以从文件中提取出lopus\r\n" +
                "MagesMpkExtractor——Mages的mpk解包工具，代表作：命运石之门\r\n" +
                "MP4_Extractor——有些游戏会把mp4视频打包到一起，由于mp4文件头前四字节不固定，但后4字节是固定的，这个提取器可以轻易的把mp4视频提取出来,支持合法mp4\r\n" +
                "Narc_Extractor——任天堂nds平台的narc文件提取器，代表作：口袋妖怪(nds)\r\n" +
                "Nds_Extractor——任天堂nds平台的rom解包工具\r\n" +
                "NintendoSound_Extractor——任天堂wii、wiiu和3ds平台的音频提取器，从brsar、bfsar和bcsar文件里面提取出br/bf/bc前缀的wav波形音频文件\r\n" +
                "NPD_Extractor——索尼ps3平台的sdat解包工具，代表作：约会大作战或守安装、约会大作战凛弥理想乡、第二次超级机器人大战OG\r\n" +
                "OggExtractor——从各种游戏里面提取ogg格式的音频文件\r\n" +
                "P5S_WMV_Extractor——女神异闻录5对决：幽灵先锋的专用wmv提取器，可以从movie文件夹的bin文件里面提取上百个wmv视频\r\n" +
                "PhyrePKG_Extractor——phyre引擎的pkg文件提取器，适用于东京幻都EX、闪之轨迹、创之轨迹\r\n" +
                "PhyreTexture_Extractor——从phyre引擎的phyre文件里提取dds的提取器，适用于刀剑神域虚空断章、彼岸游境、东京幻都EX、闪之轨迹、创之轨迹\r\n" +
                "PngExtractor——以二进制形式从游戏文件里面提取png图片的提取器，png是标准的计算机文件，有固定文件头和文件尾\r\n" +
                "PsarcExtractor——索尼ps3平台的psarc解包工具，代表作：第二次超级机器人大战OG，也可以解包无人深空的pak文件\r\n" +
                "RifxExtractor——以二进制形式从xbox360游戏里面提取大端序的wem文件，RIFX和RIFF都是资源交换文件格式\r\n" +
                "SEGS_BinExtractor——苍翼默示录：刻之幻影专用bin文件解包工具，可提取出里面的所有pac文件\r\n" +
                "SonyGxtExtractor——从索尼的psv、psp等平台游戏里面提取gxt文件，很多游戏喜欢把一堆gxt文件打包\r\n" +
                "StingPckExtractor——用于解包steam和switch平台的超女神信仰诺瓦露、psv平台的约会大作战凛绪轮回、steam平台的约会大作战凛绪轮回HD\r\n" +
                "TalesDat_Extractor——万代南梦宫的情热传说TLDAT解包工具，也可以解狂战传说的，但只能解PS3平台的，不能解steam平台的，因为steam平台加密了\r\n" +
                "TidExtractor——地雷社海王星系列pac文件经常使用这种tid文件，使用此提取器可以把tid转换到dds，并且支持BC7纹理的tid\r\n" +
                "VagExtractor——以二进制形式从PlayStation游戏文件里面提取vag音频文件，vag是PlayStation 4 bit ADPCM编码的音频格式\r\n" +
                "WarTales_PakExtractor——战争传说的pak解包工具，原解包工具PAKTool太垃圾，老是出现非法字符报错，找了个bms脚本改成c#语言了，可以彻底解包该游戏所有pak文件\r\n" +
                "WaveExtractor——RIFF资源交换文件格式，相同文件头和数据块的有wav、wwise的wem、索尼的at3和at9以及微软的xma，不过注意一件事，需要在电脑上安装ffmpeg并配置环境变量，否则无法返回正确的音频格式\r\n" +
                "WebpExtractor——RIFF资源交换文件格式，用于提取谷歌开发的webp图片\r\n" +
                "Wiiu_h3appExtractor——任天堂wiiu平台的rom解包器，可以把wiiu平台的h3、app文件转换成loadiine格式\r\n" +
                "XwmaExtractor——RIFF资源交换文件格式，用于提取微软开发的xwma音频，比较罕见的音频格式，留着吧，有备无患\r\n" +
                "--------------------------------------------------------------------------------------------------------------------------------------------------\r\n" +
                "<转换器>\r\n" +
                "Astc2Png_Converter——astc图像到png的转换器，可批量转换成png\r\n" +
                "GNF2PNG_Converter——PS4平台的gnf到png的转换器，受不了GFDstudio一个个手动转换，用它可批量转换成png\r\n" +
                "Hip2Png_Converter——hip到png的转换器，可批量转换成png，代表作如switch平台的赛马娘\r\n" +
                "Png2Astc_Converter——png到astc图像的转换器，可批量转换成astc\r\n" +
                "PVR2PNG_Converter——逆战upk解包的pvr纹理使用Texturepacker可以转换成png，但是只能免费试用7天，使用此转换器可以无限使用\r\n" +
                "Sct2Png_Converter——第七史诗的sct转换器，可批量转换成png\r\n" +
                "SonyGxtConverter——PlayStation游戏的gxt到png的转换器，和SonyGxtExtractor不同，那个是从已打包的文件里面提取出gxt，这个是转换器，可批量把gxt转换成png\r\n" +
                "StingTexConverter——用于解包steam和switch平台的超女神信仰诺瓦露、steam平台的约会大作战凛绪轮回HD还有传颂之物二人的白皇\r\n" +
                "Wav2Qoa_Converter——wav到qoa的音频转换器，ffmpeg可以识别qoa，可以使用foobar2000播放\r\n" +
                "Wiiu_gtxConverter——任天堂wiiu平台的gtx转换器，可以把gtx转换成png图片\r\n" +
                "--------------------------------------------------------------------------------------------------------------------------------------------------\r\n" +
                "<打包器>\r\n" +
                "AfsPacker——CRIware的afs档案打包器，可以把一个文件夹及子文件夹里的所有文件重新打包成afs文件\r\n" +
                "Cfsi_Repacker——cfsi打包器，可以把一个文件夹及子文件夹里的所有文件重新打包成cfsi文件\r\n" +
                "Darc_Repacker——任天堂3ds平台的darc打包器，可以把一个文件夹里的文件打包成darc文件\r\n" +
                "IdeaFactory_PacRepacker——地雷社游戏的pac打包器，可以把文件夹重新打包成pac文件\r\n" +
                "MagesMpkRepacker——Mages的mpk打包器，可以把一个文件夹及子文件夹里的所有文件重新打包成mpk文件\r\n" +
                "Nds_Repacker——任天堂nds平台的rom打包器，可以将解包的nds文件夹重新打包成nds\r\n" +
                "PsarcRepacker——索尼ps3平台的psarc打包器，可以把一个文件夹重新打包成psarc文件\r\n" +
                "XWBPacker——XWB打包器，可以把一个文件夹里的所有wav打包成xwb文件，不过不是支持所有wav格式，建议使用立体声pcm_s16le的wav文件\r\n" +
                "--------------------------------------------------------------------------------------------------------------------------------------------------\r\n" +
                "<压缩器和解压器>\r\n" +
                "Brotli_Compressor——使用Brotli算法批量压缩文件，压缩速度很慢，如果无法删除文件夹请在任务管理器退出brotli.exe\r\n" +
                "Brotli_Decompressor——使用Brotli算法批量解压文件，解压很快\r\n" +
                "Gzip_Compressor——使用Gzip算法批量压缩文件\r\n" +
                "Gzip_Decompressor——使用Gzip算法批量解压文件\r\n" +
                "Huffman_Compressor——使用Huffman算法批量压缩文件\r\n" +
                "Huffman_Decompressor——使用Huffman算法批量解压文件\r\n" +
                "LZ4_Compressor——使用LZ4算法批量压缩文件，使用C#第三方库\r\n" +
                "LZ4_Decompressor——使用LZ4算法批量解压文件，使用C#第三方库\r\n" +
                "Lz4c_Compressor——使用LZ4c算法批量压缩文件，使用外部可执行程序\r\n" +
                "Lz4c_Decompressor——使用LZ4c算法批量解压文件，使用外部可执行程序\r\n" +
                "Lz77_Compressor——使用LZ77算法批量压缩文件\r\n" +
                "Lz77_Decompressor——使用LZ77算法批量解压文件\r\n" +
                "LzhamCustom_Compressor——使用自定义Lzham算法批量压缩文件\r\n" +
                "LzhamCustom_Decompressor——使用自定义Lzham算法批量解压文件\r\n" +
                "LzhamStandard_Compressor——使用标准Lzham算法批量压缩文件\r\n" +
                "LzhamStandard_Decompressor——使用标准Lzham算法批量解压文件\r\n" +
                "Lzma_Compressor——使用LZMA算法批量压缩文件\r\n" +
                "Lzma_Decompressor——使用LZMA算法批量解压文件\r\n" +
                "LzssCustom_Compressor——使用自定义Lzss算法批量压缩文件\r\n" +
                "LzssCustom_Decompressor——使用自定义Lzss算法批量解压文件\r\n" +
                "Minlz_Compressor——使用Minlz算法批量压缩文件\r\n" +
                "Minlz_Decompressor——使用Minlz算法批量解压文件\r\n" +
                "Mio0_Compressor——使用Mio0算法批量压缩文件\r\n" +
                "Mio0_Decompressor——使用Mio0算法批量解压文件\r\n" +
                "Oodle_Compressor——使用Oodle算法批量压缩文件\r\n" +
                "Oodle_Decompressor——使用Oodle算法批量解压文件\r\n" +
                "Wflz_Compressor——使用Wflz算法批量压缩文件\r\n" +
                "Wflz_Decompressor——使用Wflz算法批量解压文件\r\n" +
                "Yay0_Compressor——使用Yay0算法批量压缩文件\r\n" +
                "Yay0_Decompressor——使用Yay0算法批量解压文件\r\n" +
                "Yaz0_Compressor——使用Yaz0算法批量压缩文件\r\n" +
                "Yaz0_Decompressor——使用Yaz0算法批量解压文件\r\n" +
                "Zlib_Compressor——使用Zlib算法批量压缩文件\r\n" +
                "Zlib_Decompressor——使用Zlib算法批量解压文件\r\n" +
                "Zstd_Compressor——使用Zstd算法批量解压文件\r\n" +
                "Zstd_Decompressor——使用Zstd算法批量解压文件\r\n"
                ; 

            rtbGuide.Text = guideText;
            rtbGuide.Select(0, 0);
            rtbGuide.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    int charIndex = rtbGuide.GetCharIndexFromPosition(e.Location);
                    int lineIndex = rtbGuide.GetLineFromCharIndex(charIndex);
                    int lineStart = rtbGuide.GetFirstCharIndexFromLine(lineIndex);
                    int lineEnd = rtbGuide.GetFirstCharIndexFromLine(lineIndex + 1);
                    if (lineEnd < 0) lineEnd = rtbGuide.TextLength;

                    if (lineIndex == lastHighlightedLine)
                    {
                        rtbGuide.Select(lineStart, lineEnd - lineStart);
                        rtbGuide.SelectionBackColor = rtbGuide.BackColor;
                        rtbGuide.SelectionColor = rtbGuide.ForeColor;
                        lastHighlightedLine = -1;
                    }
                    else
                    {
                        if (lastHighlightedLine != -1)
                        {
                            int lastLineStart = rtbGuide.GetFirstCharIndexFromLine(lastHighlightedLine);
                            int lastLineEnd = rtbGuide.GetFirstCharIndexFromLine(lastHighlightedLine + 1);
                            if (lastLineEnd < 0) lastLineEnd = rtbGuide.TextLength;

                            rtbGuide.Select(lastLineStart, lastLineEnd - lastLineStart);
                            rtbGuide.SelectionBackColor = rtbGuide.BackColor;
                            rtbGuide.SelectionColor = rtbGuide.ForeColor;
                        }

                        rtbGuide.Select(lineStart, lineEnd - lineStart);
                        rtbGuide.SelectionBackColor = Color.Green;
                        rtbGuide.SelectionColor = Color.Purple;
                        lastHighlightedLine = lineIndex;
                    }

                    rtbGuide.Select(charIndex, 0);
                }
            };

            rtbGuide.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
                    e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    e.Handled = true;
                }
            };
            rtbGuide.LostFocus += (s, e) =>
            {
                if (lastHighlightedLine != -1)
                {
                    int lastLineStart = rtbGuide.GetFirstCharIndexFromLine(lastHighlightedLine);
                    int lastLineEnd = rtbGuide.GetFirstCharIndexFromLine(lastHighlightedLine + 1);
                    if (lastLineEnd < 0) lastLineEnd = rtbGuide.TextLength;

                    rtbGuide.Select(lastLineStart, lastLineEnd - lastLineStart);
                    rtbGuide.SelectionBackColor = rtbGuide.BackColor;
                    rtbGuide.SelectionColor = rtbGuide.ForeColor;
                    lastHighlightedLine = -1;
                }
                rtbGuide.Select(0, 0);
            };
            Button btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Size = new Size(80, 30);
            btnClose.Location = new Point((600 - 80) / 2, 430);
            btnClose.Anchor = AnchorStyles.Bottom;
            btnClose.Click += (s, e) => this.Close();

            this.Controls.Add(rtbGuide);
            this.Controls.Add(btnClose);

            this.ResumeLayout(false);
        }
    }
}
