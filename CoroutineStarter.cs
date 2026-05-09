using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.Injection;
using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PriconneALLTLFixup;

public class CoroutineStarter : MonoBehaviour
{
    #region 1. Thread-Safe Internal State
    private static CoroutineStarter _instance;
    private static readonly object _syncRoot = new();
    private static int _mainThreadId;

    private static readonly Queue<Action> _pendingQueue = new(32);
    private static readonly List<Action> _executionBatch = new(32);

    private static readonly Dictionary<float, WaitForSeconds> _waitCache = new(16);
    #endregion

    #region 2. Formal Properties & Events
    public static event Action OnFrameUpdate;
    public static event Action OnFrameLateUpdate;
    public static event Action OnFrameFixedUpdate;

    public static bool IsMainThread => Environment.CurrentManagedThreadId == _mainThreadId;
    #endregion

    #region 3. Core Engine Initialization
    private static bool _isQuitting = false;

    public static void SetupMainThread()
    {
        if (_mainThreadId != 0) return;
        _mainThreadId = Environment.CurrentManagedThreadId;

        FLog.Debug($"Core Thread ID defined: {_mainThreadId}");
    }

    public static CoroutineStarter Instance
    {
        get
        {
            if (_instance != null || _isQuitting) return _instance!;
            lock (_syncRoot)
            {
                if (_instance != null) return _instance;

                if (_mainThreadId == 0) SetupMainThread();

                if (!ClassInjector.IsTypeRegisteredInIl2Cpp<CoroutineStarter>())
                    ClassInjector.RegisterTypeInIl2Cpp<CoroutineStarter>();

                var container = new GameObject("PriconneTL_CoroutineCore");
                container.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(container);

                _instance = container.AddComponent<CoroutineStarter>();
                FLog.Info("[System] Coroutine Engine Started.");
                return _instance;
            }
        }
    }
    #endregion

    #region 4. Logic Flow: Unity Lifecycle
    public void OnApplicationQuit()
    {
        _isQuitting = true;
        FLog.Info("[System] Coroutine Engine shutting down (Application Quit).");
    }

    private void Update()
    {
        lock (_pendingQueue)
        {
            if (_pendingQueue.Count > 0)
            {
                _executionBatch.Clear();
                while (_pendingQueue.Count > 0)
                    _executionBatch.Add(_pendingQueue.Dequeue());
            }
        }

        for (int i = 0; i < _executionBatch.Count; i++)
            InvokeSafe(_executionBatch[i]);

        OnFrameUpdate?.Invoke();
    }

    private void LateUpdate() => OnFrameLateUpdate?.Invoke();

    private void FixedUpdate() => OnFrameFixedUpdate?.Invoke();

    public void OnDestroy()
    {
        _isQuitting = true;
        if (_instance == this) _instance = null;
        _waitCache.Clear();
        FLog.Debug("CoroutineStarter engine stopped and memory released.");
    }
    #endregion

    #region 5. Public API
    public static void Dispatch(Action action)
    {
        if (action == null) return;

        if (IsMainThread)
        {
            action();
        }
        else
        {
            lock (_pendingQueue) { _pendingQueue.Enqueue(action); }
            _ = Instance;
        }
    }

    public static Coroutine Run(IEnumerator routine)
    {
        if (routine == null) return null!;
        return Instance.StartCoroutine(routine.WrapToIl2Cpp());
    }

    public static void Stop(Coroutine routine)
    {
        if (routine != null) Instance.StopCoroutine(routine);
    }

    public static void DelayExecute(float seconds, Action action)
    {
        if (action == null) return;
        Run(DelayedRoutine(seconds, action));
    }
    #endregion

    #region 6. Internal Robust Logic

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InvokeSafe(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            FLog.Error($"[Coroutine] Task execution error: {ex.Message}");
            FLog.Debug(ex);
        }
    }

    private static IEnumerator DelayedRoutine(float seconds, Action action)
    {
        float roundedSeconds = (float)Math.Round(seconds, 2);
        WaitForSeconds wait;

        lock (_syncRoot)
        {
            if (!_waitCache.TryGetValue(roundedSeconds, out wait!))
            {
                wait = new WaitForSeconds(roundedSeconds);

                if (_waitCache.Count > 100)
                {
                    _waitCache.Clear();
                    FLog.Debug("[Coroutine] Wait cache exceeded limit and was cleared.");
                }

                _waitCache[roundedSeconds] = wait;
            }
        }

        yield return wait;
        InvokeSafe(action);
    }
    #endregion
}