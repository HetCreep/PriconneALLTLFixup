#nullable enable
using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;


namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Unified Sprite Atlas patch module.
/// <para>Module A: Atlas Initialisation — loads custom PNG+JSON atlas replacements from disk at boot time.</para>
/// <para>Module B: Atlas Dump — serialises unrecognised atlases to JSON for inspection / replacement authoring.</para>
/// <para>Module C: Sprite Name Update — redirects <c>UISprite.spriteName</c> writes to the matching fixup atlas.</para>
/// </summary>
[HarmonyPatch]
public static class SpriteAtlasPatch
{
    #region 1. Shared Constants & State
    /// <summary>Name suffix appended to every fixup atlas and its materials/textures.</summary>
    public const string FixupSuffix = " (Fixup)";

    /// <summary>Shader used for all fixup atlas materials.</summary>
    private const string AtlasShaderName = "Cygames/Unlit/Transparent Colored";

    /// <summary>Render queue value that places fixup sprites above opaque but below particles.</summary>
    private const int AtlasRenderQueue = 3054;

    /// <summary>Map of atlas base-name → loaded fixup <see cref="UIAtlas"/>.
    /// ConcurrentDictionary because <c>UISprite.spriteName</c> setter fires from
    /// any thread that mutates a sprite (e.g. animation callbacks).</summary>
    public static readonly ConcurrentDictionary<string, UIAtlas> Atlases         = new();

    /// <summary>Map of atlas base-name → original game <see cref="UIAtlas"/> (populated by Module C).</summary>
    public static readonly ConcurrentDictionary<string, UIAtlas> OriginalAtlases = new();

    /// <summary>Set of atlas names already dumped this session (Module B).
    /// Wrapped as a concurrent set via <see cref="ConcurrentDictionary{TKey,TValue}"/>.</summary>
    private static readonly ConcurrentDictionary<string, byte> _dumpedAtlases = new();

    private static string AtlasPath => Path.Join(
        Paths.BepInExRootPath, "Translation",
        ConfigManager.Translation.Code.Value,
        "Other", "atlases");
    #endregion

    // =========================================================================
    // MODULE A: Atlas Initialisation (invoked from Plugin.BootFallback)
    // =========================================================================

