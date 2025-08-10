using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class VideoFramePacker
{
    public static Texture2DArray BuildArray(string framesFolder, int maxSize, bool mipmaps)
    {
        var files = Directory.GetFiles(framesFolder, "frame_*.png").OrderBy(f => f).ToArray();
        if (files.Length == 0) throw new System.Exception("No frames");
        var tex0 = AssetDatabase.LoadAssetAtPath<Texture2D>(ToAssetPath(files[0]));
        int w = Mathf.Min(tex0.width, maxSize);
        int h = Mathf.Min(tex0.height, maxSize);
        var arr = new Texture2DArray(w, h, files.Length, TextureFormat.RGBA32, mipmaps);
        arr.wrapMode = TextureWrapMode.Clamp; arr.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < files.Length; i++)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(ToAssetPath(files[i]));
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32); rt.Create();
            Graphics.Blit(t, rt);
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
            RenderTexture.active = rt; tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0); tmp.Apply();
            Graphics.CopyTexture(tmp, 0, 0, arr, i, 0);
            Object.DestroyImmediate(tmp); rt.Release();
        }
        var outPath = framesFolder.TrimEnd('/') + "/VideoFrames.asset";
        AssetDatabase.CreateAsset(arr, outPath);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<Texture2DArray>(outPath);
    }
    static string ToAssetPath(string abs) { return abs.Replace(Application.dataPath, "Assets"); }
}