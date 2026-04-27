using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace PriconneALLTLFixup;

public class HarmonyPatchController
{
    #region 1. Fields & Performance Monitoring
    private readonly Harmony _harmony;
    private readonly string _namespaceFilter;

    private static readonly FieldInfo _priorityField = AccessTools.Field(typeof(HarmonyPriority), "priority");

    private readonly List<Type> _criticalRegistry = new();
    private readonly List<Type> _featureRegistry = new();

    private readonly Dictionary<string, long> _patchProfiling = new();

    public IReadOnlyDictionary<string, long> PatchProfiling => _patchProfiling;

    public Harmony Instance => _harmony;

    public int ActivePatchCount => _criticalRegistry.Count + _featureRegistry.Count;
    #endregion

    #region 2. Professional Constructor
    public HarmonyPatchController(string harmonyId, string namespacePrefix)
    {
        if (string.IsNullOrEmpty(harmonyId)) throw new ArgumentNullException(nameof(harmonyId));

        _harmony = new Harmony(harmonyId);
        _namespaceFilter = namespacePrefix;

        InitializeRegistry();
    }
    #endregion

    #region 3. Public Orchestration API
    public void ApplySmartPatching()
    {
        Log.Info($"[Harmony] Initializing smart sequence: {_criticalRegistry.Count} Critical, {_featureRegistry.Count} Features.");

        foreach (var patch in _criticalRegistry) ApplySinglePatch(patch);

        if (_featureRegistry.Count > 0)
        {
            CoroutineStarter.Run(ProcessAsyncBatch(3, _featureRegistry));
        }
    }

    public void ApplyAllSynchronous()
    {
        Log.Info($"[Harmony] Full sync deployment for {ActivePatchCount} patches.");
        foreach (var patch in _criticalRegistry) ApplySinglePatch(patch);
        foreach (var patch in _featureRegistry) ApplySinglePatch(patch);
    }

    public void Patch(Type type) => ApplySinglePatch(type);

    public void UnpatchAll()
    {
        try
        {
            _harmony.UnpatchSelf();
            _patchProfiling.Clear();
            Log.Info("[Harmony] Global teardown complete. All patches removed.");
        }
        catch (Exception ex)
        {
            Log.Error($"[Harmony] Teardown failed: {ex.Message}");
        }
    }

    public void Unpatch(Type type)
    {
        if (type == null) return;
        try
        {
            var methods = _harmony.GetPatchedMethods().ToList();
            foreach (var method in methods)
            {
                var info = Harmony.GetPatchInfo(method);
                if (info == null) continue;

                bool isOwner = info.Prefixes.Any(p => p.PatchMethod.DeclaringType == type) ||
                               info.Postfixes.Any(p => p.PatchMethod.DeclaringType == type) ||
                               info.Transpilers.Any(p => p.PatchMethod.DeclaringType == type);

                if (isOwner) _harmony.Unpatch(method, HarmonyPatchType.All, _harmony.Id);
            }
            _patchProfiling.Remove(type.Name);
            Log.Debug($"Unpatched module: {type.Name}");
        }
        catch (Exception ex)
        {
            Log.Error($"[Harmony] Partial unpatch failed for {type.Name}: {ex.Message}");
        }
    }
    #endregion

    #region 4. Internal Engine Logic
    private void InitializeRegistry()
    {
        try
        {
            var allTypes = Assembly.GetExecutingAssembly().GetTypes();
            var patchList = new List<(Type Type, int Priority)>();

            foreach (var type in allTypes)
            {
                if (type.Namespace == null || !type.Namespace.StartsWith(_namespaceFilter)) continue;
                if (type.IsAbstract || !type.GetCustomAttributes(typeof(HarmonyPatch), true).Any()) continue;

                var prioAttr = type.GetCustomAttribute<HarmonyPriority>();
                int priorityValue = 400;

                if (prioAttr != null && _priorityField != null)
                {
                    var val = _priorityField.GetValue(prioAttr);
                    if (val is int p) priorityValue = p;
                }

                patchList.Add((type, priorityValue));
            }

            var sortedList = patchList.OrderBy(x => x.Priority).ToList();

            foreach (var item in sortedList)
            {
                if (item.Priority < 400) _criticalRegistry.Add(item.Type);
                else _featureRegistry.Add(item.Type);
            }

            Log.Debug($"[Scanner] Registry populated with {patchList.Count} modules.");
        }
        catch (Exception ex)
        {
            Log.Error($"[Scanner] Failed to build patch registry: {ex.Message}");
        }
    }

    private void ApplySinglePatch(Type type)
    {
        if (type == null) return;
        var timer = Stopwatch.StartNew();
        try
        {
            var prepare = type.GetMethod("Prepare", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (prepare != null)
            {
                var shouldPatch = prepare.Invoke(null, new object[] { _harmony });
                if (shouldPatch is bool result && !result)
                {
                    Log.Debug($"[Harmony] Skipped {type.Name} per Prepare() logic.");
                    return;
                }
            }

            _harmony.PatchAll(type);
            timer.Stop();

            _patchProfiling[type.Name] = timer.ElapsedMilliseconds;

            var cat = type.GetCustomAttribute<HarmonyPatchCategory>()?.Category ?? "Misc";
            Log.Debug($"[{cat}] Applied: {type.Name} in {timer.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            timer.Stop();
            Log.Error($"[Harmony] Critical failure in {type.Name}: {ex.Message}");
        }
    }

    private IEnumerator ProcessAsyncBatch(int size, List<Type> targetList)
    {
        int processed = 0;
        foreach (var type in targetList)
        {
            ApplySinglePatch(type);
            processed++;
            if (processed % size == 0) yield return null;
        }
        Log.Info($"[Harmony] Background deployment finished ({processed} modules).");
    }
    #endregion
}

#region 5. Formal Attributes
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HarmonyPatchCategory : Attribute
{
    public string Category { get; }
    public HarmonyPatchCategory(string category) => Category = category;
}
#endregion