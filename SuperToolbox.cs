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
        private Preferences preferences;
        private Dictionary<string, bool> categoryExpansionState = new Dictionary<string, bool>();
        private readonly Dictionary<string, (string category, string description)> defaultCategories = new Dictionary<string, (string, string)>
        {
            { "RIFF - RIFF/RIFX音频", ("音频", "RIFF/RIFX资源交换文件格式家族的音频提取器,支持提取wwise的小端序wem、索尼的at3/at9、微软的xma和xwma,常见的wav和RIFX的大端序wem格式") },
            { "RIFF - Fmod - bank", ("音频", "从游戏文件中提取Fmod音频库的bank文件,如国产手游白荆回廊、云裳羽衣等均采用此格式存储音频,提取后可进一步处理其中的音频资源") },
            { "RIFF - cdxa - xa", ("音频", "专门从PlayStation游戏的XA文件中提取xa音频。需注意区分大小写,XA是打包多个xa音频的容器文件,该工具可有效解析并提取其中的音频内容") },
            { "CRI - adpcm_adx - adx", ("音频", "以二进制形式从cpk、afs、awb、acb甚至uasset、uexp等多种文件中提取adx文件") },
            { "CRI - adpcm_adx - ahx", ("音频", "以二进制形式从cpk、afs等文件中提取ahx文件") },
            { "Fmod - fsb5", ("音频", "可从Fmod的bank中提取fsb文件,也支持从steam游戏永恒轮回和女神异闻录5对决_幽灵先锋的resources.resource中提取fsb。针对正当防卫4的arc文件,还提供了专用提取方法,功能全面") },
            { "Xiph.Org - Ogg", ("音频", "从各种游戏中提取ogg格式的音频文件。ogg是广泛使用的音频格式,该工具能适配不同游戏的打包方式,高效提取其中的ogg音频") },
            { "CRI - HCA - hca", ("音频", "以二进制形式从cpk、awb、acb或者uexp等文件中提取打包的hca音频文件。若提取的是加密hca文件,会自带enc后缀名（如enc.hca）,方便用户识别处理") },
            { "任天堂 - libopus - lopus", ("音频", "任天堂switch平台专用音频提取器,代表作为月姬重制版。其提取的音频文件头为OPUS,实际是libopus编码的lopus格式,与普通opus不同,本工具可精准提取此类特殊格式") },
            { "光荣特库摩 - kvs/ktss", ("音频", "提取光荣特库摩游戏的kvs和ktss音频格式,主要支持从switch平台提取kns、steam平台的kvs格式,同时兼容ps4平台的at3格式,适配多平台同类游戏音频提取") },
            { "RIFF - Google - webp", ("图片", "基于RIFF资源交换文件格式,提取Google开发的WebP格式图片") },
            { "联合图像专家组 - JPEG/JPG", ("图片", "以二进制形式从游戏文件中提取jpg图片,jpg是标准计算机文件,有固定文件头和文件尾") },
            { "便携式网络图形 - PNG", ("图片", "以二进制形式从游戏文件中提取png图片,png是标准计算机文件,具有固定文件头和文件尾") },
            { "位图图像文件 - BMP", ("图片", "以二进制形式从游戏文件中提取bmp图片,bmp是标准计算机文件,具有固定文件头") },
            { "标记图形文件格式 - TGA", ("图片", "以二进制形式从游戏文件中提取tga图片") },
            { "索尼 - gxt转换器", ("图片", "将PlayStation游戏的gxt格式转换为png格式的工具,支持批量转换。与gxt提取器不同,该工具专注于格式转换") },
            { "ENDILTLE - APK - apk", ("其他档案", "文件头为ENDILTLE的apk提取器,此类apk非安卓apk,代表游戏包括Artdink开发的龙珠Z_终极之战、万代南梦宫的刀剑神域失落之歌等") },
            { "东方天空竞技场 - GPK - gpk", ("其他档案", "东方天空竞技场_幻想乡空战姬的专用解包器") },
            { "GxArchivedFile - dat", ("其他档案", "Mizuchi引擎开发的游戏档案专用提取器,广泛用于地雷社游戏,如死亡终局轮回试炼系列、神狱塔断罪玛丽最终篇等") },
            { "苍之彼方的四重奏EXTRA2 - dat", ("其他档案", "苍之彼方的四重奏2 Extra的专用提取器") },
            { "Lightvn galgame engine - mcdat/vndat", ("其他档案", "Light.vn galgame引擎的提取器,可解包宝石少女1st的mcdat和爱你,我的英雄！的vndat文件") },
            { "CRI - afs提取器", ("其他档案", "用于解包criware的afs档案,该文件在任天堂、索尼、世嘉和xbox360平台较为常见") },
            { "CRI - cpk", ("其他档案", "用于解包CRIWARE的cpk格式档案,该格式应用广泛,可存储多种类型资源") },
            { "IdeaFactory - tid", ("图片", "地雷社海王星系列tid文件转换工具,可将tid转换为dds格式,且支持BC7纹理的tid文件") },
            { "第七史诗 - sct", ("图片", "第七史诗的sct转换器,支持批量将sct格式转换为png格式") },
            { "万代南梦宫 - bnsf", ("音频", "万代南梦宫的bnsf音频提取器,以二进制形式从TLFILE.TLDAT文件中提取。可解析情热传说和ps3平台的狂战传说中的音频,注意狂战传说steam版因加密无法提取") },
            { "索尼 - gxt提取器", ("其他档案", "从索尼的psv、psp等平台游戏中提取gxt文件。许多游戏喜欢将多个gxt文件打包存储,该工具可有效提取这些gxt文件") },
            { "直接绘制表面 - DDS", ("图片", "以二进制形式从游戏文件中提取dds图片,亲测适用于战场女武神4等游戏,支持一般的dds和DX10纹理的dds格式") },
            { "Filename小端序 - pck", ("其他档案", "steam和switch平台的超女神信仰诺瓦露、steam和psv的约战凛绪轮回、steam白色相簿_编缀的冬日回忆和传颂之物二人的白皇的专用解包工具") },
            { "Filename大端序 - pck", ("其他档案", "ps3约会大作战凛祢理想乡和约会大作战或守安装、白色相簿的专用解包工具") },
            { "地雷社和AQUAPLUS专用纹理 - tex", ("图片", "超女神信仰诺瓦露、白色相簿_编缀的冬日回忆、约会大作战系列、传颂之物二人的白皇专用tex转换器") },
            { "SEGS binary data - bin", ("其他档案", "苍翼默示录_刻之幻影专用bin文件解包工具,可提取其中包含的所有pac文件") },
            { "苍翼默示录_刻之幻影 - pac", ("其他档案", "苍翼默示录_刻之幻影的专用解包工具,无法用于苍翼默示录_神观之梦") },
            { "苍翼默示录_神观之梦 - pac", ("其他档案", "苍翼默示录_神观之梦的专用解包工具,支持解包压缩的pac文件和普通pac文件") },
            { "断罪的玛利亚 - dat", ("其他档案", "ps3游戏断罪的玛利亚专用提取器,可解包data.dat文件,提取其中的视频、音频和图片") },
            { "进击的巨人_自由之翼 - bin", ("其他档案", "进击的巨人_自由之翼提取器,能从LINKDATA.bin文件中提取出g1t、g1m等文件") },
            { "PlayStation 4 bit ADPCM - vag", ("音频", "以二进制形式从PlayStation游戏文件中提取vag音频文件,vag是采用PlayStation 4 bit ADPCM编码的音频格式") },
            { "零_濡鸦之巫女 - fmsg", ("其他档案", "零濡鸦之巫女的fmsg转换器,可将fmsg文件转换为txt文本") },
            { "零_濡鸦之巫女 - kscl", ("图片", "零濡鸦之巫女的kscl转换器,能将kscl文件转换为dds图片") },
            { "PhyreEngine Texture - phyre", ("图片", "从phyre引擎的phyre文件中提取dds图片的提取器,适用于刀剑神域虚空断章、彼岸游境、东京幻都EX等采用该引擎的游戏") },
            { "PhyreEngine package - pkg", ("其他档案", "phyre引擎的pkg文件提取器,适配东京幻都EX、闪之轨迹、创之轨迹等采用该引擎的游戏,可提取pkg文件中的各类资源") },
            { "女神异闻录5对决_幽灵先锋 - bin", ("其他档案", "女神异闻录5对决_幽灵先锋的专用wmv提取器,能从movie文件夹的bin文件中提取上百个wmv视频") },
            { "MPEG-4 - mp4", ("其他档案", "从游戏文件中提取mp4视频文件。尽管mp4文件头前四字节不固定,但后4字节固定,工具可依据此特征轻松提取合法mp4视频") },
            { "IdeaFactory - bra", ("其他档案", "bra提取器,适用于地雷社妖精剑士F和Falcome的东京幻都EX等游戏") },
            { "任天堂 - 3DS/WII/WIIU sound", ("音频", "任天堂wii、wiiu和3ds平台的音频提取器,能从brsar、bfsar和bcsar文件中提取出br/bf/bc前缀的wav波形音频文件") },
            { "Binary Audio Archive - baa", ("其他档案", "一种未知的音频档案格式提取工具。aw文件因不包含头部信息、文件大小等标识数据而依赖baa索引文件,代表作有wiiu平台的塞尔达传说黄昏公主HD") },
            { "Audio Archive - aw", ("音频", "baa文件配套提取器,用于从baa文件中提取wsys文件。无wsys文件则无法解包aw文件,且需将wsys文件放入aw文件夹才能提取wav,目前vgmstream已支持解码aw/baa") },
            { "反恐精英OL - pak", ("其他档案", "反恐精英ol的pak提取器,csol的打包格式分nar和pak两种,本工具专门用于解pak文件") },
            { "IdeaFactory - pac提取器", ("其他档案", "地雷社游戏的pac提取器,此pac格式常用于海王星系列游戏") },
            { "IdeaFactory - pac打包器", ("其他档案", "地雷社游戏的pac打包器,能将文件夹重新打包成pac文件") },
            { "光荣特库摩 - gz/exlilr", ("其他档案", "光荣特库摩的gz和elixir文件提取器,适用于蓝色反射_帝、幻舞少女之剑、无夜之国等游戏") },
            { "光荣特库摩 - ebm", ("其他档案", "光荣特库摩的ebm文件提取器,适配蓝色反射_帝、幻舞少女之剑、无夜之国等游戏") },
            { "光荣特库摩 - g1t", ("图片", "光荣特库摩的g1t文件提取器,可提取出dds图片,适用于fate系列、无夜之国2等游戏") },
            { "光荣特库摩 - gmpk", ("其他档案", "光荣特库摩的gmpk文件提取器,适用于零系列的濡鸦之巫女等游戏") },
            { "光荣特库摩 - pak", ("其他档案", "光荣特库摩的pak文件提取器,适用于蓝色反射_帝、幻舞少女之剑、苏菲的炼金工房2等游戏") },
            { "PowerVR转换png", ("图片", "pvr纹理转换器,无需依赖Texturepacker的免费试用限制,可无限使用,将pvr纹理转换为png格式,适用于逆战upk文件提取的pvr") },
            { "逆战 - upk", ("其他档案", "逆战upk提取器,解决手动逐个解包的麻烦,支持批量解包,高效提取upk文件中的资源") },
            { "战争传说 - pak", ("其他档案", "战争传说的pak解包工具,基于bms脚本改写成c#语言,解决了原PAKTool易出现非法字符报错的问题,可彻底解包该游戏所有pak文件") },
            { "IdeaFactory - cl3", ("其他档案", "地雷社游戏的CL3提取器,可从CL3文件中提取dat、tid等文件,代表作为妖精剑士F,解决了stcm-editor.exe工具使用不便的问题") },
            { "5pb - LNK4 archives - dat", ("其他档案", "5pb的LNK4文件头的dat解包工具,可解xbox360游戏11只眼_交错的视线等采用该格式的文件") },
            { "万代南梦宫 - 情热传说 - dat", ("其他档案", "万代南梦宫的情热传说TLDAT解包工具,也可解狂战传说的相关文件,但仅支持PS3平台,不支持加密的steam平台版本") },
            { "Brotli - brotli_compress", ("压缩", "使用Brotli算法批量压缩文件,注意压缩速度较慢,若无法删除文件夹可在任务管理器中退出brotli.exe进程") },
            { "Brotli - brotli_decompress", ("解压", "使用Brotli算法批量解压文件,解压速度较快,能高效处理采用该算法压缩的文件") },
            { "Gzip - gzip_compress", ("压缩", "使用Gzip算法批量压缩文件,适用于需要采用该经典压缩算法处理文件的场景") },
            { "Gzip - gzip_decompress", ("解压", "使用Gzip算法批量解压文件,可快速解压缩采用该算法压缩的文件") },
            { "Huffman - huffman_compress", ("压缩", "使用Huffman算法批量压缩文件,利用该算法的特性对文件进行压缩处理") },
            { "Huffman - huffman_decompress", ("解压", "使用Huffman算法批量解压文件,专门用于解压缩采用Huffman算法压缩的文件") },
            { "Lz4 - lz4_compress", ("压缩", "使用Lz4算法批量压缩文件,基于C#第三方库实现,满足对文件进行Lz4压缩的需求") },
            { "Lz4 - lz4_decompress", ("解压", "使用Lz4算法批量解压文件,基于C#第三方库实现,可解压缩采用Lz4算法压缩的文件") },
            { "Lz4c - lz4c_compress", ("压缩", "使用Lz4c算法批量压缩文件,通过调用外部可执行程序实现,适用于需要Lz4c压缩的场景") },
            { "Lz4c - lz4c_decompress", ("解压", "使用Lz4c算法批量解压文件,通过调用外部可执行程序实现,用于解压缩Lz4c算法处理的文件") },
            { "LZ77 - lz77_compress", ("压缩", "使用Lz77算法批量压缩文件,利用该经典压缩算法对文件进行压缩处理") },
            { "LZ77 - lz77_decompress", ("解压", "使用Lz77算法批量解压文件,专门解压缩采用Lz77算法压缩的文件") },
            { "LZMA - 7-zip_lzma_compress", ("压缩", "使用Lzma算法批量压缩文件,基于7-zip的相关实现,适用于需要高压缩率的场景") },
            { "LZMA - 7-zip_lzma_decompress", ("解压", "使用Lzma算法批量解压文件,可解压缩采用Lzma算法压缩的文件,包括7-zip生成的相关文件") },
            { "LZO - lzo_compress", ("压缩", "使用Lzo算法批量压缩文件,利用该算法的特点对文件进行压缩处理") },
            { "LZO - lzo_decompress", ("解压", "使用Lzo算法批量解压文件,用于解压缩采用Lzo算法压缩的文件") },
            { "LZSS - lzss自定义压缩", ("压缩", "使用自定义Lzss算法批量压缩文件,针对特定场景优化的Lzss压缩实现,满足特殊压缩需求") },
            { "LZSS - lzss自定义解压", ("解压", "使用自定义Lzss算法批量解压文件,专门用于解压缩采用自定义Lzss算法压缩的文件") },
            { "Lzham - lzham自定义压缩", ("压缩", "使用自定义Lzham算法批量压缩文件,基于Lzham算法的定制实现,适用于特定压缩场景") },
            { "Lzham - lzham自定义解压", ("解压", "使用自定义Lzham算法批量解压文件,用于解压缩采用自定义Lzham算法压缩的文件") },
            { "Lzham - Lzham标准压缩", ("压缩", "使用标准Lzham算法批量压缩文件,遵循标准Lzham算法实现,保证兼容性") },
            { "Lzham - Lzham标准解压", ("解压", "使用标准Lzham算法批量解压文件,可解压缩采用标准Lzham算法压缩的文件") },
            { "Minlz - minlz_compress", ("压缩", "使用Minlz算法批量压缩文件,利用该算法对文件进行压缩处理") },
            { "Minlz - minlz_decompress", ("解压", "使用Minlz算法批量解压文件,专门解压缩采用Minlz算法压缩的文件") },
            { "Mio0 - mio0_compress", ("压缩", "使用Mio0算法批量压缩文件,适用于需要采用该算法进行压缩的场景") },
            { "Mio0 - mio0_decompress", ("解压", "使用Mio0算法批量解压文件,用于解压缩采用Mio0算法压缩的文件") },
            { "Oodle - oodle_compress", ("压缩", "使用Oodle算法批量压缩文件,利用该高效压缩算法对文件进行压缩处理") },
            { "Oodle - oodle_decompress", ("解压", "使用Oodle算法批量解压文件,可解压缩采用Oodle算法压缩的文件") },
            { "Snappy - snappy_compress", ("压缩", "使用Snappy算法批量压缩文件,该算法注重压缩速度,适用于对速度要求较高的场景") },
            { "Snappy - snappy_decompress", ("解压", "使用Snappy算法批量解压文件,解压速度快,用于处理采用Snappy算法压缩的文件") },
            { "Wflz - wflz_compress", ("压缩", "使用Wflz算法批量压缩文件,适用于需要采用该算法进行压缩的场景") },
            { "Wflz - wflz_decompress", ("解压", "使用Wflz算法批量解压文件,用于解压缩采用Wflz算法压缩的文件") },
            { "Yay0 - yay0_compress", ("压缩", "使用Yay0算法批量压缩文件,适用于相关平台或场景下的文件压缩需求") },
            { "Yay0 - yay0_decompress", ("解压", "使用Yay0算法批量解压文件,专门解压缩采用Yay0算法压缩的文件") },
            { "Yaz0 - yaz0_compress", ("压缩", "使用Yaz0算法批量压缩文件,适用于需要采用该算法进行压缩的场景") },
            { "Yaz0 - yaz0_decompress", ("解压", "使用Yaz0算法批量解压文件,用于解压缩采用Yaz0算法压缩的文件") },
            { "Zlib - zlib_compress", ("压缩", "使用Zlib算法批量压缩文件,经典的压缩算法,应用广泛,适用于多种场景") },
            { "Zlib - zlib_decompress", ("解压", "使用Zlib算法批量解压文件,可解压缩采用Zlib算法压缩的文件") },
            { "ZSTD - zstd_compress", ("压缩", "使用Zstd算法批量压缩文件,该算法在压缩率和速度上有较好平衡,适用于多种场景") },
            { "ZSTD - zstd_decompress", ("解压", "使用Zstd算法批量解压文件,用于解压缩采用Zstd算法压缩的文件") },
            { "Wiiu - gtx转换器", ("图片", "任天堂wiiu平台的gtx转换器,可将gtx格式转换为png图片") },
            { "Wiiu - h3/app", ("其他档案", "任天堂wiiu平台的rom解包器,能将wiiu平台的h3、app文件转换成loadiine格式,方便用户解包") },
            { "Nds - nds提取器", ("其他档案", "任天堂nds平台的rom解包工具,可解包nds rom文件,提取其中的各类资源") },
            { "Nds - nds打包器", ("其他档案", "任天堂nds平台的rom打包器,能将解包的nds文件夹重新打包成nds rom文件") },
            { "3ds - darc提取器", ("其他档案", "任天堂3ds平台的darc解包工具,代表作为美妙旋律七彩演唱会闪耀设计,可提取darc文件中的资源") },
            { "3ds - darc打包器", ("其他档案", "任天堂3ds平台的darc打包器,能将文件夹里的文件打包成darc文件") },
            { "Nds - narc提取器", ("其他档案", "任天堂nds平台的narc文件提取器,代表作为口袋妖怪(nds),可提取narc文件中的资源") },
            { "PS3 - psarc提取器", ("其他档案", "索尼ps3平台的psarc解包工具,代表作为第二次超级机器人大战OG,也可解包无人深空的pak文件") },
            { "PS3 - psarc打包器", ("其他档案", "索尼ps3平台的psarc打包器,能将一个文件夹重新打包成psarc文件") },
            { "PS3 - NPDRM - sdat", ("其他档案", "索尼ps3平台的sdat解包工具,代表作为约会大作战或守安装、约会大作战凛弥理想乡等,可提取sdat文件中的资源") },         
            { "CRI - afs打包器", ("其他档案", "CRIware的afs档案打包器,可将一个文件夹及子文件夹里的所有文件重新打包成afs文件") },
            { "Mages - mpk提取器", ("其他档案", "Mages的mpk解包工具,代表作为命运石之门,可提取mpk文件中的各类资源") },
            { "Mages - mpk打包器", ("其他档案", "Mages的mpk打包器,能将一个文件夹及子文件夹里的所有文件重新打包成mpk文件") },
            { "Gnf2Png", ("图片", "PS4平台的gnf到png的转换器,支持批量转换,解决了GFDstudio手动转换的繁琐问题") },
            { "wav2qoa - 转换qoa", ("音频", "wav到qoa的音频转换器,转换后的qoa格式可被ffmpeg识别,也可用foobar2000播放") },
            { "CMVS_Engine - cmv", ("其他档案", "CMVS引擎的cmv视频解码器,代表作为天津罪,可解码该引擎的cmv视频文件") },
            { "SRPG_Studio - dts", ("其他档案", "SRPG Studio的dts提取器,代表作为刻印战记2_七圣英雄,可提取dts文件中的资源") },
            { "XACT Wave Bank - xwb打包器", ("其他档案", "XWB打包器,能将一个文件夹里的所有wav打包成xwb文件,为了打包成功建议使用pcm_s16le的wav文件,有些编码不支持") },
            { "PNG2ASTC", ("图片", "png到astc图像的转换器,支持批量转换,满足将png图片转换为astc格式的需求") },
            { "ASTC2PNG", ("图片", "astc图像到png的转换器,支持批量转换,方便查看和使用astc格式的图片") },
            { "hip2png", ("图片", "hip到png的转换器,代表作为switch平台的赛马娘,可批量处理该格式转换成png") },
            { "双截龙彩虹pak提取器", ("其他档案", "双截龙彩虹的专用pak提取器") },
            { "CFSI - cfsi提取器", ("其他档案", "cfsi文件专用提取器,适用于极限脱出3_零时困境和Re_从零开始的异世界生活虚假的王选候补等游戏") },
            { "CFSI - cfsi打包器", ("其他档案", "cfsi打包器,可将一个文件夹及子文件夹里的所有文件重新打包成cfsi文件") },
            { "消逝的光芒 - rpack", ("其他档案", "消逝的光芒rpack专用提取器") },
            { "消逝的光芒 - csb", ("其他档案", "消逝的光芒csb专用提取器") },
            { "PlayStation MultiStream File - msf", ("音频", "以二进制形式从索尼游戏中提取msf音频文件") },
            { "PlayStation - pssg archive", ("图片", "PlayStation pssg档案提取器,可提取该档案中的图片资源") },
            { "Terminal Reality - pod/epd ahchive", ("其他档案", "Terminal Reality工作室的pod档案提取器,代表作为星球大战原力释放2,可提取pod/epd档案中的资源") },
            { "PlayStation - GPDA archive", ("其他档案", "索尼psp平台上常见的GPDA档案的专用提取器,例如我的妹妹不可能那么可爱、凉宫春日的追忆等游戏") },
            { "暗影狂奔 - data/toc archive", ("其他档案", "Xbox360游戏暗影狂奔和皇牌空战6解放之战火的专用提取器,可解data/toc这种组合打包的档案") },
            { "ahx2wav", ("音频", "此工具可以将Criware的ahx文件转换成wav格式") },
            { "异度之刃2 - ard/arh archive", ("其他档案", "switch游戏异度之刃2的提取器,可解包ard/arh这种组合打包的档案") },
            { "异度之刃3 - ard/arh archive", ("其他档案", "switch游戏异度之刃3的提取器,可解包ard/arh这种组合打包的档案") },
            { "异度之刃 - LBIM2DDS", ("图片", "异度之刃系列的LBIM转换器,可将文件尾为LBIM的文件转换成dds图像,如果是wismda文件该工具会先拆分xbc1文件,如果是xbc1文件会先移除前48字节,随后zlib解压,然后转换成dds图片,一步到位") },          
            { "异度之刃 - arc file", ("其他档案", "从3ds平台的异度之刃arc文件里面提取tpl文件") },
            { "异度之刃 - BC file", ("其他档案", "从异度之刃系列游戏提取BC动画文件,这些文件包含ANIM签名") },
            { "异度之刃 - tpl2bclim", ("图片", "将3ds平台异度之刃的tpl文件转换成bclim文件") },
            { "异度之刃 - bclim2png", ("图片", "将bclim文件转换成png文件") },
            { "异度之刃 - bdat提取器", ("其他档案", "异度之刃系列游戏的bdat提取器,不支持3ds平台,异度之刃2的部分bdat无法正常提取,即使zlib解压后也不行") },
            { "异度之刃 - bdat打包器", ("其他档案", "将json文件夹重新打包为bdat文件,目录必须为解包后的文件夹结构(包含bschema文件和json文件夹)") },
            { "异度之刃 - MXTX file", ("其他档案", "异度之刃系列游戏的MTXT提取器,从casmt、caevd、camdo、casmda、bmn、caavp等文件里面提取mtxt文件,然后你再用转换器转换成dds") },
            { "异度之刃 - LBIM file", ("其他档案", "表面上看这是个异度之刃系列的LBIM提取器,实际上它是xbc1专用分解器,从pcsmt、mot、wismt、wifnt、winvda、wismda、wilay等文件里面提取出各种文件,提取出来后有些文件还能再用它二次提取") },
            { "异度之刃 - pcbeb file", ("其他档案", "异度之刃终极版的pcbeb解包器,拆分、删除垃圾字节、解压再解压") },
            { "异度之刃 - MXTX2DDS", ("图片", "异度之刃系列游戏的MTXT转换器,可以把MTXT纹理转换成dds图像") },
            { "异度之刃 - map.pkb", ("其他档案", "wii平台异度之刃的map.pkb专用器") },
            { "异度之刃 - sar", ("其他档案", "wiiu平台异度之刃x的sar解包器,可提取出里面的hkt文件,它也是switch平台异度之刃终极版的提取器,可提取mcapk和chr文件里面的数据") },
            { "Fate Extella/Link - pk/pfs/pkb", ("其他档案", "psv平台的Fate Extella和Fate Extella Link专用提取器") },
            { "白色相簿2 - dar archive", ("其他档案", "ps3平台的白色相簿2的data.dar专用提取器,voice.dar使用RIFF/RIFX提取器提取就可以了") },
            { "白色相簿2 - pak archive", ("其他档案", "steam平台的白色相簿2的pak专用提取器，和ps3的dar完全不同") },
            { "PlayStation - TROPHY.TRP file", ("其他档案", "索尼Playstation平台的trp奖杯文件提取器,代表游戏如ps3的白色相簿、psv的SD高达G世纪-创世") },
            { "奥特曼格斗进化3 - bin", ("其他档案", "ps2平台的奥特曼格斗进化3专用提取器,由quickbms脚本修改而来") },
            { "混乱特工 - vpp_pc", ("其他档案", "混乱特工的专用提取器") },
            { "捍卫雄鹰2 - gtp archive", ("其他档案", "捍卫雄鹰IL-2斯大林格勒战役的gtp专用提取器") },
            { "DXBC - DirectX Bytecode", ("其他档案", "DirectX字节码文件专用提取器") },
            { "DXBC2HLSL", ("其他档案", "DXBC到HLSL文件的转换器,使用CMD_Decompiler反编译转换") },
            { "地雷社 - cat archive", ("其他档案", "激次元组合布兰+涅普缇努VS僵尸军团、神次元偶像:海王星PP和海王星U的cat专用提取器") },
            { "rad game tools - rada提取器", ("音频", "rad game tools开发的rada音频文件专用提取器,先用Fmodel把pak和ucas文件里的uasset跟ubulk文件全部提取出来再提取rada文件") },
            { "rad game tools - rada转换器", ("音频", "rad game tools开发的rada音频转换器，可以将vgmstream不支持的rada转换成wav") },
            { "Xbox360 - god2iso打包器", ("其他档案", "xbox360 iso打包器,从god镜像格式打包成iso镜像格式") },
            { "Xbox360 - iso提取器", ("其他档案", "xbox360 iso提取器，从iso镜像里把游戏文件全部提取出来") },
            { "Dreamcast - Bin/Cue转换GDI", ("其他档案", "将Dreamcast游戏的Bin/Cue镜像文件转换为GDI格式") },
        };
        public SuperToolbox()
        {
            InitializeComponent();
            txtFolderPath.AllowDrop = true;
            txtFolderPath.DragEnter += TxtFolderPath_DragEnter;
            txtFolderPath.DragDrop += TxtFolderPath_DragDrop;
            txtFolderPath.DragLeave += TxtFolderPath_DragLeave;
            btnAbout.Click += BtnAbout_Click;
            preferences = Preferences.Load();
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

            treeView1.MouseMove += TreeView1_MouseMove;
            treeView1.AfterExpand += TreeView1_AfterExpand;
            treeView1.AfterCollapse += TreeView1_AfterCollapse;
        }
        private void InitializeTreeView()
        {
            foreach (string category in defaultCategories.Values.Select(x => x.category).Distinct())
            {
                AddCategory(category);
            }

            var sortedExtractors = defaultCategories
                .OrderBy(x => x.Key)
                .ToList();

            foreach (var item in sortedExtractors)
            {
                string extractorName = item.Key;
                string categoryName = item.Value.category;

                if (categoryNodes.TryGetValue(categoryName, out TreeNode? categoryNode))
                {
                    TreeNode extractorNode = categoryNode.Nodes.Add(extractorName);
                    formatNodes[extractorName] = extractorNode;
                    extractorNode.Tag = extractorName;
                }
            }

            foreach (TreeNode categoryNode in treeView1.Nodes)
            {
                var sortedChildren = categoryNode.Nodes
                    .Cast<TreeNode>()
                    .OrderBy(node => node.Text)
                    .ToList();

                categoryNode.Nodes.Clear();
                categoryNode.Nodes.AddRange(sortedChildren.ToArray());
                string categoryName = categoryNode.Text;
                bool shouldExpand = preferences.ExpandedCategories.ContainsKey(categoryName)
                    ? preferences.ExpandedCategories[categoryName]
                    : false; 

                if (shouldExpand)
                {
                    categoryNode.Expand();
                }
                else
                {
                    categoryNode.Collapse();
                }

                categoryExpansionState[categoryName] = shouldExpand;
            }
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
                    EnqueueMessage($"已选择文件夹:{folderBrowserDialog.SelectedPath}");
                }
            }
        }
        private readonly HashSet<string> _converters = new HashSet<string>
        {
         "PNG编码ASTC", "ASTC解码PNG", "Gnf2Png", "PowerVR转换png","异度之刃 - tpl2bclim","异度之刃 - bclim2png","异度之刃 - MXTX2DDS",
         "第七史诗 - sct", "索尼 - gxt转换器", "地雷社和AQUAPLUS专用纹理 - tex","DXBC2HLSL","rad game tools - rada转换器",
         "wav2qoa - 转换qoa", "Wiiu - gtx转换器", "hip2png","异度之刃 - LBIM2DDS","ahx2wav","Dreamcast - Bin/Cue转换GDI"
        };
        private bool IsConverter(string formatName) => _converters.Contains(formatName);
        private async void btnExtract_Click(object sender, EventArgs e)
        {
            if (isExtracting)
            {
                EnqueueMessage("正在进行操作,请等待...");
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
                            EnqueueMessage($"{operationType}操作完成,总共{operationType}了{count}个文件");
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
                            EnqueueMessage($"{operationType}过程中出现错误:{ex.Message}");
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
                EnqueueMessage($"操作初始化失败:{ex.Message}");
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
                    EnqueueMessage($"转换完成,共转换{count}个文件");
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
                    EnqueueMessage($"打包完成,共打包{count}个文件");
                };
                extractor.PackingFailed += (s, error) =>
                {
                    EnqueueMessage($"打包失败:{error}");
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
                    EnqueueMessage($"压缩完成,共压缩{count}个文件");
                };

                extractor.CompressionFailed += (s, error) =>
                {
                    EnqueueMessage($"压缩失败:{error}");
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
                    EnqueueMessage($"解压完成,共解压{count}个文件");
                };
                extractor.DecompressionFailed += (s, error) =>
                {
                    EnqueueMessage($"解压失败:{error}");
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
                    EnqueueMessage($"提取完成,共提取出{totalFileCount}个文件");
                };
                extractor.ExtractionFailed += (s, error) =>
                {
                    EnqueueMessage($"提取失败:{error}");
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
                    string formattedMessage = isError ? $"错误:{message}" : $"{prefix}: {message}";
                    EnqueueMessage(formattedMessage);
                }));
            }
        }
        private BaseExtractor CreateExtractor(string formatName)
        {
            switch (formatName)
            {
                case "RIFF - RIFF/RIFX音频": return new RIFF_RIFX_Sound_Extractor();
                case "RIFF - Fmod - bank": return new BankExtractor();
                case "RIFF - Google - webp": return new WebpExtractor();
                case "RIFF - cdxa - xa": return new CdxaExtractor();
                case "CRI - adpcm_adx - adx": return new AdxExtractor();
                case "CRI - adpcm_adx - ahx": return new AhxExtractor();
                case "Fmod - fsb5": return new Fsb5Extractor();
                case "任天堂 - libopus - lopus": return new LopusExtractor();
                case "光荣特库摩 - kvs/ktss": return new Kvs_Kns_Extractor();
                case "Xiph.Org - Ogg": return new OggExtractor();
                case "联合图像专家组 - JPEG/JPG": return new JpgExtractor();
                case "便携式网络图形 - PNG": return new PngExtractor();
                case "位图图像文件 - BMP": return new BmpExtractor();
                case "标记图形文件格式 - TGA": return new TgaExtractor();
                case "CRI - HCA - hca": return new HcaExtractor();
                case "ENDILTLE - APK - apk": return new ApkExtractor();
                case "东方天空竞技场 - GPK - gpk": return new GpkExtractor();
                case "GxArchivedFile - dat": return new GDAT_Extractor();
                case "苍之彼方的四重奏EXTRA2 - dat": return new Aokana2Extractor();
                case "Lightvn galgame engine - mcdat/vndat": return new LightvnExtractor();
                case "CRI - afs提取器": return new AfsExtractor();
                case "CRI - cpk": return new CpkExtractor();
                case "IdeaFactory - tid": return new TidExtractor();
                case "第七史诗 - sct": return new Sct2Png_Converter();
                case "万代南梦宫 - bnsf": return new Bnsf_Extractor();
                case "索尼 - gxt提取器": return new SonyGxtExtractor();
                case "直接绘制表面 - DDS": return new DdsExtractor();
                case "Filename小端序 - pck": return new Filename_Pck_LE_Extractor();
                case "Filename大端序 - pck": return new Filename_Pck_BE_Extractor();
                case "地雷社和AQUAPLUS专用纹理 - tex": return new StingTexConverter();
                case "SEGS binary data - bin": return new SEGS_BinExtractor();
                case "苍翼默示录_刻之幻影 - pac": return new FPAC_CP_Extractor();
                case "苍翼默示录_神观之梦 - pac": return new FPAC_CF_Extractor();
                case "PlayStation 4 bit ADPCM - vag": return new VagExtractor();
                case "断罪的玛利亚 - dat": return new DataDatExtractor();
                case "进击的巨人_自由之翼 - bin": return new Attack_on_Titan_Wings_Extractor();
                case "索尼 - gxt转换器": return new SonyGxtConverter();
                case "零_濡鸦之巫女 - fmsg": return new FMSG_Extractor();
                case "零_濡鸦之巫女 - kscl": return new KSCL_Extractor();
                case "PhyreEngine Texture - phyre": return new PhyreTexture_Extractor();
                case "PhyreEngine package - pkg": return new PhyrePKG_Extractor();
                case "女神异闻录5对决_幽灵先锋 - bin": return new P5S_WMV_Extractor();
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
                case "LZO - lzo_compress": return new Lzo_Compressor();
                case "LZO - lzo_decompress": return new Lzo_Decompressor();
                case "LZSS - lzss自定义压缩": return new LzssCustom_Compressor();
                case "LZSS - lzss自定义解压": return new LzssCustom_Decompressor();
                case "Lzham - lzham自定义压缩": return new LzhamCustom_Compressor();
                case "Lzham - lzham自定义解压": return new LzhamCustom_Decompressor();
                case "Lzham - Lzham标准压缩": return new LzhamStandard_Compressor();
                case "Lzham - Lzham标准解压": return new LzhamStandard_Decompressor();
                case "Minlz - minlz_compress": return new Minlz_Compressor();
                case "Minlz - minlz_decompress": return new Minlz_Decompressor();
                case "Mio0 - mio0_compress": return new Mio0_Compressor();
                case "Mio0 - mio0_decompress": return new Mio0_Decompressor();
                case "Oodle - oodle_compress": return new Oodle_Compressor();
                case "Oodle - oodle_decompress": return new Oodle_Decompressor();
                case "Snappy - snappy_compress": return new Snappy_Compressor();
                case "Snappy - snappy_decompress": return new Snappy_Decompressor();
                case "Wflz - wflz_compress": return new Wflz_Compressor();
                case "Wflz - wflz_decompress": return new Wflz_Decompressor();
                case "Yay0 - yay0_compress": return new Yay0_Compressor();
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
                case "CRI - afs打包器": return new AfsRepacker();
                case "Mages - mpk提取器": return new MagesMpkExtractor();
                case "Mages - mpk打包器": return new MagesMpkRepacker();
                case "Gnf2Png": return new GNF2PNG_Converter();
                case "wav2qoa - 转换qoa": return new Wav2Qoa_Converter();
                case "CMVS_Engine - cmv": return new CmvDecoder();
                case "SRPG_Studio - dts": return new DtsExtractor();
                case "XACT Wave Bank - xwb打包器": return new XWBPacker();
                case "PNG2ASTC": return new Png2Astc_Converter();
                case "ASTC2PNG": return new Astc2Png_Converter();
                case "hip2png": return new Hip2Png_Converter();
                case "双截龙彩虹pak提取器": return new DoubleDragonNeon_PakExtractor();
                case "CFSI - cfsi提取器": return new Cfsi_Extractor();
                case "CFSI - cfsi打包器": return new Cfsi_Repacker();
                case "消逝的光芒 - rpack": return new DyingLight_rpack_Extractor();
                case "消逝的光芒 - csb": return new DyingLight_csb_Extractor();
                case "PlayStation MultiStream File - msf": return new Msf_Extractor();
                case "PlayStation - pssg archive": return new PSSG_Extractor();
                case "Terminal Reality - pod/epd ahchive": return new PodExtractor();
                case "PlayStation - GPDA archive": return new GPDA_Extractor();
                case "暗影狂奔 - data/toc archive": return new DataToc_Extractor();
                case "异度之刃2 - ard/arh archive": return new Xenoblade2_Extractor();
                case "异度之刃3 - ard/arh archive": return new Xenoblade3_Extractor();
                case "异度之刃 - LBIM2DDS": return new LBIM2DDS_Converter();
                case "ahx2wav": return new Ahx2wav_Converter();
                case "异度之刃 - arc file": return new XenobladeTpl_Extractor();
                case "异度之刃 - BC file": return new XenobladeBC_Extractor();
                case "异度之刃 - tpl2bclim":return new Tpl2bclim_Converter();
                case "异度之刃 - bclim2png": return new Bclim2png_Converter();
                case "异度之刃 - bdat提取器":return new XenobladeBdat_Extractor();
                case "异度之刃 - bdat打包器":return new XenobladeBdat_Repacker();
                case "异度之刃 - MXTX file": return new XenobladeMTXT_Extractor();
                case "异度之刃 - LBIM file": return new XenobladeLBIM_Extractor();
                case "异度之刃 - MXTX2DDS": return new MTXT2DDS_Converter();
                case "异度之刃 - map.pkb": return new XenobladeMap_Extractor();
                case "异度之刃 - sar": return new XenobladeSar_Extractor();
                case "异度之刃 - pcbeb file": return new Xenoblade_Pcbeb_Extractor();               
                case "Fate Extella/Link - pk/pfs/pkb": return new Fate_pk_Extractor();
                case "白色相簿2 - dar archive": return new DarExtractor();
                case "白色相簿2 - pak archive": return new WA2_Pak_Extractor();
                case "PlayStation - TROPHY.TRP file": return new PlayStation_Trp_Extractor();
                case "奥特曼格斗进化3 - bin": return new Ultraman3_bin_Extractor();
                case "混乱特工 - vpp_pc": return new Vpp_pc_Extractor();
                case "捍卫雄鹰2 - gtp archive": return new Gtp_Extractor();
                case "DXBC - DirectX Bytecode": return new DXBC_Extractor();
                case "DXBC2HLSL": return new DXBC2HLSL_Converter();
                case "地雷社 - cat archive": return new CatExtractor();
                case "rad game tools - rada提取器": return new Rada_Extractor();
                case "rad game tools - rada转换器": return new Rada2wav_Converter();
                case "Xbox360 - god2iso打包器": return new Xbox360_iso_packer();
                case "Xbox360 - iso提取器": return new Xbox360_iso_Extractor();
                case "Dreamcast - Bin/Cue转换GDI": return new BinCue2GDI_Converter();
                default: throw new NotSupportedException($"不支持的格式:{formatName}");
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
                    ? $"已选择:{e.Node.Text} (分组)"
                    : $"已选择:{e.Node.Text}";
            }
        }
        private void TreeView1_MouseMove(object? sender, MouseEventArgs e)
        {
            TreeNode? node = treeView1.GetNodeAt(e.Location);
            if (node != null && node.Tag as string != "category")
            {
                if (defaultCategories.ContainsKey(node.Text))
                {
                    string description = defaultCategories[node.Text].description;
                    toolTip1.SetToolTip(treeView1, description);
                }
                else
                {
                    toolTip1.SetToolTip(treeView1, "该工具的具体说明");
                }
            }
            else
            {
                toolTip1.SetToolTip(treeView1, "每个工具都有不同的作用");
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
                !defaultCategories.Values.Select(x => x.category).Contains(treeView1.SelectedNode.Text);
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
            if (selectedNode == null || selectedNode.Parent == null || selectedNode.Tag as string == "category")
                return;

            TreeNode? targetCategory = categoryNodes.ContainsKey(category) ? categoryNodes[category] : null;
            if (targetCategory == null || selectedNode.Parent == targetCategory)
                return;
            selectedNode.Remove();
            targetCategory.Nodes.Add(selectedNode);
            var sortedChildren = targetCategory.Nodes
                .Cast<TreeNode>()
                .OrderBy(node => node.Text)
                .ToList();
            targetCategory.Nodes.Clear();
            targetCategory.Nodes.AddRange(sortedChildren.ToArray());
            var oldParent = selectedNode.Parent;
            if (oldParent != null)
            {
                var oldSortedChildren = oldParent.Nodes
                    .Cast<TreeNode>()
                    .OrderBy(node => node.Text)
                    .ToList();
                oldParent.Nodes.Clear();
                oldParent.Nodes.AddRange(oldSortedChildren.ToArray());
            }
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
                preferences?.Save();
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
                        lblFileCount.Text = $"已提取出:{totalFileCount}个文件";
                    }
                }
                else
                {
                    lblFileCount.Text = $"已提取出:{totalFileCount}个文件";
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
            if (defaultCategories.Values.Select(x => x.category).Contains(selectedNode.Text))
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
                    MessageBox.Show($"分组'{newName}'已存在!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show("无法删除非空分组,请先将其中的提取器移至其他分组!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (defaultCategories.Values.Select(x => x.category).Contains(selectedNode.Text))
            {
                MessageBox.Show("不能删除默认分组!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (MessageBox.Show($"确定要删除分组'{selectedNode.Text}'吗?", "确认删除",
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
        private void BtnAbout_Click(object? sender, EventArgs e)
        {
            try
            {
                using (var aboutForm = new AboutForm())
                {
                    aboutForm.StartPosition = FormStartPosition.CenterParent;
                    aboutForm.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开关于窗口:{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void TxtFolderPath_DragLeave(object? sender, EventArgs e)
        {
            if (txtFolderPath == null) return;

            txtFolderPath.BackColor = SystemColors.Window;
        }       
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
        }
        private void TreeView1_AfterExpand(object? sender, TreeViewEventArgs e)
        {
            if (e.Node != null && e.Node.Tag as string == "category")
            {
                string categoryName = e.Node.Text;
                categoryExpansionState[categoryName] = true;
                preferences.ExpandedCategories[categoryName] = true;
                preferences.Save();
            }
        }

        private void TreeView1_AfterCollapse(object? sender, TreeViewEventArgs e)
        {
            if (e.Node != null && e.Node.Tag as string == "category")
            {
                string categoryName = e.Node.Text;
                categoryExpansionState[categoryName] = false;
                preferences.ExpandedCategories[categoryName] = false;
                preferences.Save();
            }
        }
    }
}
