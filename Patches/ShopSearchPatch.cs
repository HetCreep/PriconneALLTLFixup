#nullable enable
using Elements;
using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Restores and enhances the shop material search filter.
/// The original decompiler-generated Prefix had an empty body due to a decompilation failure.
/// This implementation reconstructs the search predicate with full multi-language support:
/// uses <see cref="StringComparison.OrdinalIgnoreCase"/> for Latin scripts and
/// <see cref="StringComparison.CurrentCultureIgnoreCase"/> for Unicode-aware matching.
/// </summary>
/// <remarks>
/// Patches the compiler-generated lambda <c>_MaterialFilterBySearchText_b__109_0</c>
/// which is invoked as a LINQ predicate inside <c>PartsShopFooter.MaterialFilterBySearchText</c>.
/// By supplying a Prefix that sets <c>__result</c> and returns <c>false</c> we skip the
/// original IL and provide our own matching logic.
/// </remarks>
[HarmonyPatch]
public static class ShopSearchPatch
{
    #region 1. Reflection Cache (lazy, thread-safe)
    // PartsShopFooter holds the active search string in a field discovered at runtime.
    // We cache the accessor after the first successful lookup to avoid repeated reflection.
    private static volatile Func<PartsShopFooter, string?>? _getSearchText;
    private static readonly object _reflectLock = new();
    #endregion

    #region 2. Hook
    /// <summary>
    /// Prefix for the shop item search predicate.
    /// Evaluates whether <paramref name="item"/> matches the current search query
    /// and writes the result into <paramref name="__result"/>.
    /// </summary>
    [HarmonyPatch(typeof(PartsShopFooter), "_MaterialFilterBySearchText_b__109_0")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixMaterialFilter(
        PartsShopFooter __instance,
        ShopItem        item,
        ref bool        __result)
    {
        if (!ConfigManager.UI.MultiLangSearch.Value)
        {
            __result = true;
            return true; // pass through to original
        }
        if (!__instance.IsSafe() || !item.IsSafe())
        {
            __result = true; // safe default: show item when instance is invalid
            return false;
        }

        string query = GetSearchText(__instance);

        // Empty query → show everything
        if (string.IsNullOrWhiteSpace(query))
        {
            __result = true;
            return false;
        }

        // Build the candidate strings for matching.
        // ShopItem exposes localised display names through several paths; we try them all
        // so that translated (non-Japanese) names are matched correctly.
        __result = MatchesQuery(item, query);
        return false;
    }
    #endregion

    #region 3. Search Logic
    /// <summary>
    /// Returns <c>true</c> when any displayable name on <paramref name="item"/>
    /// contains <paramref name="query"/> using a case/culture-insensitive comparison.
    /// </summary>
    private static bool MatchesQuery(ShopItem item, string query)
    {
        // Primary: the rendered label text already set by the UI (reflects translations)
        string? labelText = TryGetLabelText(item);
        if (labelText != null && ContainsIgnoreCase(labelText, query)) return true;

        // Fallback 1: ItemName property (raw asset name)
        string? itemName = TryGetProperty<string>(item, "ItemName")
                        ?? TryGetProperty<string>(item, "viewName")
                        ?? TryGetProperty<string>(item, "Name");
        if (itemName != null && ContainsIgnoreCase(itemName, query)) return true;

        // Fallback 2: Description / sub-text
        string? desc = TryGetProperty<string>(item, "Description")
                    ?? TryGetProperty<string>(item, "ItemDescription");
        if (desc != null && ContainsIgnoreCase(desc, query)) return true;

        return false;
    }

    /// <summary>
    /// Culture-aware, case-insensitive contains check.
    /// For scripts where <c>OrdinalIgnoreCase</c> is insufficient (Thai, Arabic, CJK, etc.)
    /// we fall back to <c>CurrentCultureIgnoreCase</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsIgnoreCase(string source, string query)
    {
        // Fast path: ordinal (ASCII, Latin, Cyrillic, most CJK)
        if (source.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

        // Slow path: full Unicode culture-aware folding (Thai combining marks, Arabic forms, etc.)
        return source.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
    #endregion

    #region 4. Reflection Helpers
    /// <summary>
    /// Reads the current search query string from the footer's input field.
    /// Tries known field names first, then falls back to reflection discovery.
    /// </summary>
    private static string GetSearchText(PartsShopFooter footer)
    {
        // Try compiled accessor if already resolved
        if (_getSearchText != null)
            return _getSearchText(footer) ?? string.Empty;

        // Resolve field once and compile a fast delegate
        lock (_reflectLock)
        {
            if (_getSearchText != null)
                return _getSearchText(footer) ?? string.Empty;

            var accessor = BuildSearchTextAccessor();
            _getSearchText = accessor;
            return accessor?.Invoke(footer) ?? string.Empty;
        }
    }

    private static Func<PartsShopFooter, string?>? BuildSearchTextAccessor()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = typeof(PartsShopFooter);

        // Common field-name patterns for search-text storage in Elements UI components
        string[] candidates = { "searchText", "_searchText", "m_SearchText", "inputText",
                                 "searchInputText", "m_InputText", "filterText", "_filterText" };

        foreach (var name in candidates)
        {
            var field = type.GetField(name, flags);
            if (field?.FieldType == typeof(string))
            {
                FLog.Debug($"[ShopSearch] Resolved search-text field: {name}");
                return inst => (string?)field.GetValue(inst);
            }

            // Also check properties
            var prop = type.GetProperty(name, flags);
            if (prop?.PropertyType == typeof(string))
            {
                FLog.Debug($"[ShopSearch] Resolved search-text property: {name}");
                return inst => (string?)prop.GetValue(inst);
            }
        }

        // Last resort: find any string field whose name contains "search" or "filter"
        foreach (var field in type.GetFields(flags))
        {
            if (field.FieldType != typeof(string)) continue;
            string fn = field.Name.ToLowerInvariant();
            if (fn.Contains("search") || fn.Contains("filter"))
            {
                FLog.Debug($"[ShopSearch] Auto-discovered search field: {field.Name}");
                return inst => (string?)field.GetValue(inst);
            }
        }

        FLog.Warn("[ShopSearch] Could not resolve search-text field on PartsShopFooter — filter disabled.");
        return _ => null;
    }

    private static string? TryGetLabelText(ShopItem item)
    {
        // ShopItem may expose its display label as a UILabel child component;
        // we probe common property names instead of hard-wiring one.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var name in new[] { "nameLabel", "itemNameLabel", "label", "titleLabel" })
        {
            var field = item.GetType().GetField(name, flags);
            if (field?.GetValue(item) is CustomUILabel lbl && lbl.IsSafe())
                return lbl.text;
        }
        return null;
    }

    /// <summary>
    /// Reflectively reads a property or field of type <typeparamref name="T"/> from
    /// <paramref name="obj"/>. Returns <c>null</c> when the member is missing or the
    /// underlying access throws — the failure is logged via <see cref="FLog.Debug"/>
    /// (developer-only) so reflection mismatches surface without being noisy in
    /// production.
    /// </summary>
    private static T? TryGetProperty<T>(object obj, string name) where T : class
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            var member = obj.GetType().GetProperty(name, flags)
                         ?? (MemberInfo?)obj.GetType().GetField(name, flags);
            if (member == null)
            {
                if (FLog.IsDeveloperContext)
                    FLog.Debug($"[Shop] Member '{name}' not found on '{obj.GetType().Name}'.");
                return null;
            }
            return member switch
            {
                PropertyInfo p => p.GetValue(obj) as T,
                FieldInfo    f => f.GetValue(obj) as T,
                _              => null
            };
        }
        catch (Exception ex)
        {
            if (FLog.IsDeveloperContext)
                FLog.Debug($"[Shop] Reflective access to '{name}' on '{obj.GetType().Name}' threw: {ex.Message}");
            return null;
        }
    }
    #endregion

