using BepInEx;
using Cute;
using HarmonyLib;
using PriconneALLTLFixup;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core;
using XUnity.AutoTranslator.Plugin.Core.Configuration;

namespace PriconneALLTLFixup.Patches;

#region MODULE: INITIALIZATION
[HarmonyPatch(typeof(BootApp), nameof(BootApp.Start))]
[HarmonyPatchCategory("Visual")]
public class AtlasInitPatch
{
    private static string AtlasPath =>
        Path.Combine(Paths.BepInExRootPath, "Translation", Util.GetTargetLanguage(), "Other", "atlases");

    public static readonly Dictionary<string, UIAtlas> CustomAtlases = new(32);
    public static readonly Dictionary<string, UIAtlas> OriginalAtlases = new(32);
    internal const string NameSuffix = " (Fixup)";

    [HarmonyPrepare]
    public static bool Prepare() => Settings.Visual.AtlasPatches?.Value == true;

    [HarmonyPriority(Priority.High)]
    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
            Log.Info($"[Atlas] Initializing Atlas system from: {AtlasPath}");

            if (!Directory.Exists(AtlasPath)) return;

            var jsonFiles = Directory.GetFiles(AtlasPath, "*.json");
            if (jsonFiles.Length == 0) return;

            var shader = Shader.Find("Cygames/Unlit/Transparent Colored") ?? Shader.Find("Unlit/Transparent Colored");
            if (shader == null)
            {
                Log.Error("[Atlas] Shader not found! Images will not render.");
                return;
            }

            foreach (var jsonFile in jsonFiles)
            {
                try { LoadAtlas(jsonFile, shader); }
                catch (Exception ex) { Log.Error($"[Atlas] Load Error: {jsonFile}", ex); }
            }
            Log.Info($"[Atlas] Total registered: {CustomAtlases.Count} atlases.");
        }
        catch (Exception ex) { Log.Error("[Atlas] Critical Initialization Error", ex); }
    }

    private static void LoadAtlas(string jsonFile, Shader shader)
    {
        var atlasName = Path.GetFileNameWithoutExtension(jsonFile);
        var directory = Path.GetDirectoryName(jsonFile);

        var matchingFiles = Directory.GetFiles(directory!, atlasName + "*.png");
        if (matchingFiles.Length == 0) return;

        var texturePath = matchingFiles[0];
        var atlasGO = new GameObject(atlasName + NameSuffix).Persist();
        var atlas = atlasGO.AddComponent<UIAtlas>();

        JsonUtility.FromJsonInternal(File.ReadAllText(jsonFile), atlas, atlas.GetIl2CppType());

        var texData = File.ReadAllBytes(texturePath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false).Persist();
        texture.name = atlasName + "_Tex";
        texture.LoadImage(texData);

        var mat = new Material(shader).Persist();
        mat.mainTexture = texture;
        atlas.material = mat;
        atlas.name = atlasName + NameSuffix;

        CustomAtlases[atlasName] = atlas;
    }
}
#endregion

#region MODULE: INJECTION
[HarmonyPatch(typeof(UISprite), nameof(UISprite.OnInit))]
[HarmonyPatchCategory("Visual")]
public class SpriteInitPatch
{
    [HarmonyPriority(Priority.Normal)]
    public static void Postfix(UISprite __instance)
    {
        if (!__instance.IsSafe() || __instance.atlas == null) return;

        string atlasName = __instance.atlas.name;

        if (AtlasInitPatch.CustomAtlases.TryGetValue(atlasName, out var customAtlas))
        {
            if (customAtlas.GetSprite(__instance.mSpriteName) != null)
            {
                __instance.RemoveFromPanel();

                __instance.atlas = customAtlas;

                __instance.MarkAsChanged();
            }
        }
    }
}

[HarmonyPatch(typeof(UISprite), nameof(UISprite.spriteName), MethodType.Setter)]
public class SpriteNameUpdatePatch
{
    public static void Prefix(UISprite __instance, string value)
    {
        if (!__instance.IsSafe() || __instance.atlas == null) return;

        string cleanName = __instance.atlas.name.Replace(AtlasInitPatch.NameSuffix, "");

        if (AtlasInitPatch.CustomAtlases.TryGetValue(cleanName, out var customAtlas))
        {
            if (customAtlas.GetSprite(value) != null)
            {
                if (__instance.atlas.name != customAtlas.name)
                {
                    __instance.RemoveFromPanel();
                    __instance.atlas = customAtlas;
                    __instance.MarkAsChanged();
                }
            }
            else if (__instance.atlas.name.Contains(AtlasInitPatch.NameSuffix))
            {
                if (AtlasInitPatch.OriginalAtlases.TryGetValue(cleanName, out var original))
                {
                    __instance.RemoveFromPanel();
                    __instance.atlas = original;
                    __instance.MarkAsChanged();
                }
            }
        }
    }
}
#endregion

