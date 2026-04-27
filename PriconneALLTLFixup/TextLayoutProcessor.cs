using System;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace PriconneALLTLFixup;

public class TextLayoutProcessor
{
    #region 1. Thread-Safe Caching & Global State
    private static readonly object _syncRoot = new();

    private static readonly Dictionary<int, float> _advanceCache = new(2048);
    private static readonly Dictionary<int, string> _layoutCache = new(512);
    private static readonly List<int> _lruHistory = new(512);
    #endregion

    #region 2. Readonly Internal Fields
    private readonly TextMesh _component;
    private readonly Font _font;
    private readonly Renderer _renderer;

    private readonly StringBuilder _buffer = new(2048);
    #endregion

    #region 3. Formal Properties
    public float ContentWidth => MeasureContent(_component.text);

    public float ViewportHeight => _renderer.bounds.size.y;
    #endregion

    #region 4. Professional Constructor
    public TextLayoutProcessor(TextMesh mesh)
    {
        _component = mesh ?? throw new ArgumentNullException(nameof(mesh));
        _font = mesh.font;
        _renderer = mesh.GetComponent<Renderer>();

        if (_font == null)
            Log.Error($"[Layout] Component '{mesh.name}' is missing a valid Font reference.");
    }
    #endregion

    #region 5. Primary Layout API
    public float MeasureContent(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        float total = 0;
        float scale = CalculateEffectiveScale();
        bool skippingTag = false;

        foreach (char c in text)
        {
            if (c == '[') { skippingTag = true; continue; }
            if (c == ']' && skippingTag) { skippingTag = false; continue; }
            if (skippingTag) continue;

            total += MeasureGlyph(c) * scale;
        }
        return total;
    }

    public void ApplyAdaptiveLayout(float maxWidth)
    {
        string raw = _component.text;
        if (string.IsNullOrEmpty(raw) || maxWidth <= 0) return;

        int layoutHash = raw.GetHashCode() ^ maxWidth.GetHashCode();
        lock (_syncRoot)
        {
            if (_layoutCache.TryGetValue(layoutHash, out var cached) && cached is not null)
            {
                if (_component.text != cached) _component.text = cached;
                return;
            }
        }

        Log.Debug($"Processing layout for: {raw.Length} chars");

        float scale = CalculateEffectiveScale();
        float currentX = 0f;
        int lastBreakableIndex = -1;
        float widthAtLastBreak = 0f;
        bool inTag = false;

        _buffer.Clear();
        ReadOnlySpan<char> span = raw.AsSpan();

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];

            if (c == '[') { inTag = true; _buffer.Append(c); continue; }
            if (c == ']' && inTag) { inTag = false; _buffer.Append(c); continue; }
            if (inTag) { _buffer.Append(c); continue; }

            if (c == '\n') { currentX = 0; lastBreakableIndex = -1; _buffer.Append(c); continue; }

            if (IsNonSpacingGlyph(c)) { _buffer.Append(c); continue; }

            float charW = MeasureGlyph(c) * scale;

            if (char.IsWhiteSpace(c))
            {
                lastBreakableIndex = _buffer.Length;
                widthAtLastBreak = currentX;
            }

            if (currentX + charW > maxWidth)
            {
                if (lastBreakableIndex != -1)
                {
                    _buffer[lastBreakableIndex] = '\n';
                    currentX -= widthAtLastBreak;
                    lastBreakableIndex = -1;
                }
                else
                {
                    _buffer.Append('\n');
                    currentX = 0;
                }
            }

            _buffer.Append(c);
            currentX += charW;
        }

        string finalResult = _buffer.ToString();
        CacheLayoutResult(layoutHash, finalResult);
        _component.text = finalResult;
    }
    #endregion

    #region 6. Internal Optimization Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNonSpacingGlyph(char c)
    {
        UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
        return cat == UnicodeCategory.NonSpacingMark ||
               cat == UnicodeCategory.SpacingCombiningMark ||
               cat == UnicodeCategory.EnclosingMark;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float CalculateEffectiveScale()
    {
        if (_font == null || _font.fontSize == 0) return _component.characterSize;

        return (_component.fontSize == 0 ? 1f : (float)_component.fontSize / _font.fontSize) * _component.characterSize * _component.transform.lossyScale.x;
    }

    private float MeasureGlyph(char c)
    {
        if (_font == null || IsNonSpacingGlyph(c)) return 0;

        int glyphKey = c ^ (_component.fontSize << 16) ^ (int)_component.fontStyle;

        lock (_syncRoot)
        {
            if (_advanceCache.TryGetValue(glyphKey, out float w)) return w;
        }

        if (_font.GetCharacterInfo(c, out CharacterInfo info, _component.fontSize, _component.fontStyle))
        {
            lock (_syncRoot) { _advanceCache[glyphKey] = info.advance; }
            return info.advance;
        }

        return 0;
    }

    private static void CacheLayoutResult(int key, string val)
    {
        lock (_syncRoot)
        {
            if (!_layoutCache.ContainsKey(key))
            {
                if (_layoutCache.Count >= 512)
                {
                    int cleanCount = Math.Min(50, _lruHistory.Count);
                    for (int i = 0; i < cleanCount; i++)
                    {
                        _layoutCache.Remove(_lruHistory[0]);
                        _lruHistory.RemoveAt(0);
                    }
                    Log.Debug($"[Memory] Layout cache purged {cleanCount} items.");
                }
                _lruHistory.Add(key);
            }
            _layoutCache[key] = val;
        }
    }
    #endregion
}