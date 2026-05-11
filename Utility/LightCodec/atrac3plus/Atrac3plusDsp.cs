using System;
using static System.Math;
using LightCodec.Utils;
using static LightCodec.atrac3plus.Atrac3plusDecoder;
using static LightCodec.atrac3plus.ChannelUnitContext;
using FFT = LightCodec.Utils.FFT;
using SineWin = LightCodec.Utils.SineWin;
using static LightCodec.atrac3plus.Atrac3plusData2;
using Atrac3plusData2 = LightCodec.atrac3plus.Atrac3plusData2;

namespace LightCodec.atrac3plus
{
    public class Atrac3plusDsp
    {
        private static readonly int ATRAC3P_MDCT_SIZE = ATRAC3P_SUBBAND_SAMPLES * 2;
        private static readonly float[] sine_table = new float[2048]; //< wave table
        private static readonly float[] hann_window = new float[256]; //< Hann windowing function
        private static readonly float[] amp_sf_tab = new float[64]; //< scalefactors for quantized amplitudes
        private static readonly double TWOPI = 2 * PI;

        private static int DEQUANT_PHASE(int ph)
        {
            return (ph & 0x1F) << 6;
        }

        public virtual void initImdct(FFT mdctCtx)
        {
            SineWin.initFfSineWindows();

            // Initialize the MDCT transform
            mdctCtx.mdctInit(8, true, -1.0);
        }

        public static void initWaveSynth()
        {
            // generate sine wave table
            for (int i = 0; i < 2048; i++)
            {
                sine_table[i] = (float)Sin(TWOPI * i / 2048);
            }

            // generate Hann window
            for (int i = 0; i < 256; i++)
            {
                hann_window[i] = (float)((1.0 - Cos(TWOPI * i / 256)) * 0.5);
            }

            // generate amplitude scalefactors table
            for (int i = 0; i < 64; i++)
            {
                amp_sf_tab[i] = (float)Pow(2.0, ((double)(i - 3)) / 4.0);
            }
        }

        public virtual void powerCompensation(ChannelUnitContext ctx, int chIndex, float[] sp, int rngIndex, int sb)
        {
            float[] pwcsp = new float[ATRAC3P_SUBBAND_SAMPLES];
            int gcv = 0;
            int swapCh = (ctx.unitType == CH_UNIT_STEREO && ctx.swapChannels[sb] ? 1 : 0);

            if (ctx.channels[chIndex ^ swapCh].powerLevs[subband_to_powgrp[sb]] == ATRAC3P_POWER_COMP_OFF)
            {
                return;
            }

            // generate initial noise spectrum
            for (int i = 0; i < ATRAC3P_SUBBAND_SAMPLES; i++, rngIndex++)
            {
                pwcsp[i] = noise_tab[rngIndex & 0x3FF];
            }

            // check gain control information
            AtracGainInfo g1 = ctx.channels[chIndex ^ swapCh].gainData[sb];
            AtracGainInfo g2 = ctx.channels[chIndex ^ swapCh].gainDataPrev[sb];

            int gainLev = (g1.numPoints > 0 ? (6 - g1.levCode[0]) : 0);

            for (int i = 0; i < g2.numPoints; i++)
            {
                gcv = Max(gcv, gainLev - (g2.levCode[i] - 6));
            }

            for (int i = 0; i < g1.numPoints; i++)
            {
                gcv = Max(gcv, 6 - g1.levCode[i]);
            }

            float grpLev = pwc_levs[ctx.channels[chIndex ^ swapCh].powerLevs[subband_to_powgrp[sb]]] / (1 << gcv);

            // skip the lowest two quant units (frequencies 0...351 Hz) for subband 0
            for (int qu = subband_to_qu[sb] + (sb == 0 ? 2 : 0); qu < subband_to_qu[sb + 1]; qu++)
            {
                if (ctx.channels[chIndex].quWordlen[qu] <= 0)
                {
                    continue;
                }

                float quLev = ff_atrac3p_sf_tab[ctx.channels[chIndex].quSfIdx[qu]] * ff_atrac3p_mant_tab[ctx.channels[chIndex].quWordlen[qu]] / (1 << ctx.channels[chIndex].quWordlen[qu]) * grpLev;

                int dst = ff_atrac3p_qu_to_spec_pos[qu];
                int nsp = ff_atrac3p_qu_to_spec_pos[qu + 1] - ff_atrac3p_qu_to_spec_pos[qu];

                for (int i = 0; i < nsp; i++)
                {
                    sp[dst + i] += pwcsp[i] * quLev;
                }
            }
        }

