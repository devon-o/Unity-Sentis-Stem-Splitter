using B83.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class Main : MonoBehaviour
{
    [SerializeField] private StemSplitter splitter;
    [SerializeField] private TextMeshProUGUI outputDisplay;
    [SerializeField] private BPMDetector bpmDetector;
    [SerializeField] private Button exportButton;
    [SerializeField] private Button beginAgainButton;
    [SerializeField] private Toggle ftModelToggle;
    [SerializeField] private AudioClip testAudio;

    private string originalFileName;
    private float[] vocals;
    private float[] bass;
    private float[] drums;
    private float[] other;



    private void Start()
    {
        HandleBeginAgainClick();
        exportButton.onClick.AddListener(HandleExportClick);
        beginAgainButton.onClick.AddListener(HandleBeginAgainClick);

#if UNITY_EDITOR
        if (testAudio != null)
        {
            HandleMp3Loaded(testAudio);
        }
#endif
    }

    private void OnDestroy()
    {
        exportButton.onClick.RemoveListener(HandleExportClick);
        beginAgainButton.onClick.RemoveListener(HandleBeginAgainClick);
        DisableDragAndDrop();
    }

    private void EnableDragAndDrop()
    {
        UnityDragAndDropHook.InstallHook();
        UnityDragAndDropHook.OnDroppedFiles += HandleFilesDropped;
    }

    private void DisableDragAndDrop()
    {
        UnityDragAndDropHook.OnDroppedFiles -= HandleFilesDropped;
        UnityDragAndDropHook.UninstallHook();
    }

    private void ShowAdditionalUi(bool includeExport = true)
    {
        beginAgainButton.gameObject.SetActive(true);
        exportButton.gameObject.SetActive(includeExport);
    }

    private void HideAdditionalUi()
    {
        beginAgainButton.gameObject.SetActive(false);
        exportButton.gameObject.SetActive(false);
    }

    private void SetMessage(string message) => outputDisplay.text = message;

    private void HandleBeginAgainClick()
    {
        drums = null;
        vocals = null;
        bass = null;
        other = null;

        SetMessage("Drop .mp3 File Here");
        EnableDragAndDrop();
        HideAdditionalUi();

        ftModelToggle.gameObject.SetActive(true);
    }

    private void HandleExportClick()
    {
#if UNITY_EDITOR
        string filepath = Path.Combine(Application.dataPath, "Stems");
        Directory.CreateDirectory(filepath);
#else
        string filepath = FolderPicker.SelectFolder();
#endif
        if (string.IsNullOrEmpty(filepath))
            return;

        var exports = new Dictionary<string, float[]>
        {
            { $"{originalFileName}-Drums.wav", drums },
            { $"{originalFileName}-Vocals.wav", vocals },
            { $"{originalFileName}-Bass.wav", bass },
            { $"{originalFileName}-Other.wav", other }
        };

        foreach (var export in exports)
        {
            var exportPath = Path.Combine(filepath, export.Key);
            using var fileStream = WavExporterUtils.CreateEmpty(exportPath);
            WavExporterUtils.Write(fileStream, export.Value);
            WavExporterUtils.WriteHeader(fileStream);
        }
    }

    private void HandleFilesDropped(List<string> files, POINT position)
    {
        string mp3Path = files.FirstOrDefault(path => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase));
        if (mp3Path == null)
            return;

        ftModelToggle.gameObject.SetActive(false);

        originalFileName = Path.GetFileNameWithoutExtension(mp3Path);
        DisableDragAndDrop();
        SetMessage("Loading MP3...");
        StartCoroutine(LoadMp3(mp3Path, HandleMp3Loaded));
    }

    private void HandleMp3Loaded(AudioClip clip)
    {
        _ = SplitAudio(clip);
    }

    public IEnumerator LoadMp3(string filePath, Action<AudioClip> onLoaded)
    {
        string uri = new Uri(filePath).AbsoluteUri;
        using var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG);
        var handler = (DownloadHandlerAudioClip)request.downloadHandler;
        handler.compressed = false;
        handler.streamAudio = false;

        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            SetMessage($"Failed to load MP3: ({request.error})");
            EnableDragAndDrop();
            ShowAdditionalUi(false);
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
        if (clip == null || clip.frequency != AudioUtils.Frequency || clip.channels != AudioUtils.Channels)
        {
            if (clip != null)
                Destroy(clip);

            SetMessage("MP3 must be 44.1 kHz stereo (resampling not implemented yet).");
            EnableDragAndDrop();
            ShowAdditionalUi(false);
            yield break;
        }

        onLoaded?.Invoke(clip);
    }

    private async Task SplitAudio(AudioClip clip)
    {
        try
        {
            float[][] results = await splitter.SeparateAsync(
                clip, ftModelToggle.isOn,
                (completed, total) => SetMessage($"Processing: {completed}/{total} chunks"));

            drums = results[0];
            bass = results[1];
            other = results[2];
            vocals = results[3];

            var bpm = bpmDetector.DetectBPM(drums);

            SetMessage($"BPM: {bpm}");
            ShowAdditionalUi();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            SetMessage($"Separation failed: {ex.Message}");
            ShowAdditionalUi(false);
        }
        finally
        {
            if (clip != null)
            {
#if !UNITY_EDITOR
                
                Destroy(clip);
#endif
            }
        }
    }
}