    #region 2. Module A — Init
    /// <summary>
    /// Scans the translation atlas directory for <c>*.json</c> + matching
    /// <c>*.png</c> pairs and loads them as persistent <see cref="UIAtlas"/>
    /// objects that can substitute the game's built-in sprites.
    /// Called directly from <c>Plugin.BootFallback()</c>.
    /// </summary>
    public static void PostfixAtlasInit()
    {
        string[] jsonFiles;
        try
        {
            jsonFiles = Directory.GetFiles(AtlasPath, "*.json");
        }
        catch (Exception ex)
        {
            FLog.Warn($"[Atlas] Atlas path not accessible: {AtlasPath} — {ex.Message}");
            return;
        }

        if (jsonFiles.Length == 0)
        {
            FLog.Debug($"[Atlas] No atlas JSON files found in {AtlasPath}. Skipping initialisation.");
            return;
        }

        var shader = Shader.Find(AtlasShaderName);
        if (shader == null)
        {
            FLog.Error($"[Atlas] Required shader '{AtlasShaderName}' not found. Atlas loading aborted.");
            return;
        }

        // Collect all PNG base names available in the atlas directory
        var pngBaseNames = Directory.EnumerateFiles(AtlasPath, "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(f => f != null)
            .Cast<string>()
            .ToList();

        if (pngBaseNames.Count == 0)
        {
            FLog.Warn($"[Atlas] No PNG files found in {AtlasPath}.");
            return;
        }

        var sw = Stopwatch.StartNew();

        // Phase 1: Resolve JSON→PNG mappings (CPU-only, no Unity calls)
        var loadTasks = new List<(string name, string jsonPath, string pngPath)>(jsonFiles.Length);
        foreach (string jsonFile in jsonFiles)
        {
            string atlasName = Path.GetFileNameWithoutExtension(jsonFile);

            var candidates = pngBaseNames
                .Where(f => Regex.IsMatch(f, $@"^{Regex.Escape(atlasName)}(\s\[[0-9A-F]{{10}}\])?$"))
                .ToList();

            if (candidates.Count == 0)
            {
                FLog.Warn($"[Atlas] No PNG found for atlas '{atlasName}' (json: {jsonFile}).");
                continue;
            }

            string chosenBase = candidates.Count == 1
                ? candidates[0]
                : candidates
                    .OrderByDescending(f => File.GetLastWriteTime(Path.Join(AtlasPath, f + ".png")))
                    .First();

            string pngPath = Path.Join(AtlasPath, chosenBase + ".png");
            if (!File.Exists(pngPath))
            {
                FLog.Warn($"[Atlas] PNG resolved to non-existent path: {pngPath}.");
                continue;
            }

            loadTasks.Add((atlasName, jsonFile, pngPath));
        }

        // Phase 2: Parallel disk I/O — read JSON + PNG bytes on ThreadPool
        var preloaded = new (string name, string json, byte[] bytes)[loadTasks.Count];
        Parallel.For(0, loadTasks.Count, i =>
        {
            var (name, jsonPath, pngPath) = loadTasks[i];
            try
            {
                preloaded[i] = (name, File.ReadAllText(jsonPath), File.ReadAllBytes(pngPath));
            }
            catch (Exception ex)
            {
                FLog.Error($"[Atlas] Failed to pre-read '{name}': {ex.Message}");
                preloaded[i] = (name, null!, null!);
            }
        });

        // Phase 3: Create Unity objects on main thread (Texture2D/Material/GameObject)
        int loaded = 0;
        for (int i = 0; i < preloaded.Length; i++)
        {
            var (name, json, bytes) = preloaded[i];
            if (json == null || bytes == null) continue;

            CreateAtlasFromPreloaded(name, json, bytes, shader);
            loaded++;
        }

        sw.Stop();
        FLog.Info($"[Atlas] Loaded {loaded}/{jsonFiles.Length} atlases in {sw.ElapsedMilliseconds} ms.");
    }

    /// <summary>Creates a persistent <see cref="UIAtlas"/> from pre-read JSON and PNG data.
    /// Must be called on the main thread (creates Unity objects).</summary>
    private static void CreateAtlasFromPreloaded(string atlasName, string json, byte[] pngBytes, Shader shader)
    {
        try
        {
            var go = new GameObject(atlasName + FixupSuffix);
            go.Persistent();

            var atlas = go.AddComponent<UIAtlas>();
            atlas.name = atlasName + FixupSuffix;
            JsonUtility.FromJsonInternal(json, atlas, atlas.GetIl2CppType());

            int size = ComputeAtlasSize(atlas);
            var tex  = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name                 = atlasName + FixupSuffix,
                requestedMipmapLevel = 0,
                filterMode           = FilterMode.Trilinear
            };
            ImageConversion.LoadImage(tex, pngBytes);

            var mat = new Material(shader)
            {
                name         = atlasName + FixupSuffix,
                renderQueue  = AtlasRenderQueue,
                mainTexture  = tex
            };

            atlas.material = mat;
            Atlases[atlasName] = atlas;

            FLog.Debug($"[Atlas] Loaded atlas '{atlasName}' ({size}×{size}).");
        }
        catch (Exception ex)
        {
            FLog.Error($"[Atlas] Failed to load atlas '{atlasName}': {ex.Message}");
        }
    }

