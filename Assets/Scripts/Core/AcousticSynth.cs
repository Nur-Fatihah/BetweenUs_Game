using System;

namespace Crony
{
    /// <summary>
    /// Pure-math acoustic instrument models (no Unity API, safe on worker threads). Plucked strings use the
    /// Karplus-Strong algorithm; piano uses slightly stretched partials with a felt-hammer transient; the flute is
    /// a breathy near-sine with vibrato; bells and glockenspiel use inharmonic partials. Every write can wrap around
    /// the buffer so decaying tails flow seamlessly into the start of a loop.
    /// </summary>
    public static class AcousticSynth
    {
        public const int Rate = 22050;
        const float TwoPi = (float)(Math.PI * 2);

        public static float Hz(int midi) => 440f * (float)Math.Pow(2, (midi - 69) / 12.0);
        public static float[] Buffer(float seconds) => new float[(int)(seconds * Rate)];

        static void Add(float[] buffer, int index, float value, bool wrap)
        {
            if (index < 0) return;
            if (index >= buffer.Length) { if (!wrap) return; index %= buffer.Length; }
            buffer[index] += value;
        }

        static float Fade(int i, int length) => Math.Min(1f, (length - i) / (Rate * .03f));

        /// <summary>Guitar, pizzicato strings and cello plucks. Lower decay = shorter note; softness rounds the attack.</summary>
        public static void Pluck(float[] buffer, float startSeconds, float hz, float seconds, float amp, Random random, float decay = .996f, int softness = 2, bool wrap = false)
        {
            int period = Math.Max(2, (int)(Rate / hz));
            float[] ring = new float[period];
            for (int i = 0; i < period; i++) ring[i] = (float)(random.NextDouble() * 2 - 1);
            for (int pass = 0; pass < softness; pass++)
                for (int i = 0; i < period; i++) ring[i] = (ring[i] + ring[(i + 1) % period]) * .5f;
            int start = (int)(startSeconds * Rate), length = (int)(seconds * Rate), index = 0;
            for (int i = 0; i < length; i++)
            {
                float value = ring[index];
                int next = (index + 1) % period;
                ring[index] = decay * .5f * (value + ring[next]);
                index = next;
                Add(buffer, start + i, value * amp * Fade(i, length), wrap);
            }
        }

        public static void Piano(float[] buffer, float startSeconds, float hz, float seconds, float amp, Random random, bool wrap = false)
        {
            int start = (int)(startSeconds * Rate), length = (int)(seconds * Rate);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate, value = 0f;
                for (int k = 1; k <= 6; k++)
                {
                    float partial = k * hz * (float)Math.Sqrt(1 + .0003f * k * k);
                    value += (float)(Math.Sin(TwoPi * partial * t) * Math.Exp(-t * (1.1f + k * .7f)) / Math.Pow(k, 1.4));
                }
                value += (float)((random.NextDouble() * 2 - 1) * Math.Exp(-t * 300)) * .12f;
                Add(buffer, start + i, value * amp * Math.Min(1f, t * 300f) * Fade(i, length), wrap);
            }
        }

