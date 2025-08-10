using UnityEditor;
using UnityEngine;
using System;
using System.Diagnostics;
using System.IO;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using UnityEditor.Animations;

public class VideoCubeWizard : EditorWindow
{
    [MenuItem("Tools/VideoCube Wizard")] static void Open() { GetWindow<VideoCubeWizard>(true, "VideoCube Wizard"); }

    SerializedObject so; string mp4Path; string ffmpegPath = "ffmpeg"; int fps = 30; int maxFrames = 300; int maxSize = 1024; bool loop = true; bool mipmaps = false;
    VRCAvatarDescriptor targetAvatar;

    void OnGUI()
    {
        EditorGUILayout.LabelField("MP4 → PNG(連番) → Texture2DArray → Animator/EX/Audio", EditorStyles.boldLabel);
        mp4Path = PathField("MP4", mp4Path, "mp4");
        ffmpegPath = EditorGUILayout.TextField("ffmpeg", ffmpegPath);
        fps = EditorGUILayout.IntPopup("FPS", fps, new[] { "15", "24", "30" }, new[] { 15, 24, 30 });
        maxFrames = EditorGUILayout.IntSlider("Max Frames", maxFrames, 30, 1000);
        maxSize = EditorGUILayout.IntPopup("Max Texture Size", maxSize, new[] { "512", "768", "1024" }, new[] { 512, 768, 1024 });
        loop = EditorGUILayout.Toggle("Loop", loop); mipmaps = EditorGUILayout.Toggle("Mipmaps", mipmaps);
        targetAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Target Avatar", targetAvatar, typeof(VRCAvatarDescriptor), true);
        if (GUILayout.Button("Generate")) try { Generate(); } catch (Exception e) { UnityEngine.Debug.LogException(e); }
    }

    string PathField(string label, string path, string ext)
    {
        EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel(label);
        path = EditorGUILayout.TextField(path);
        if (GUILayout.Button("...", GUILayout.Width(30))) { path = EditorUtility.OpenFilePanel(label, "", ext); }
        EditorGUILayout.EndHorizontal(); return path;
    }

    void Generate()
    {
        if (string.IsNullOrEmpty(mp4Path)) throw new Exception("MP4 not set");
        var proj = Application.dataPath; var outDir = AssetDatabase.GenerateUniqueAssetPath("Assets/VideoCube_" + Path.GetFileNameWithoutExtension(mp4Path));
        Directory.CreateDirectory(outDir);
        // 1) ffmpeg: frames & audio
        string frames = Path.Combine(outDir, "Frames"); Directory.CreateDirectory(frames);
        string pattern = Path.Combine(frames, "frame_%05d.png");
        RunFFmpeg($"-i \"{mp4Path}\" -vf fps={fps},scale={maxSize}:-2 -q:v 2 -frames:v {maxFrames} \"{pattern}\"");
        string audioPath = Path.Combine(outDir, "audio.wav");
        RunFFmpeg($"-i \"{mp4Path}\" -vn -ac 2 -ar 48000 -sample_fmt s16 \"{audioPath}\"");
        AssetDatabase.Refresh();

        // 2) Build Texture2DArray
        var arr = VideoFramePacker.BuildArray(frames, maxSize, mipmaps);

        // 3) Material & Shader
        var shader = Shader.Find("Mame2an/VideoArrayUnlit"); if (shader == null) throw new Exception("Shader missing");
        var mat = new Material(shader); mat.SetTexture("_TexArr", arr); AssetDatabase.CreateAsset(mat, outDir + "/VideoMat.mat");

        // 4) Cube (16:9)
        var root = new GameObject("VideoCube"); var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube); mesh.transform.SetParent(root.transform, false);
        mesh.transform.localScale = new Vector3(1.6f, 0.9f, 0.1f); var rend = mesh.GetComponent<Renderer>(); rend.sharedMaterial = mat; DestroyImmediate(mesh.GetComponent<Collider>());