    /// <summary>Calculates the next power-of-two size that encompasses all sprite rects in <paramref name="atlas"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeAtlasSize(UIAtlas atlas)
    {
        int maxX = 0, maxY = 0;
        foreach (var sprite in atlas.spriteList)
        {
            maxX = Math.Max(maxX, sprite.x + sprite.width);
            maxY = Math.Max(maxY, sprite.y + sprite.height);
        }
        return Mathf.NextPowerOfTwo(Mathf.Max(maxX, maxY));
    }
    #endregion

    // =========================================================================
    // MODULE B: Atlas Dump (UISprite::OnInit)
    // =========================================================================

    #region 3. Module B — Hook
    /// <summary>
    /// High-priority prefix on <c>UISprite.OnInit</c>: if texture dumping is enabled and
    /// the sprite's atlas has not been seen before, serialises it to JSON inside the
    /// configured dump folder for authoring fixup replacements.
    /// Runs at priority 600 to execute before any atlas-swap logic.
    /// </summary>
    [HarmonyPatch(typeof(UISprite), "OnInit")]
    [HarmonyPrefix]
    [HarmonyPriority(600)]
    [HarmonyWrapSafe]
    public static void PrefixAtlasDump(UISprite __instance)
    {
        if (!__instance.IsSafe()) return;

        var atlas = __instance.atlas;
        if (!atlas.IsSafe()) return;
        if (!ConfigManager.Visual.EnableTextureDumping.Value) return;

        // Skip already-dumped, already-loaded fixup atlases, and game atlases bearing the fixup suffix
        if (_dumpedAtlases.ContainsKey(atlas!.name)
         || Atlases.Values.Contains(atlas)
         || atlas.name.Contains(FixupSuffix)) return;

        string dumpPath = string.IsNullOrWhiteSpace(ConfigManager.Visual.TexturesDumpPath.Value)
            ? AtlasPath
            : ConfigManager.Visual.TexturesDumpPath.Value;
        if (string.IsNullOrEmpty(dumpPath))
        {
            FLog.Warn("[Atlas] Texture dump path is not configured — skipping dump.");
            return;
        }

        try
        {
            string json     = JsonUtility.ToJson(atlas);
            string filePath = Path.Join(dumpPath, atlas.name + ".json");
            File.WriteAllText(filePath, json);
            _dumpedAtlases.TryAdd(atlas.name, 0);
            FLog.Debug($"[Atlas] Dumped atlas '{atlas.name}' to {filePath}.");
        }
        catch (Exception ex)
        {
            FLog.Warn($"[Atlas] Failed to dump atlas '{atlas.name}': {ex.Message}");
        }
    }
    #endregion

    // =========================================================================
    // MODULE C: Sprite Name Update (UISprite::spriteName setter)
    // =========================================================================

    #region 4. Module C — Hook
    /// <summary>
    /// Prefix on the <c>UISprite.spriteName</c> setter:
    /// when a sprite references a base atlas name that has a loaded fixup counterpart,
    /// redirects the sprite to use the fixup atlas instead.
    /// This ensures custom translated sprites replace their Japanese originals transparently.
    /// </summary>
    [HarmonyPatch(typeof(UISprite), "spriteName", MethodType.Setter)]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixSpriteNameUpdate(UISprite __instance, string value)
    {
        if (!__instance.IsSafe() || string.IsNullOrEmpty(value)) return;

        var currentAtlas = __instance.atlas;
        if (!currentAtlas.IsSafe()) return;

        string atlasName = currentAtlas!.name.Replace(FixupSuffix, string.Empty);

        // Already on a fixup atlas — nothing to do
        if (currentAtlas.name.Contains(FixupSuffix)) return;

        if (!Atlases.TryGetValue(atlasName, out UIAtlas? fixupAtlas)) return;

        // Preserve the original atlas reference the first time we swap it
        OriginalAtlases.TryAdd(atlasName, currentAtlas);

        __instance.atlas = fixupAtlas;
        FLog.Debug($"[Atlas] Sprite '{value}' redirected to fixup atlas '{atlasName}'.");
    }
    #endregion
}
