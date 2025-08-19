# Super-toolbox

超级工具箱，一个专门用于解游戏文件的工具，RIFX只有一种大端序的wem,RIFF可就多了，常见的wav、at3、at9、xma、小端序的wem、webp、bank、xwma、xa，不包括avi、str，因为这两种属于视频文件，根本不需要

这么做，视频用ffmpeg转换就行了，除了RIFX和RIFF系列，还有fsb、ogg、hca、adx、ahx、png、jpg等格式支持提取，每一种格式都专门写进了了它们专属的extractor.cs，将来可能会支持更多的格式，先留着吧，这

个项目可以使各位解包游戏更方便，并且可以把类库集成到其他的项目中，作为插件使用，目前已集成到了assetstudio，大家可以下载试试

感谢thesupersonic16制作的DALTools，可以解开switch平台和steam平台的超女神信仰诺瓦露，我下载源码将其生成dll引用到我的项目内，省得自己重头写代码解pck和tex文件了。

8月17日后新增zlib、gzip、lzss、lz4、zstd、yaz0、brotli和7-zip的sdk的lzma共8种压缩和解压缩方法

8月19日创建一个新的Packer文件夹，里面放置各种打包器，然后就是程序的界面里面没写提取器和打包器的默认为提取器，举个例子：3ds - darc提取器和3ds - darc打包器这种特意备注的，那它就是分打包器和解包器，

而逆战 - pak，后面没写打包器还是提取器的，那它默认就是一个提取器。