        public static void Flute(float[] buffer, float startSeconds, float hz, float seconds, float amp, Random random, bool wrap = false)
        {
            int start = (int)(startSeconds * Rate), length = (int)(seconds * Rate);
            float breath = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float pitch = hz * (1f + .004f * (float)Math.Sin(TwoPi * 5f * t) * Math.Min(1f, t * 2f));
                float tone = (float)(Math.Sin(TwoPi * pitch * t) + .3 * Math.Sin(TwoPi * pitch * 2 * t) + .08 * Math.Sin(TwoPi * pitch * 3 * t));
                breath += ((float)(random.NextDouble() * 2 - 1) - breath) * .2f;
                float envelope = Math.Min(1f, t / .07f) * Math.Min(1f, (length - i) / (Rate * .12f));
                Add(buffer, start + i, (tone * .8f + breath * .35f) * amp * envelope, wrap);
            }
        }

        /// <summary>Glockenspiel / wind chime: bright inharmonic partials that ring and fade.</summary>
        public static void Chime(float[] buffer, float startSeconds, float hz, float seconds, float amp, bool wrap = false)
        {
            int start = (int)(startSeconds * Rate), length = (int)(seconds * Rate);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float value = (float)(Math.Sin(TwoPi * hz * t) * Math.Exp(-t * 3) + .45 * Math.Sin(TwoPi * hz * 2.76f * t) * Math.Exp(-t * 6) + .2 * Math.Sin(TwoPi * hz * 5.4f * t) * Math.Exp(-t * 11));
                Add(buffer, start + i, value * amp * Math.Min(1f, t * 600f) * Fade(i, length), wrap);
            }
        }

        /// <summary>Hand bell: a warm strike with a long hum.</summary>
        public static void Bell(float[] buffer, float startSeconds, float hz, float seconds, float amp, bool wrap = false)
        {
            int start = (int)(startSeconds * Rate), length = (int)(seconds * Rate);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float value = (float)(Math.Sin(TwoPi * hz * t) * Math.Exp(-t * 1.6) + .5 * Math.Sin(TwoPi * hz * 2f * t) * Math.Exp(-t * 2.5) +
                                      .3 * Math.Sin(TwoPi * hz * 2.4f * t) * Math.Exp(-t * 3) + .5 * Math.Sin(TwoPi * hz * .5f * t) * Math.Exp(-t * 1.2));
                Add(buffer, start + i, value * amp * Math.Min(1f, t * 500f) * Fade(i, length), wrap);
            }
        }

        /// <summary>Hollow wooden knock: two resonant modes and a tiny click. Used for clicks, stamps and bridge steps.</summary>
        public static void Wood(float[] buffer, float startSeconds, float hz, float amp, Random random, float damping = 60f)
        {
            int start = (int)(startSeconds * Rate), length = (int)(Rate * Math.Min(.25f, 6f / damping));
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float value = (float)((Math.Sin(TwoPi * hz * t) + .55 * Math.Sin(TwoPi * hz * 2.42f * t)) * Math.Exp(-t * damping));
                value += (float)((random.NextDouble() * 2 - 1) * Math.Exp(-t * 900)) * .5f;
                Add(buffer, start + i, value * amp * Fade(i, length), false);
            }
        }

        /// <summary>Filtered noise: rustles, crunches, splashes, whooshes and parchment.</summary>
        public static void Noise(float[] buffer, float startSeconds, float seconds, float amp, Random random, float lowpass, float highpass, Func<float, float> envelope)
        {
            int start = (int)(startSeconds * Rate), length = (int)(seconds * Rate);
            float lowState = 0f, highState = 0f, lastLow = 0f;
            float a = 1f - (float)Math.Exp(-TwoPi * lowpass / Rate), b = (float)Math.Exp(-TwoPi * highpass / Rate);
            for (int i = 0; i < length; i++)
            {
                lowState += ((float)(random.NextDouble() * 2 - 1) - lowState) * a;
                highState = b * (highState + lowState - lastLow);
                lastLow = lowState;
                Add(buffer, start + i, highState * amp * envelope(i / (float)length), false);
            }
        }

        /// <summary>Small Schroeder room reverb, run twice over the loop so the tail wraps seamlessly.</summary>
        public static void Room(float[] buffer, float wet)
        {
            int[] combs = { 1116, 1188, 1277, 1356 };
            float[][] lines = new float[combs.Length][];
            int[] heads = new int[combs.Length];
            for (int c = 0; c < combs.Length; c++) lines[c] = new float[combs[c] * Rate / 44100];
            int[] passes = { 556 * Rate / 44100, 441 * Rate / 44100 };
            float[][] allpass = { new float[passes[0]], new float[passes[1]] };
            int[] apHeads = new int[2];
            float[] output = new float[buffer.Length];
            for (int round = 0; round < 2; round++)
                for (int i = 0; i < buffer.Length; i++)
                {
                    float input = buffer[i], sum = 0f;
                    for (int c = 0; c < combs.Length; c++)
                    {
                        float[] line = lines[c];
                        float delayed = line[heads[c]];
                        line[heads[c]] = input + delayed * .78f;
                        heads[c] = (heads[c] + 1) % line.Length;
                        sum += delayed;
                    }
                    sum *= .25f;
                    for (int p = 0; p < 2; p++)
                    {
                        float[] line = allpass[p];
                        float delayed = line[apHeads[p]];
                        line[apHeads[p]] = sum + delayed * .5f;
                        apHeads[p] = (apHeads[p] + 1) % line.Length;
                        sum = delayed - sum * .5f;
                    }
                    if (round == 1) output[i] = input + sum * wet;
                }
            Array.Copy(output, buffer, buffer.Length);
        }

        /// <summary>Normalise to a peak, then soft-saturate so stacked notes never clip.</summary>
        public static void Master(float[] buffer, float peak)
        {
            float max = 1e-4f;
            foreach (float v in buffer) max = Math.Max(max, Math.Abs(v));
            float gain = peak / max, norm = (float)Math.Tanh(1.3);
            for (int i = 0; i < buffer.Length; i++) buffer[i] = (float)Math.Tanh(buffer[i] * gain * 1.3) / norm * peak;
        }
    }
}