#region MODULE: VISUAL FIX
#if false
[HarmonyPatch]
[HarmonyPatchCategory("Visual")]
public class SpriteScaleFixPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(UISprite), nameof(UISprite.OnInit));
        yield return AccessTools.PropertySetter(typeof(UISprite), nameof(UISprite.spriteName));
    }

    [HarmonyPriority(Priority.Low)] // ต่ำ (600) เพื่อให้รันหลัง SpriteInitPatch สลับ Atlas เสร็จแล้ว
    [HarmonyPostfix]
    public static void Postfix(UISprite __instance)
    {
        if (!__instance.IsSafe() || __instance.atlas == null) return;
        if (!__instance.atlas.name.Contains(AtlasInitPatch.NameSuffix)) return;

        var atlasSprite = __instance.GetAtlasSprite();
        if (atlasSprite == null) return;

        try
        {
            float pSize = __instance.pixelSize;
            int targetW = (Mathf.RoundToInt(pSize * (atlasSprite.width + atlasSprite.paddingLeft + atlasSprite.paddingRight)));
            int targetH = (Mathf.RoundToInt(pSize * (atlasSprite.height + atlasSprite.paddingTop + atlasSprite.paddingBottom)));

            if (__instance.mWidth != targetW || __instance.mHeight != targetH)
            {
                __instance.mWidth = targetW;
                __instance.mHeight = targetH;
                __instance.MarkAsChanged();
            }
        }
        catch { /* Ignore visual errors */ }
    }
}
#endif
#endregion

#region MODULE: DEVELOPER TOOLS
[HarmonyPatch(typeof(UISprite), nameof(UISprite.OnInit))]
[HarmonyPatchCategory("Developer")]
public class AtlasDumpPatch
{
    private static readonly HashSet<string> DumpedNames = new();

    public static void Prefix(UISprite __instance)
    {
        if (__instance.atlas == null) return;

        // --- ใช้ Reflection ดึงค่าเพื่อตัดปัญหาตัวแดง ---
        bool isDumpEnabled = false;
        string? xunityPath = null;

        try
        {
            // 1. หาคลาส Settings ผ่านชื่อ (String) เพื่อไม่ให้คอมไพเลอร์ฟ้อง Error
            var settingsType = AccessTools.TypeByName("XUnity.AutoTranslator.Plugin.Core.Configuration.Settings")
                            ?? AccessTools.TypeByName("XUnity.AutoTranslator.Plugin.Core.Settings");

            if (settingsType != null)
            {
                // 2. ดึงค่า EnableTextureDumping
                var dumpProp = AccessTools.Property(settingsType, "EnableTextureDumping");
                isDumpEnabled = (bool?)dumpProp?.GetValue(null) ?? false;

                // 3. ดึงค่า Path (ลองหาทั้งสองชื่อที่ระบบมักจะใช้)
                var pathProp = AccessTools.Property(settingsType, "TextureDirectory")
                            ?? AccessTools.Property(settingsType, "TexturesPath");
                xunityPath = pathProp?.GetValue(null) as string;
            }
        }
        catch { /* ป้องกันเกมค้างถ้าหาคลาสไม่เจอ */ }

        // --- ตรวจสอบเงื่อนไขการทำงาน ---
        if (!isDumpEnabled || string.IsNullOrEmpty(xunityPath)) return;

        var atlas = __instance.atlas;
        string finalDumpPath = Path.Combine(xunityPath, "[Dump]");

        if (DumpedNames.Contains(atlas.name) || atlas.name.Contains(AtlasInitPatch.NameSuffix)) return;

        try
        {
            if (!Directory.Exists(finalDumpPath)) Directory.CreateDirectory(finalDumpPath);

            string jsonPath = Path.Combine(finalDumpPath, atlas.name + ".json");
            string json = JsonUtility.ToJson(atlas);
            File.WriteAllText(jsonPath, json);

            DumpedNames.Add(atlas.name);
            Log.Debug($"[Atlas Dump] Exported JSON to: {jsonPath}");
        }
        catch (Exception ex) { Log.Error($"[Atlas Dump] Error: {ex.Message}"); }
    }
}
#endregion
