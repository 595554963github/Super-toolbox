using static LightCodec.Utils.CodecUtils;
using BitReader = LightCodec.Utils.BitReader;
using FFT = LightCodec.Utils.FFT;
using static LightCodec.atrac3plus.Atrac3plusData2;
using Atrac3plusData2 = LightCodec.atrac3plus.Atrac3plusData2;

namespace LightCodec.atrac3plus
{
    public class Atrac3plusDecoder : ILightCodec
    {
        private Context ctx = null!;

        public virtual int init(int bytesPerFrame, int channels, int outputChannels, int codingMode)
        {
            ChannelUnit.init();

            ctx = new Context();
            ctx.outputChannels = outputChannels;
            ctx.dsp = new Atrac3plusDsp();
            for (int i = 0; i < ctx.numChannelBlocks; i++)
            {
                ctx.channelUnits[i] = new ChannelUnit();
                ctx.channelUnits[i].Dsp = ctx.dsp;
            }

            // initialize IPQF
            ctx.ipqfDctCtx = new FFT();
            ctx.ipqfDctCtx.mdctInit(5, true, 31.0 / 32768.9);

            ctx.mdctCtx = new FFT();
            ctx.dsp.initImdct(ctx.mdctCtx);

            Atrac3plusDsp.initWaveSynth();

            ctx.gaincCtx = new Atrac();
            ctx.gaincCtx.initGainCompensation(6, 2);

            return 0;
        }

        public virtual unsafe int decode(void* inputAddr, int inputLength, void* output, out int outputLength)
        {
            outputLength = 0;
            int ret;

            if (ctx == null)
            {
                return AT3P_ERROR;
            }

            if (inputLength < 0)
            {
                return AT3P_ERROR;
            }
            if (inputLength == 0)
            {
                return 0;
            }

            ctx.br = new BitReader(inputAddr, inputLength);
            if (ctx.br.readBool())
            {
                System.Console.WriteLine(string.Format("Invalid start bit"));
                return AT3P_ERROR;
            }

            int chBlock = 0;
            int channelsToProcess = 0;
            while (ctx.br.BitsLeft >= 2)
            {
                int chUnitId = ctx.br.read(2);

                if (chUnitId == CH_UNIT_TERMINATOR)
                {
                    break;
                }
                if (chUnitId == CH_UNIT_EXTENSION)
                {
                    System.Console.WriteLine(string.Format("Non implemented channel unit extension"));
                    return AT3P_ERROR;
                }

                if (chBlock >= ctx.channelUnits.Length)
                {
                    System.Console.WriteLine(string.Format("Too many channel blocks"));
                    return AT3P_ERROR;
                }
                
                if (ctx.channelUnits[chBlock] == null)
                {
                    System.Console.WriteLine(string.Format($"channelUnits[{chBlock}] = NULL!"));
                    return AT3P_ERROR;
                }

                ctx.channelUnits[chBlock].BitReader = ctx.br;

                ctx.channelUnits[chBlock].ctx.unitType = chUnitId;

                channelsToProcess = chUnitId + 1;

                ctx.channelUnits[chBlock].NumChannels = channelsToProcess;

                ret = ctx.channelUnits[chBlock].decode();
                if (ret < 0)
                {
                    return ret;
                }

                ctx.channelUnits[chBlock].decodeResidualSpectrum(ctx.samples);

                ctx.channelUnits[chBlock].reconstructFrame(ctx);

                int sampleBytes = ATRAC3P_FRAME_SAMPLES * ctx.outputChannels * sizeof(short);

                writeOutput(ctx.outpBuf, (short*)output + outputLength / sizeof(short), ATRAC3P_FRAME_SAMPLES, channelsToProcess, ctx.outputChannels);

                outputLength += sampleBytes;

                chBlock++;
            }

            //System.Console.WriteLine(string.Format("Bytes read 0x{0:X}", ctx.br.BytesRead));

            return ctx.br.BytesRead;
        }

        public virtual int NumberOfSamples
        {
            get
            {
                return ATRAC3P_FRAME_SAMPLES;
            }
        }
    }

}