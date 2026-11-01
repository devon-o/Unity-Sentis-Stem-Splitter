using System;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using UnityEngine;

public class StemSplitter : MonoBehaviour
{
    [Header("Standard model")]
    [SerializeField] private ModelAsset modelAsset;

    [Header("Fine-tuned specialist models")]
    [SerializeField] private ModelAsset FtDrumModelAsset;
    [SerializeField] private ModelAsset FtVocalsModelAsset;
    [SerializeField] private ModelAsset FtBassModelAsset;
    [SerializeField] private ModelAsset FtOtherModelAsset;

    [Header("Inference")]
    [SerializeField] private BackendType backend = BackendType.CPU;

    // Keep the standard worker for repeated separations; load FT models
    // one at a time to avoid holding all four huge workers in memory.
    private Worker baseWorker;

    private Worker GetBaseWorker()
    {
        if (baseWorker == null)
        {
            if (modelAsset == null)
                throw new InvalidOperationException("Standard model asset is not assigned.");

            baseWorker = new Worker(ModelLoader.Load(modelAsset), backend);
        }
        return baseWorker;
    }

    private ModelAsset GetFtModelAsset(int stemIndex)
    {
        return stemIndex switch
        {
            0 => FtDrumModelAsset,
            1 => FtBassModelAsset,
            2 => FtOtherModelAsset,
            3 => FtVocalsModelAsset,
            _ => throw new ArgumentOutOfRangeException(nameof(stemIndex))
        };
    }

    private async Task<float[]> RunChunkAsync(Worker worker, float[] audio)
    {
        if (audio == null || audio.Length != AudioUtils.SamplesPerChannel * AudioUtils.Channels)
            throw new ArgumentException("Expected stereo, channel-first 7.8-second chunk.", nameof(audio));

        using var input = new Tensor<float>(new TensorShape(1, AudioUtils.Channels, AudioUtils.SamplesPerChannel), audio);

        worker.Schedule(input);
        var output = worker.PeekOutput("stems") as Tensor<float>
            ?? throw new InvalidOperationException("Model output 'stems' was not a float tensor.");

        using var result = await output.ReadbackAndCloneAsync();
        return result.DownloadToArray();
    }

    // Returns [drums, bass, other, vocals]. Each array is interleaved stereo
    // and has exactly clip.samples * 2 floats, regardless of clip duration.
    public async Task<float[][]> SeparateAsync(AudioClip clip, bool useFineTunedModels, Action<int, int> onProgress = null)
    {
        if (clip == null)
            throw new ArgumentNullException(nameof(clip));

        if (clip.frequency != AudioUtils.Frequency || clip.channels != AudioUtils.Channels)
            throw new InvalidOperationException("Audio must be 44.1 kHz stereo.");

        if (clip.samples <= 0)
            throw new InvalidOperationException("The clip contains no audio samples.");

        int totalFrames = clip.samples;
        int chunkCount = AudioUtils.GetChunkCount(totalFrames);
        float[] weights = AudioUtils.BuildOverlapWeights(totalFrames);
        var stems = new float[AudioUtils.StemCount][];

        for (int stem = 0; stem < AudioUtils.StemCount; stem++)
            stems[stem] = new float[totalFrames * AudioUtils.Channels];

        if (!useFineTunedModels)
        {
            // One inference per chunk produces all four stems.
            Worker worker = GetBaseWorker();

            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                int startFrame = chunk * AudioUtils.StrideFrames;
                float[] input = AudioUtils.GetSentisChunk(clip, startFrame);
                float[] rawOutput = await RunChunkAsync(worker, input);

                for (int stem = 0; stem < AudioUtils.StemCount; stem++)
                    AudioUtils.AccumulateStem(stems[stem], rawOutput, stem, startFrame, totalFrames);

                onProgress?.Invoke(chunk + 1, chunkCount);
                await Task.Yield();
            }

            for (int stem = 0; stem < AudioUtils.StemCount; stem++)
                AudioUtils.NormalizeStem(stems[stem], weights);
        }
        else
        {
            // Each fine-tuned model returns four rows, but we keep only
            // the specialist's row. Process the full song with a single
            // worker before disposing it and loading the next specialist.
            for (int stem = 0; stem < AudioUtils.StemCount; stem++)
            {
                ModelAsset asset = GetFtModelAsset(stem);
                if (asset == null)
                    throw new InvalidOperationException($"FT model for stem {stem} is not assigned.");

                using (var worker = new Worker(ModelLoader.Load(asset), backend))
                {
                    for (int chunk = 0; chunk < chunkCount; chunk++)
                    {
                        int startFrame = chunk * AudioUtils.StrideFrames;
                        float[] input = AudioUtils.GetSentisChunk(clip, startFrame);
                        float[] rawOutput = await RunChunkAsync(worker, input);

                        AudioUtils.AccumulateStem(
                            stems[stem], rawOutput, stem, startFrame, totalFrames);

                        onProgress?.Invoke(stem * chunkCount + chunk + 1,
                            AudioUtils.StemCount * chunkCount);
                        await Task.Yield();
                    }
                }

                AudioUtils.NormalizeStem(stems[stem], weights);
            }
        }

        return stems;
    }

    private void OnDestroy()
    {
        baseWorker?.Dispose();
        baseWorker = null;
    }
}
