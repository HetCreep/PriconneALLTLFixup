using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace PriconneALLTLFixup;

public class HarmonyPatchController
{
    #region 1. Fields
    private readonly Harmony _harmony;
    private readonly string _namespaceFilter;
    // ConcurrentDictionary: patch profiling rows are written from the smart-batch
    // coroutine while the property is exposed for read on any thread.
    private readonly ConcurrentDictionary<string, long> _patchProfiling = new();

    private readonly List<Type> _criticalRegistry = new();
    private readonly List<Type> _featureRegistry = new();

    public IReadOnlyDictionary<string, long> PatchProfiling => _patchProfiling;
    public int ActivePatchCount => _criticalRegistry.Count + _featureRegistry.Count;
    #endregion

    public HarmonyPatchController(string harmonyId, string namespacePrefix)
    {
        _harmony = new Harmony(harmonyId);
        _namespaceFilter = namespacePrefix;
        InitializeRegistry();
    }

    #region 2. API
    public void Activate(List<Type> critical, List<Type> features)
    {
        _criticalRegistry.Clear();
        _featureRegistry.Clear();
        _criticalRegistry.AddRange(critical);
        _featureRegistry.AddRange(features);

        if (FLog.IsDeveloperContext) ApplyAllSynchronous();
        else ApplySmartPatching();
    }

    public void ApplySmartPatching()
    {
        foreach (var patch in _criticalRegistry) ApplySinglePatch(patch);
        if (_featureRegistry.Count > 0)
            CoroutineStarter.Run(ProcessAsyncBatch(3, _featureRegistry));
    }

    public void ApplyAllSynchronous()
    {
        foreach (var patch in _criticalRegistry) ApplySinglePatch(patch);
        foreach (var patch in _featureRegistry) ApplySinglePatch(patch);
    }

    public void UnpatchAll()
    {
        try { _harmony.UnpatchSelf(); _patchProfiling.Clear(); }
        catch (Exception ex) { FLog.Error($"[Harmony] Unpatch failed: {ex.Message}"); }
    }

    /// <summary>Apply (or re-apply) a single patch class on demand.</summary>
    public void Patch(Type type) => ApplySinglePatch(type);

    /// <summary>Remove all patches contributed by a single patch class.</summary>
    public void Unpatch(Type type)
    {
        if (type == null) return;
        try
        {
            foreach (var original in PatchedMethods())
            {
                var info = Harmony.GetPatchInfo(original);
                if (info == null) continue;

                bool owned = info.Prefixes.Any(p  => p.owner == _harmony.Id && p.PatchMethod.DeclaringType == type)
                          || info.Postfixes.Any(p => p.owner == _harmony.Id && p.PatchMethod.DeclaringType == type)
                          || info.Transpilers.Any(p => p.owner == _harmony.Id && p.PatchMethod.DeclaringType == type);

                if (owned)
                    _harmony.Unpatch(original, HarmonyPatchType.All, _harmony.Id);
            }
            _patchProfiling.TryRemove(type.Name, out _);
            FLog.Debug($"[Harmony] Unpatched: {type.Name}");
        }
        catch (Exception ex) { FLog.Error($"[Harmony] Unpatch({type.Name}) failed: {ex.Message}"); }
    }

    private IEnumerable<MethodBase> PatchedMethods()
    {
        try { return Harmony.GetAllPatchedMethods(); }
        catch { return Array.Empty<MethodBase>(); }
    }
    #endregion

    #region 3. Internal Logic (The Safe Engine)
    private void InitializeRegistry()
    {
        try
        {
            // [Strict Isolation] สแกนเฉพาะมอดเราเท่านั้น ห้ามออกไปข้างนอก!
            var myAssembly = Assembly.GetExecutingAssembly();
            var allTypes = GetLoadableTypes(myAssembly);

            foreach (var type in allTypes)
            {
                if (type == null || type.Namespace == null || !type.Namespace.StartsWith(_namespaceFilter)) continue;
                if (type.IsAbstract || !type.IsDefined(typeof(HarmonyPatch), true)) continue;

                // ตรวจสอบ Priority แบบ Manual เพื่อเลี่ยง AccessTools ที่ขี้บ่น
                int priority = 400;
                var prioAttr = type.GetCustomAttribute<HarmonyPriority>();
                if (prioAttr != null)
                {
                    // ใช้ Reflection ดึงค่า priority ออกมาตรงๆ
                    var prop = typeof(HarmonyPriority).GetField("priority", Util.UniversalFlags);
                    if (prop != null) priority = (int)prop.GetValue(prioAttr);
                }

                if (priority < 400) _criticalRegistry.Add(type);
                else _featureRegistry.Add(type);
            }
            FLog.Info($"[Harmony] Registry built: {ActivePatchCount} modules found.");
        }
        catch (Exception ex) { FLog.Error($"[Scanner] Fatal Error: {ex.Message}"); }
    }

    private IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        if (assembly == null) return Array.Empty<Type>();
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            // คืนค่าเฉพาะตัวที่โหลดได้ (ข้ามตัวพังๆ ไปเงียบๆ)
            return e.Types.Where(t => t != null && t.Namespace != null && t.Namespace.StartsWith(_namespaceFilter))!;
        }
        catch { return Array.Empty<Type>(); }
    }

    private void ApplySinglePatch(Type type)
    {
        if (type == null) return;
        var timer = Stopwatch.StartNew();
        try
        {
            // เช็ก Prepare()
            var prepare = type.GetMethod("Prepare", Util.UniversalFlags);
            if (prepare != null)
            {
                var shouldPatch = prepare.Invoke(null, new object[] { _harmony });
                if (shouldPatch is bool result && !result) return;
            }

            // [Surgical Patching] ใช้ ClassProcessor เพื่อห้าม Harmony ไปสแกน Assembly อื่นเพิ่ม
            var processor = _harmony.CreateClassProcessor(type);
            processor.Patch();

            timer.Stop();
            _patchProfiling.AddOrUpdate(type.Name, timer.ElapsedMilliseconds, (_, _) => timer.ElapsedMilliseconds);
            if (FLog.IsDeveloperContext) FLog.Debug($"[Harmony] Applied: {type.Name}");
        }
        catch (Exception ex)
        {
            // Distinguish "method not found in this game build" from real failures
            bool isNullMethod = ex.Message.Contains("in method null")
                             || ex.Message.Contains("Patching exception in method null")
                             || (ex.InnerException?.Message.Contains("null") == true && ex.Message.Contains("Patching"));

            if (isNullMethod)
                FLog.Debug($"[Harmony] {type.Name}: optional method absent in this build — skipped.");
            else
                FLog.Error($"[Harmony] Failure in {type.Name}: {ex.Message}");
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
    }
    #endregion
}