# Super-toolbox

超级工具箱，一个专门用于解游戏文件的工具，RIFX只有一种大端序的wem,RIFF可就多了，常见的wav、at3、at9、xma、小端序的wem、webp、bank、xwma、xa，不包括avi、str，因为这两种属于视频文件，根本不需要

这么做，视频用ffmpeg转换就行了，除了RIFX和RIFF系列，还有fsb、ogg、hca、adx、ahx、png、jpg等格式支持提取，每一种格式都专门写进了了它们专属的extractor.cs，将来可能会支持更多的格式，先留着吧，这

个项目可以使各位解包游戏更方便，并且可以把类库集成到其他的项目中，作为插件使用，目前已集成到了assetstudio，大家可以下载试试