        public virtual void imdct(FFT mdctCtx, float[] @in, int inOffset, float[] @out, int outOffset, int windId, int sb)
        {
            if (mdctCtx == null) return;
            if ((sb & 1) != 0)
            {
                for (int i = 0; i < ATRAC3P_SUBBAND_SAMPLES / 2; i++)
                {
                    float tmp = @in[inOffset + i];
                    @in[inOffset + i] = @in[inOffset + ATRAC3P_SUBBAND_SAMPLES - 1 - i];
                    @in[inOffset + ATRAC3P_SUBBAND_SAMPLES - 1 - i] = tmp;
                }
            }

            mdctCtx.imdctCalc(@out, outOffset, @in, inOffset);

            /* Perform windowing on the output.
             * ATRAC3+ uses two different MDCT windows:
             * - The first one is just the plain sine window of size 256
             * - The 2nd one is the plain sine window of size 128
             *   wrapped into zero (at the start) and one (at the end) regions.
             *   Both regions are 32 samples long. */
            if ((windId & 2) != 0)
            { // 1st half: steep window
                Arrays.Fill(@out, outOffset, outOffset + 32, 0f);
                FloatDSP.vectorFmul(@out, outOffset + 32, @out, outOffset + 32, SineWin.ff_sine_64, 0, 64);
            }
            else
            { // 1st halt: simple sine window
                FloatDSP.vectorFmul(@out, outOffset, @out, outOffset, SineWin.ff_sine_128, 0, ATRAC3P_MDCT_SIZE / 2);
            }

            if ((windId & 1) != 0)
            { // 2nd half: steep window
                FloatDSP.vectorFmulReverse(@out, outOffset + 160, @out, outOffset + 160, SineWin.ff_sine_64, 0, 64);
                Arrays.Fill(@out, outOffset + 224, outOffset + 224 + 32, 0f);
            }
            else
            { // 2nd half: simple sine window
                FloatDSP.vectorFmulReverse(@out, outOffset + 128, @out, outOffset + 128, SineWin.ff_sine_128, 0, ATRAC3P_MDCT_SIZE / 2);
            }
        }

