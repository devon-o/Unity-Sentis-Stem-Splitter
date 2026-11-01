using System;
using UnityEngine;

public sealed class BPMDetector : MonoBehaviour
{
    [SerializeField, Range(40, 180)] private int minBpm = 70;
    [SerializeField, Range(80, 240)] private int maxBpm = 180;

    private const int WindowFrames = 1024;
    private const int HopFrames = 512;

    public int DetectBPM(float[] interleavedSamples)
    {
        float estimate = DetectBpmPrecise(interleavedSamples, AudioUtils.Frequency, AudioUtils.Channels);

        return estimate > 0f ? Mathf.RoundToInt(estimate) : 0;
    }

    public float DetectBpmPrecise(float[] samples, int sampleRate, int channels)
    {
        if (samples == null) 
            throw new ArgumentNullException(nameof(samples));

        if (sampleRate <= 0 || channels <= 0 || samples.Length % channels != 0)
            throw new ArgumentException("Invalid sample rate, channel count or PCM layout.");

        if (minBpm >= maxBpm)
            throw new InvalidOperationException("minBpm must be below maxBpm.");

        int sampleFrames = samples.Length / channels;

        // Fewer than eight seconds is possible, but not very reliable for tempo estimation.
        if (sampleFrames < sampleRate * 8) 
            return 0f;

        int frameCount = 1 + (sampleFrames - WindowFrames) / HopFrames;
        if (frameCount < 3) 
            return 0f;

        // 1. RMS energy of each overlapping analysis window (frames, NOT floats).
        float[] energy = new float[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            int firstSample = frame * HopFrames * channels;
            double squareSum = 0.0;
            int floatCount = WindowFrames * channels;
            for (int i = 0; i < floatCount; i++)
            {
                double sample = samples[firstSample + i];
                squareSum += sample * sample;
            }
            energy[frame] = (float)Math.Sqrt(squareSum / floatCount);
        }

        // 2. Positive energy changes roughly represent drum onsets.
        float[] onsets = new float[frameCount];
        double onsetSum = 0.0;
        for (int i = 1; i < frameCount; i++)
        {
            float rise = Mathf.Max(0f, energy[i] - energy[i - 1]);
            float onset = Mathf.Sqrt(rise); // compress very loud hits
            onsets[i] = onset;
            onsetSum += onset;
        }
        if (onsetSum < 1e-6) 
            return 0f;

        // Subtract the mean so random energy doesn't create a large baseline correlation.
        float onsetMean = (float)(onsetSum / frameCount);
        for (int i = 0; i < frameCount; i++)
            onsets[i] -= onsetMean;

        // 3. Autocorrelation over musical beat delays (and twice those delays).
        double envelopeRate = (double)sampleRate / HopFrames;
        int minLag = Math.Max(1, (int)Math.Ceiling(60.0 * envelopeRate / maxBpm));
        int maxLag = Math.Min(frameCount / 2,
            (int)Math.Floor(60.0 * envelopeRate / minBpm));
        if (maxLag <= minLag) 
            return 0f;

        double[] correlation = new double[2 * maxLag + 2];
        for (int lag = minLag - 1; lag <= 2 * maxLag + 1; lag++)
        {
            if (lag <= 0 || lag >= frameCount) 
                continue;

            double dot = 0.0, leftPower = 0.0, rightPower = 0.0;
            for (int i = lag; i < frameCount; i++)
            {
                double a = onsets[i];
                double b = onsets[i - lag];
                dot += a * b;
                leftPower += a * a;
                rightPower += b * b;
            }
            if (leftPower > 1e-12 && rightPower > 1e-12)
                correlation[lag] = dot / Math.Sqrt(leftPower * rightPower);
        }

        double Score(int lag)
        {
            double direct = Math.Max(0, correlation[lag]);
            double doublePeriod = Math.Max(0, correlation[lag * 2]);

            return Math.Sqrt(direct * doublePeriod);
        }

        int bestLag = 0;
        double bestScore = double.NegativeInfinity;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double score = Score(lag);
            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }

        // A weak periodicity is best reported as unknown rather than as a precise BPM.
        if (bestLag == 0 || bestScore < 0.08) 
            return 0f;

        // 4. Sub-frame interpolation for fractional BPM estimates.
        double refinedLag = bestLag;
        if (bestLag > minLag && bestLag < maxLag)
        {
            double a = Score(bestLag - 1);
            double b = Score(bestLag);
            double c = Score(bestLag + 1);
            double denominator = a - 2.0 * b + c;
            if (Math.Abs(denominator) > 1e-9)
            {
                double offset = 0.5 * (a - c) / denominator;
                refinedLag += Math.Max(-0.5, Math.Min(0.5, offset));
            }
        }

        return (float)(60.0 * envelopeRate / refinedLag);
    }
}
