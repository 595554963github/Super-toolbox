using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Apex2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private const int APEX_HEADER_SIZE = 514;

        private async Task<byte[]> ExtractMonoWavFromApexAsync(string apexFilePath, CancellationToken cancellationToken)
        {
            byte[] apexData = await File.ReadAllBytesAsync(apexFilePath, cancellationToken);

            if (apexData.Length < APEX_HEADER_SIZE)
            {
                throw new InvalidDataException("无效的APEX文件:文件太小");
            }

            byte[] wavData = new byte[apexData.Length - APEX_HEADER_SIZE];
            Array.Copy(apexData, APEX_HEADER_SIZE, wavData, 0, wavData.Length);

            return wavData;
        }

        private async Task<byte[]> ConvertToStereoWavAsync(byte[] monoWavData, CancellationToken cancellationToken)
        {
            string tempMonoPath = Path.GetTempFileName() + ".wav";

            try
            {
                await File.WriteAllBytesAsync(tempMonoPath, monoWavData, cancellationToken);

                var wavReader = new WaveReader();
                AudioData audioData;

                using (var fs = File.OpenRead(tempMonoPath))
                {
                    audioData = wavReader.Read(fs);
                }

                if (audioData == null)
                {
                    throw new InvalidDataException("无法读取WAV音频数据");
                }

                var pcm16 = audioData.GetFormat<Pcm16Format>();
                if (pcm16 == null)
                {
                    throw new InvalidDataException("APEX内嵌的WAV不是PCM16格式");
                }

                short[][] monoChannels = pcm16.Channels;
                if (monoChannels == null || monoChannels.Length == 0)
                {
                    throw new InvalidDataException("无法获取PCM16通道数据");
                }

                short[] mono = monoChannels[0];
                int sampleRate = pcm16.SampleRate;
                short[][] stereo = new short[2][];
                stereo[0] = mono;
                stereo[1] = mono;

                var stereoFormat = new Pcm16Format(stereo, sampleRate);
                var wavWriter = new WaveWriter();

                using (var ms = new MemoryStream())
                {
                    wavWriter.WriteToStream(stereoFormat, ms);
                    return ms.ToArray();
                }
            }
            finally
            {
                try { File.Delete(tempMonoPath); } catch { }
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var apexFiles = Directory.GetFiles(directoryPath, "*.apex", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"_(\d+)$");
                    return match.Success && int.TryParse(match.Groups[1].Value, out int num) ? num : int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = apexFiles.Length;
            int successCount = 0;

            if (apexFiles.Length == 0)
            {
                ConversionError?.Invoke(this, "未找到需要转换的APEX文件");
                OnConversionFailed("未找到需要转换的APEX文件");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个APEX文件");

            foreach (var apexFilePath in apexFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = Path.GetFileNameWithoutExtension(apexFilePath);
                ConversionProgress?.Invoke(this, $"正在转换:{fileName}.apex");

                string fileDirectory = Path.GetDirectoryName(apexFilePath) ?? string.Empty;
                string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                try
                {
                    byte[] monoWavData = await ExtractMonoWavFromApexAsync(apexFilePath, cancellationToken);
                    byte[] stereoWavData = await ConvertToStereoWavAsync(monoWavData, cancellationToken);

                    await File.WriteAllBytesAsync(wavFilePath, stereoWavData, cancellationToken);

                    successCount++;
                    ConversionProgress?.Invoke(this, $"已转换:{fileName}.wav");
                    OnFileConverted(wavFilePath);
                }
                catch (Exception ex)
                {
                    ConversionError?.Invoke(this, $"{fileName}.apex转换失败:{ex.Message}");
                    OnConversionFailed($"{fileName}.apex转换失败");
                }
            }

            ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
            OnConversionCompleted();
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}