        /// <summary>
        ///  Synthesize sine waves according to given parameters.
        /// 
        ///  @param[in]    synthParam   common synthesis parameters
        ///  @param[in]    wavesInfo    parameters for each sine wave
        ///  @param[in]    envelope     envelope data for all waves in a group
        ///  @param[in]    phaseShift   flag indicates 180 degrees phase shift
        ///  @param[in]    regOffset    region offset for trimming envelope data
        ///  @param[out]   out          receives synthesized data
        /// </summary>
        private void wavesSynth(WaveSynthParams synthParams, WavesData wavesInfo, WaveEnvelope envelope, bool phaseShift, int regOffset, float[] @out)
        {
            int waveParam = wavesInfo.startIndex;

            for (int wn = 0; wn < wavesInfo.numWavs; wn++, waveParam++)
            {
                // amplitude dequantization
                double amp = amp_sf_tab[synthParams.waves[waveParam].ampSf] * (synthParams.amplitudeMode == 0 ? (synthParams.waves[waveParam].ampIndex + 1) / 15.13f : 1.0f);

                int inc = synthParams.waves[waveParam].freqIndex;
                int pos = (DEQUANT_PHASE(synthParams.waves[waveParam].phaseIndex) - (regOffset ^ 128) * inc + 2048) & 2047;

                // waveform generation
                for (int i = 0; i < 128; i++)
                {
                    @out[i] += (float)(sine_table[pos] * amp);
                    pos = (pos + inc) & 2047;
                }
            }

            if (phaseShift)
            {
                // 180 degrees phase shift
                for (int i = 0; i < 128; i++)
                {
                    @out[i] = -@out[i];
                }
            }

            // fade in with steep Hann window if requested
            if (envelope.hasStartPoint)
            {
                int pos = (envelope.startPos << 2) - regOffset;
                if (pos > 0 && pos <= 128)
                {
                    Arrays.Fill(@out, 0, pos, 0f);
                    if (!envelope.hasStopPoint || envelope.startPos != envelope.stopPos)
                    {
                        @out[pos + 0] *= hann_window[0];
                        @out[pos + 1] *= hann_window[32];
                        @out[pos + 2] *= hann_window[64];
                        @out[pos + 3] *= hann_window[96];
                    }
                }
            }

            // fade out with steep Hann window if requested
            if (envelope.hasStopPoint)
            {
                int pos = (envelope.stopPos + 1 << 2) - regOffset;
                if (pos > 0 && pos <= 128)
                {
                    @out[pos - 4] *= hann_window[96];
                    @out[pos - 3] *= hann_window[64];
                    @out[pos - 2] *= hann_window[32];
                    @out[pos - 1] *= hann_window[0];
                    Arrays.Fill(@out, pos, 128, 0f);
                }
            }
        }

        public virtual void generateTones(ChannelUnitContext ctx, int chNum, int sb, float[] @out, int outOffset)
        {
            float[] wavreg1 = new float[128];
            float[] wavreg2 = new float[128];
            WavesData tonesNow = ctx.channels[chNum].tonesInfoPrev[sb];
            WavesData tonesNext = ctx.channels[chNum].tonesInfo[sb];

            // reconstruct full envelopes for both overlapping regions
            // from truncated bitstream data
            if (tonesNext.pendEnv.hasStartPoint && tonesNext.pendEnv.startPos < tonesNext.pendEnv.stopPos)
            {
                tonesNext.currEnv.hasStartPoint = true;
                tonesNext.currEnv.startPos = tonesNext.pendEnv.startPos + 32;
            }
            else if (tonesNow.pendEnv.hasStartPoint)
            {
                tonesNext.currEnv.hasStartPoint = true;
                tonesNext.currEnv.startPos = tonesNow.pendEnv.startPos;
            }
            else
            {
                tonesNext.currEnv.hasStartPoint = false;
                tonesNext.currEnv.startPos = 0;
            }

            if (tonesNow.pendEnv.hasStopPoint && tonesNow.pendEnv.stopPos >= tonesNext.currEnv.startPos)
            {
                tonesNext.currEnv.hasStopPoint = true;
                tonesNext.currEnv.stopPos = tonesNow.pendEnv.stopPos;
            }
            else if (tonesNext.pendEnv.hasStopPoint)
            {
                tonesNext.currEnv.hasStopPoint = true;
                tonesNext.currEnv.stopPos = tonesNext.pendEnv.stopPos + 32;
            }
            else
            {
                tonesNext.currEnv.hasStopPoint = false;
                tonesNext.currEnv.stopPos = 64;
            }

            // is the visible part of the envelope non-zero?
            bool reg1EnvNonzero = (tonesNow.currEnv.stopPos < 32 ? false : true);
            bool reg2EnvNonzero = (tonesNext.currEnv.startPos >= 32 ? false : true);

            // synthesize waves for both overlapping regions
            if (tonesNow.numWavs > 0 && reg1EnvNonzero)
            {
                int ch = ctx.wavesInfoPrev.toneMaster[sb] ? (1 - chNum) : chNum;
                bool phaseInv = ctx.wavesInfoPrev.phaseShift[sb] && ((ch & 1) != 0);
                wavesSynth(ctx.wavesInfoPrev, tonesNow, tonesNow.currEnv, phaseInv, 128, wavreg1);
            }

            if (tonesNext.numWavs > 0 && reg2EnvNonzero)
            {
                int ch = ctx.wavesInfo.toneMaster[sb] ? (1 - chNum) : chNum;
                bool phaseInv = ctx.wavesInfo.phaseShift[sb] && ((ch & 1) != 0);
                wavesSynth(ctx.wavesInfo, tonesNext, tonesNext.currEnv, phaseInv, 0, wavreg2);
            }

            // Hann windowing for non-faded wave signals
            if (tonesNow.numWavs > 0 && tonesNext.numWavs > 0 && reg1EnvNonzero && reg2EnvNonzero)
            {
                FloatDSP.vectorFmul(wavreg1, 0, wavreg1, 0, hann_window, 128, 128);
                FloatDSP.vectorFmul(wavreg2, 0, wavreg2, 0, hann_window, 0, 128);
            }
            else
            {
                if (tonesNow.numWavs > 0 && !tonesNow.currEnv.hasStopPoint)
                {
                    FloatDSP.vectorFmul(wavreg1, 0, wavreg1, 0, hann_window, 128, 128);
                }
                if (tonesNext.numWavs > 0 && !tonesNext.currEnv.hasStartPoint)
                {
                    FloatDSP.vectorFmul(wavreg2, 0, wavreg2, 0, hann_window, 0, 128);
                }
            }

            // Overlap and add to residual
            for (int i = 0; i < 128; i++)
            {
                @out[outOffset + i] += wavreg1[i] + wavreg2[i];
            }
        }