        // 5) Audio
        var audioGO = new GameObject("VideoAudio"); audioGO.transform.SetParent(root.transform, false);
        var src = audioGO.AddComponent<AudioSource>(); src.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ToAssetPath(audioPath)); src.playOnAwake = false; src.spatialize = true; src.spatialBlend = 1f; src.loop = loop;
        audioGO.AddComponent(Type.GetType("VRC.SDKBase.VRC_SpatialAudioSource, VRCSDKBase"));

        // 6) FX Animator + Clip (animates material _Frame & toggles audio enable)
        var ctrl = new AnimatorController(); AssetDatabase.CreateAsset(ctrl, outDir + "/VideoFX.controller");
        var state = ctrl.AddMotion(BuildFrameClip(arr.depth, fps, loop, rend, src, outDir));

        // 7) Attach to Avatar or make new
        if (targetAvatar == null) { var avRoot = new GameObject("VideoAvatarRoot"); root.transform.SetParent(avRoot.transform, false); avRoot.AddComponent<VRCAvatarDescriptor>(); }
        else { root.transform.SetParent(targetAvatar.transform, false); }
        var anim = (targetAvatar ? targetAvatar.gameObject : root.transform.root.gameObject).GetComponent<Animator>() ?? (targetAvatar ? targetAvatar.gameObject : root).AddComponent<Animator>();
        anim.runtimeAnimatorController = MergeFX(anim.runtimeAnimatorController as AnimatorController, ctrl, outDir);

        // 8) Expressions: parameter & menu
        var param = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        param.parameters = new[]{ new VRCExpressionParameters.Parameter{ name="VideoPlay", valueType=VRCExpressionParameters.ValueType.Bool, defaultValue=0, saved=true},
                                  new VRCExpressionParameters.Parameter{ name="VideoSpeed", valueType=VRCExpressionParameters.ValueType.Float, defaultValue=1, saved=false} };
        AssetDatabase.CreateAsset(param, outDir + "/VideoParameters.asset");
        var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        var ctgl = new VRCExpressionsMenu.Control { name = "Video/Play", type = VRCExpressionsMenu.Control.ControlType.Toggle, parameter = new VRCExpressionsMenu.Control.Parameter { name = "VideoPlay" } };
        var speed = new VRCExpressionsMenu.Control { name = "Video/Speed", type = VRCExpressionsMenu.Control.ControlType.RadialPuppet, parameter = new VRCExpressionsMenu.Control.Parameter { name = "VideoSpeed" } };
        menu.controls.Add(ctgl); menu.controls.Add(speed); AssetDatabase.CreateAsset(menu, outDir + "/VideoMenu.asset");

        // 9) FX Layer transitions
        // Layer 0 idle(off) → on(VideoPlay=true) → state (plays) / blendtree speed = VideoSpeed
        var layer = ctrl.AddLayer("VideoLayer"); var sm = layer.stateMachine;
        var idle = sm.AddState("Idle"); idle.writeDefaultValues = false; sm.defaultState = idle;
        var play = sm.AddState("Play"); play.motion = state.motion; play.writeDefaultValues = false; play.speedParameterActive = true; play.speedParameter = "VideoSpeed";
        var p = new AnimatorControllerParameter { name = "VideoPlay", type = AnimatorControllerParameterType.Bool, defaultBool = false }; ctrl.AddParameter(p);
        var t1 = idle.AddTransition(play); t1.AddCondition(AnimatorConditionMode.If, 0, "VideoPlay"); t1.hasExitTime = false; var t2 = play.AddTransition(idle); t2.AddCondition(AnimatorConditionMode.IfNot, 0, "VideoPlay"); t2.hasExitTime = false;

        Selection.activeObject = root;
        EditorUtility.DisplayDialog("VideoCube", "Setup Complete", "OK");
    }

    AnimationClip BuildFrameClip(int frames, int fps, bool loop, Renderer rend, AudioSource src, string outDir)
    {
        var clip = new AnimationClip(); clip.frameRate = fps;
        var binding = EditorCurveBinding.FloatCurve($"{GetPath(rend.transform)}", typeof(Renderer), "material._Frame");
        var keys = new Keyframe[frames]; for (int i = 0; i < frames; i++) { keys[i] = new Keyframe((float)i, i); }
        AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
        // Audio on/off: enable GameObject at start to trigger PlayOnEnable via a helper
        var audioEnable = EditorCurveBinding.FloatCurve($"{GetPath(src.transform)}", typeof(GameObject), "m_IsActive");
        var aKeys = new[] { new Keyframe(0, 1) }; AnimationUtility.SetEditorCurve(clip, audioEnable, new AnimationCurve(aKeys));
        clip.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
        AssetDatabase.CreateAsset(clip, outDir + "/VideoFrames.anim"); return clip;
    }

    AnimatorController MergeFX(AnimatorController current, AnimatorController add, string outDir)
    {
        if (current == null) return add; // 簡易合流：実運用ではレイヤーマージ推奨
        foreach (var l in add.layers) current.AddLayer(l);
        foreach (var p in add.parameters) if (Array.FindIndex(current.parameters, x => x.name == p.name) < 0) current.AddParameter(p);
        AssetDatabase.SaveAssets(); return current;
    }

    string GetPath(Transform t) { return AnimationUtility.CalculateTransformPath(t, t.root); }
    string ToAssetPath(string abs) { return abs.Replace(Application.dataPath, "Assets"); }

    void RunFFmpeg(string args)
    {
        var psi = new ProcessStartInfo(ffmpegPath, args) { UseShellExecute = false, CreateNoWindow = true };
        var p = Process.Start(psi); p.WaitForExit(); if (p.ExitCode != 0) throw new Exception("ffmpeg failed");
    }
}