    // =========================================================================
    // REGION 2: Item-Select Dialog Search (ItemSelectSearchPatch)
    // =========================================================================

    #region 2. Item-Select Search
    /// <summary>
    /// Prefix (replaces) on <c>PartsDialogItemSelect.searchItemExec</c>:
    /// filters the displayed material / super-material list against the current search word
    /// using <see cref="StringComparison.CurrentCultureIgnoreCase"/> so that translated
    /// item names in any language are matched correctly.
    /// The original <c>MatchesEnglishSearch</c> helper could not be decompiled;
    /// this reconstruction performs a culture-aware substring match directly.
    /// Returns <see langword="false"/> to replace the original method.
    /// </summary>
    [HarmonyPatch(typeof(PartsDialogItemSelect), "searchItemExec")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixItemSelectSearch(PartsDialogItemSelect __instance)
    {
        if (!__instance.IsSafe()) return true;

        __instance.searchItemList.Clear();
        string query = __instance.searchWord?.ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query)) return true;

        switch (__instance.tabIndex)
        {
            case PartsDialogItemSelect.eTabIndex.MEMORY_PIECE:
                __instance.sortMaterial();
                for (int i = 0; i < __instance.dispMaterialParamList.Count; i++)
                {
                    var param = __instance.dispMaterialParamList[i];
                    if (MatchesTranslatedItem(param, query))
                        __instance.searchItemList.Add(param);
                }
                __instance.dispMaterialParamList = __instance.searchItemList;
                return false;

            case PartsDialogItemSelect.eTabIndex.SUPER_MEMORY_PIECE:
                __instance.sortSuperMaterial();
                for (int j = 0; j < __instance.dispSuperMaterialParamList.Count; j++)
                {
                    var param = __instance.dispSuperMaterialParamList[j];
                    if (MatchesTranslatedItem(param, query))
                        __instance.searchItemList.Add(param);
                }
                __instance.dispSuperMaterialParamList = __instance.searchItemList;
                return false;

            default:
                return true;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the item's translated (or original) name
    /// contains <paramref name="query"/> using culture-aware, case-insensitive comparison.
    /// Uses reflection to probe common name properties across game versions.
    /// Supports all Unicode scripts (Thai, CJK, Latin, etc.).
    /// </summary>
    private static bool MatchesTranslatedItem(object? param, string query)
    {
        if (param == null) return false;

        // Probe common name-bearing properties used by MasterItemParam / MasterUnitParam
        string? name = TryGetProperty<string>(param, "ItemName")
                    ?? TryGetProperty<string>(param, "UnitName")
                    ?? TryGetProperty<string>(param, "Name")
                    ?? TryGetProperty<string>(param, "viewName");

        if (name == null) return false;
        return name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
    #endregion

    // =========================================================================
    // REGION 3: Shop Search Format Pass-Through (ShopSearchFormatPatch)
    // =========================================================================

    #region 3. Shop Search Format
    /// <summary>
    /// Prefix (replaces) on <c>ShopUtility.GetMaterialSearchTextFormat</c>:
    /// bypasses the Japanese format template and returns the raw source string unchanged.
    /// This ensures that translated item names are searched as entered rather than being
    /// re-formatted with Japanese-specific placeholder syntax.
    /// Returns <see langword="false"/> to skip the original method.
    /// </summary>
    [HarmonyPatch(typeof(ShopUtility), "GetMaterialSearchTextFormat")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixShopSearchFormat(string _source, ref string __result)
    {
        __result = _source;
        return false;
    }
    #endregion
}