        public virtual void ipqf(FFT dctCtx, IPQFChannelContext hist, float[] @in, float[] @out)
        {
            float[] idctIn = new float[ATRAC3P_SUBBANDS];
            float[] idctOut = new float[ATRAC3P_SUBBANDS];

            Arrays.Fill(@out, 0, ATRAC3P_FRAME_SAMPLES, 0f);

            for (int s = 0; s < ATRAC3P_SUBBAND_SAMPLES; s++)
            {
                // pack up one sample from each subband
                for (int sb = 0; sb < ATRAC3P_SUBBANDS; sb++)
                {
                    idctIn[sb] = @in[sb * ATRAC3P_SUBBAND_SAMPLES + s];
                }

                // Calculate the sine and cosine part of the PQF using IDCT-IV
                dctCtx.imdctHalf(idctOut, 0, idctIn, 0);

                // append the result to the history
                for (int i = 0; i < 8; i++)
                {
                    hist.buf1[hist.pos][i] = idctOut[i + 8];
                    hist.buf2[hist.pos][i] = idctOut[7 - i];
                }

                int posNow = hist.pos;
                int posNext = mod23_lut[posNow + 2]; // posNext = (posNow + 1) % 23

                for (int t = 0; t < ATRAC3P_PQF_FIR_LEN; t++)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        @out[s * 16 + i + 0] += hist.buf1[posNow][i] * ipqf_coeffs1[t][i] + hist.buf2[posNext][i] * ipqf_coeffs2[t][i];
                        @out[s * 16 + i + 8] += hist.buf1[posNow][7 - i] * ipqf_coeffs1[t][i + 8] + hist.buf2[posNext][7 - i] * ipqf_coeffs2[t][i + 8];
                    }

                    posNow = mod23_lut[posNext + 2]; // posNow = (posNow + 2) % 23;
                    posNext = mod23_lut[posNow + 2]; // posNext = (posNext + 2) % 23;
                }

                hist.pos = mod23_lut[hist.pos]; // hist.pos = (hist.pos - 1) % 23;
            }
        }
    }

}