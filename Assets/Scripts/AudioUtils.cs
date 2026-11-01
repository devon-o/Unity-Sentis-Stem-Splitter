using System;
using UnityEngine;

public static class AudioUtils
{
    public const int SamplesPerChannel = 343980;
    public const int Frequency = 44100;
    public const int Channels = 2;
    public const int StemCount = 4;

    // The models accept 7.8-second chunks at 44.1 kHz.
    // Move forward by 5.85 seconds each time, giving 25% overlap.
    public const int OverlapFrames = SamplesPerChannel / 4;
    public const int StrideFrames = SamplesPerChannel - OverlapFrames;

    public static int GetChunkCount(int totalFrames)
    {
        if (totalFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalFrames));

        if (totalFrames <= SamplesPerChannel)
            return 1;

        // Avoid extra, unnecessary chunks when the final full chunk
        // already reaches the end of the track.
        return 1 + (int)((totalFrames - (long)SamplesPerChannel + StrideFrames - 1) / StrideFrames);
    }

    public static float[] GetSentisChunk(AudioClip clip, int startFrame)
    {
        if (clip == null)
            throw new ArgumentNullException(nameof(clip));

        if (clip.frequency != Frequency || clip.channels != Channels)
            throw new InvalidOperationException("Audio must be 44.1 kHz stereo.");

        if (startFrame < 0 || startFrame >= clip.samples)
            throw new ArgumentOutOfRangeException(nameof(startFrame));

        int framesToRead = Math.Min(SamplesPerChannel, clip.samples - startFrame);
        float[] interleaved = new float[framesToRead * Channels];

        if (!clip.GetData(interleaved, startFrame))
            throw new InvalidOperationException("Could not read decoded AudioClip samples.");

        // Sentis: [1, 2, 343980], with the full left channel
        // followed by the full right channel. Remaining frames are zero-padded.
        float[] input = new float[SamplesPerChannel * Channels];
        for (int i = 0; i < framesToRead; i++)
        {
            input[i] = interleaved[i * 2];
            input[i + SamplesPerChannel] = interleaved[i * 2 + 1];
        }
        return input;
    }

    // A linear crossfade. Fade only where there is an adjacent chunk:
    // no fade-in at the beginning of a song or fade-out at its end.
    private static float GetChunkWeight(int localFrame, int startFrame, int totalFrames)
    {
        float weight = 1f;
        bool hasPrevious = startFrame > 0;
        bool hasNext = startFrame + SamplesPerChannel < totalFrames;

        if (hasPrevious && localFrame < OverlapFrames)
            weight *= localFrame / (float)OverlapFrames;

        if (hasNext && localFrame >= StrideFrames)
            weight *= (SamplesPerChannel - localFrame) / (float)OverlapFrames;

        return weight;
    }

    public static float[] BuildOverlapWeights(int totalFrames)
    {
        int chunkCount = GetChunkCount(totalFrames);
        float[] weights = new float[totalFrames];

        for (int chunk = 0; chunk < chunkCount; chunk++)
        {
            int startFrame = chunk * StrideFrames;
            int validFrames = Math.Min(SamplesPerChannel, totalFrames - startFrame);

            for (int i = 0; i < validFrames; i++)
                weights[startFrame + i] += GetChunkWeight(i, startFrame, totalFrames);
        }

        return weights;
    }

    // Add one chosen row of [1, 4, 2, 343980] into an interleaved,
    // full-song stereo destination. Works for both base and FT models.
    public static void AccumulateStem(float[] destination, float[] rawOutput, int stemIndex, int startFrame, int totalFrames)
    {
        ValidateOutput(rawOutput, stemIndex);

        if (destination == null || destination.Length != totalFrames * Channels)
            throw new ArgumentException("Destination must be full-length interleaved stereo.", nameof(destination));

        int validFrames = Math.Min(SamplesPerChannel, totalFrames - startFrame);
        int stemOffset = stemIndex * Channels * SamplesPerChannel;

        for (int i = 0; i < validFrames; i++)
        {
            float weight = GetChunkWeight(i, startFrame, totalFrames);
            int destinationOffset = (startFrame + i) * Channels;

            destination[destinationOffset] += rawOutput[stemOffset + i] * weight;
            destination[destinationOffset + 1] += rawOutput[stemOffset + SamplesPerChannel + i] * weight;
        }
    }

    public static void NormalizeStem(float[] destination, float[] weights)
    {
        if (destination == null || weights == null || destination.Length != weights.Length * Channels)
            throw new ArgumentException("Invalid audio/weights lengths.");

        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0f)
                throw new InvalidOperationException($"No chunk covers frame {i}.");

            int offset = i * Channels;
            destination[offset] /= weights[i];
            destination[offset + 1] /= weights[i];
        }
    }

    private static void ValidateOutput(float[] output, int stemIndex)
    {
        if (stemIndex < 0 || stemIndex >= StemCount)
            throw new ArgumentOutOfRangeException(nameof(stemIndex));

        if (output == null || output.Length != StemCount * Channels * SamplesPerChannel)
            throw new ArgumentException("Invalid ONNX output length: expected [1,4,2,343980].", nameof(output));
    }
}
