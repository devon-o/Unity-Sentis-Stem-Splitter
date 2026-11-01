# Unity-Sentis-Stem-Splitter
Unity3D Windows app for splitting the stems (and calculating BPM) of an .mp3 file

# Project Title

Unity Sentis .MP3 Stem Splitter

## Description

This is a Unity 3D project to create a Windows 64-bit app that will
- split the stems of a .mp3 file (drums, vocals, bass, other)
- display the BPM of the mp3 (based on the drums stem only)

## Getting Started

*Note* This will only work with .mp3 files with 2 channels and 44.1KHz.

Once built, simply drag and drop a .mp3 file as described above into the app window. Let it process (I recommend short clips rather than full songs to begin - maybe 15-20 seconds long). When done, the BPM will be displayed along with a button to export the stems. If you choose to do so it will export 4 .wav files (for drums, vocals, bass, and other) to the folder you specify.

To test in the Unity Editor, assign an AudioClip to the TestAudio serialized field of the 'Main' game object in the main scene and just enter playmode. After processing and hitting the Export Stems button, the stem wav files will be saved in `Assets/Stems`.

### Dependencies

This repo does not include the required models. They can be downloaded from HuggingFace:
- [Base Model (quicker all-in-one model)](https://huggingface.co/StemSplitio/htdemucs-onnx)
- [Fine Tuned Models (4 models - one per stem)](https://huggingface.co/StemSplitio/htdemucs-ft-onnx)

Download the 5 ONNX files from those 2 links and place them in the `Assets/Models` director.

In the Main scene, assign those models to the serialized fields of the Stem Splitter object (the base model goes into the "Model Asset" field and the 4 fine tuned models go into the fine tuned specialist model fields)

## Authors

Devon O. Wolfgang

## License

This project is licensed under the MIT License - see the LICENSE.md file for details

## Acknowledgments

* [Markus Göbel (fantastic drag/drop win hook)](https://github.com/Bunny83/UnityWindowsFileDrag-Drop/blob/master/B83.Win32.cs)
* [josuahoenger (native win folder picker)](https://discussions.unity.com/t/native-folder-picker-under-windows/1624972)