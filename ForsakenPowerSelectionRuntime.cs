using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace BossRules;

internal static class ForsakenPowerSelectionRuntime
{
    private const float GuardianPowerHintOffsetDown = 16f;

    private static readonly Dictionary<string, int> GuardianPowerOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        { "GP_Eikthyr", 0 },
        { "GP_TheElder", 1 },
        { "GP_Bonemass", 2 },
        { "GP_Moder", 3 },
        { "GP_Yagluth", 4 },
        { "GP_Queen", 5 },
        { "GP_Fader", 6 },
        { "GP_DeepNorth", 7 },
        { "SE_Boss_Brutalis", 8 },
        { "SE_Boss_Gorr", 9 },
        { "SE_Boss_StormHerald", 10 },
        { "SE_Boss_Sythrak", 11 }
    };

    private static TMP_Text? _guardianPowerHintText;
    private static int _guardianPowerHintHudInstanceId;
    private static readonly List<string> CachedUnlockedGuardianPowers = new();
    private static int _cachedUnlockedGuardianPowersPlayerInstanceId;
    private static bool _unlockedGuardianPowersDirty = true;
    private static KeyboardShortcut _cachedHintShortcut;
    private static string _cachedHintRotateLabel = "";
    private static string _cachedHintText = "";

    internal static void InvalidateGuardianPowerCache()
    {
        _unlockedGuardianPowersDirty = true;
    }

    internal static void Shutdown()
    {
        DestroyGuardianPowerHint();
        CachedUnlockedGuardianPowers.Clear();
        _cachedUnlockedGuardianPowersPlayerInstanceId = 0;
        _unlockedGuardianPowersDirty = true;
        _cachedHintShortcut = default;
        _cachedHintRotateLabel = "";
        _cachedHintText = "";
    }

    internal static void TryRotateSelection(Player? player)
    {
        if (!CanRotateSelection(player))
        {
            return;
        }

        List<string> unlockedPowers = GetCachedUnlockedGuardianPowers(player!);
        if (unlockedPowers.Count == 0)
        {
            player!.Message(MessageHud.MessageType.TopLeft, "No unlocked Forsaken Powers available.");
            return;
        }

        string currentPower = player!.GetGuardianPowerName() ?? "";
        if (unlockedPowers.Count == 1 && string.Equals(unlockedPowers[0], currentPower, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int currentIndex = unlockedPowers.FindIndex(powerName => string.Equals(powerName, currentPower, StringComparison.OrdinalIgnoreCase));
        int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % unlockedPowers.Count : 0;
        string nextPower = unlockedPowers[nextIndex];
        player.SetGuardianPower(nextPower);
        player.Message(MessageHud.MessageType.TopLeft, $"Forsaken Power: {GetDisplayName(nextPower)}");
    }

    internal static void UpdateHudHint(Hud? hud, Player? player)
    {
        if (hud == null)
        {
            return;
        }

        bool shouldShow = ShouldShowHudHint(hud, player);
        if (!shouldShow)
        {
            HideGuardianPowerHint(hud);
            return;
        }

        TMP_Text hintText = EnsureGuardianPowerHint(hud);
        if (!hintText.gameObject.activeSelf)
        {
            hintText.gameObject.SetActive(true);
        }

        ApplyGuardianPowerHintLayout(hud, hintText);
        string hint = GetCachedHintText();
        if (!string.Equals(hintText.text, hint, StringComparison.Ordinal))
        {
            hintText.text = hint;
        }
    }

    private static bool CanRotateSelection(Player? player)
    {
        if (!BossRulesConfig.IsRemoteForsakenPowerSelectionEnabled() ||
            player == null ||
            player != Player.m_localPlayer ||
            !IsShortcutDown(BossRulesConfig.GetRotateForsakenPowerShortcut()))
        {
            return false;
        }

        if (player.IsDead() || player.InCutscene() || player.IsTeleporting())
        {
            return false;
        }

        if ((Chat.instance != null && Chat.instance.HasFocus()) ||
            Console.IsVisible() ||
            TextInput.IsVisible() ||
            StoreGui.IsVisible() ||
            InventoryGui.IsVisible() ||
            Menu.IsVisible() ||
            (TextViewer.instance != null && TextViewer.instance.IsVisible()) ||
            Minimap.IsOpen() ||
            Minimap.InTextInput() ||
            GameCamera.InFreeFly() ||
            PlayerCustomizaton.IsBarberGuiVisible() ||
            UnifiedPopup.IsVisible() ||
            Hud.InRadial() ||
            Hud.IsPieceSelectionVisible())
        {
            return false;
        }

        return true;
    }

    private static List<string> BuildUnlockedGuardianPowers(Player player)
    {
        List<string> unlockedPowers = BossStonePerPlayerRuntime.GetUnlockedGuardianPowerNames(player);
        unlockedPowers.RemoveAll(powerName => powerName.Length == 0 || ObjectDB.instance?.GetStatusEffect(powerName.GetStableHashCode()) == null);
        unlockedPowers.Sort(CompareGuardianPowerNames);
        return unlockedPowers;
    }

    private static List<string> GetCachedUnlockedGuardianPowers(Player player)
    {
        int playerInstanceId = player.GetInstanceID();
        if (_unlockedGuardianPowersDirty ||
            _cachedUnlockedGuardianPowersPlayerInstanceId != playerInstanceId)
        {
            CachedUnlockedGuardianPowers.Clear();
            CachedUnlockedGuardianPowers.AddRange(BuildUnlockedGuardianPowers(player));
            _cachedUnlockedGuardianPowersPlayerInstanceId = playerInstanceId;
            _unlockedGuardianPowersDirty = false;
        }

        return CachedUnlockedGuardianPowers;
    }

    private static int CompareGuardianPowerNames(string left, string right)
    {
        int leftOrder = GetGuardianPowerOrder(left);
        int rightOrder = GetGuardianPowerOrder(right);
        int orderComparison = leftOrder.CompareTo(rightOrder);
        if (orderComparison != 0)
        {
            return orderComparison;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetGuardianPowerOrder(string guardianPowerName)
    {
        return GuardianPowerOrder.TryGetValue(guardianPowerName ?? "", out int order) ? order : int.MaxValue;
    }

    private static string GetDisplayName(string guardianPowerName)
    {
        string displayName = ObjectDB.instance?.GetStatusEffect(guardianPowerName.GetStableHashCode())?.m_name ?? guardianPowerName;
        return Localization.instance != null ? Localization.instance.Localize(displayName) : displayName;
    }

    private static bool ShouldShowHudHint(Hud hud, Player? player)
    {
        KeyboardShortcut shortcut = BossRulesConfig.GetRotateForsakenPowerShortcut();
        if (!BossRulesConfig.IsRemoteForsakenPowerSelectionEnabled() ||
            player == null ||
            player != Player.m_localPlayer ||
            shortcut.MainKey == KeyCode.None ||
            hud.m_gpRoot == null ||
            !hud.m_gpRoot.gameObject.activeSelf)
        {
            return false;
        }

        return GetCachedUnlockedGuardianPowers(player).Count > 0;
    }

    private static TMP_Text EnsureGuardianPowerHint(Hud hud)
    {
        int hudInstanceId = hud.GetInstanceID();
        if (_guardianPowerHintText != null &&
            _guardianPowerHintHudInstanceId == hudInstanceId &&
            _guardianPowerHintText.gameObject != null)
        {
            return _guardianPowerHintText;
        }

        DestroyGuardianPowerHint();
        TMP_Text hintTemplate = hud.m_gpCooldown != null ? hud.m_gpCooldown : hud.m_gpName;
        _guardianPowerHintText = UnityEngine.Object.Instantiate(hintTemplate, hud.m_gpRoot);
        _guardianPowerHintText.name = "BossRules_ForsakenRotateHint";
        _guardianPowerHintText.text = "";
        _guardianPowerHintText.textWrappingMode = TextWrappingModes.NoWrap;
        _guardianPowerHintText.fontSize = Mathf.Max(12f, hintTemplate.fontSize - 2f);
        _guardianPowerHintText.alignment = TextAlignmentOptions.Center;

        RectTransform hintRect = _guardianPowerHintText.rectTransform;
        hintRect.SetParent(hud.m_gpRoot, false);
        _guardianPowerHintHudInstanceId = hudInstanceId;
        return _guardianPowerHintText;
    }

    private static void DestroyGuardianPowerHint()
    {
        if (_guardianPowerHintText != null && _guardianPowerHintText.gameObject != null)
        {
            _guardianPowerHintText.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(_guardianPowerHintText.gameObject);
        }

        _guardianPowerHintText = null;
        _guardianPowerHintHudInstanceId = 0;
    }

    private static void HideGuardianPowerHint(Hud hud)
    {
        if (_guardianPowerHintText != null &&
            _guardianPowerHintHudInstanceId == hud.GetInstanceID() &&
            _guardianPowerHintText.gameObject != null &&
            _guardianPowerHintText.gameObject.activeSelf)
        {
            _guardianPowerHintText.gameObject.SetActive(false);
        }
    }

    private static void ApplyGuardianPowerHintLayout(Hud hud, TMP_Text hintText)
    {
        RectTransform hintRect = hintText.rectTransform;
        RectTransform iconRect = hud.m_gpIcon.rectTransform;
        hintRect.anchorMin = iconRect.anchorMin;
        hintRect.anchorMax = iconRect.anchorMax;
        hintRect.pivot = new Vector2(0.5f, 1f);
        hintRect.anchoredPosition = new Vector2(
            iconRect.anchoredPosition.x,
            iconRect.anchoredPosition.y - iconRect.rect.height * 0.5f - GuardianPowerHintOffsetDown);
        hintRect.sizeDelta = new Vector2(Mathf.Max(iconRect.rect.width + 72f, 128f), hintRect.sizeDelta.y);
    }

    private static string GetCachedHintText()
    {
        KeyboardShortcut shortcut = BossRulesConfig.GetRotateForsakenPowerShortcut();
        string rotateLabel = BossRulesLocalization.Text(
            BossRulesLocalization.MessageForsakenPowerRotateKey);
        if (_cachedHintText.Length == 0 ||
            !_cachedHintShortcut.Equals(shortcut) ||
            !string.Equals(_cachedHintRotateLabel, rotateLabel, StringComparison.Ordinal))
        {
            _cachedHintShortcut = shortcut;
            _cachedHintRotateLabel = rotateLabel;
            _cachedHintText = $"[{GetShortcutLabel(shortcut)}] {rotateLabel}";
        }

        return _cachedHintText;
    }

    private static string GetShortcutLabel(KeyboardShortcut shortcut)
    {
        List<string> keys = shortcut.Modifiers.Select(GetKeyLabel).ToList();
        if (shortcut.MainKey != KeyCode.None)
        {
            keys.Add(GetKeyLabel(shortcut.MainKey));
        }

        return string.Join("+", keys.Where(label => label.Length > 0));
    }

    private static string GetKeyLabel(KeyCode keyCode)
    {
        return keyCode switch
        {
            KeyCode.LeftControl => "Ctrl",
            KeyCode.RightControl => "Ctrl",
            KeyCode.LeftShift => "Shift",
            KeyCode.RightShift => "Shift",
            KeyCode.LeftAlt => "Alt",
            KeyCode.RightAlt => "Alt",
            KeyCode.Alpha0 => "0",
            KeyCode.Alpha1 => "1",
            KeyCode.Alpha2 => "2",
            KeyCode.Alpha3 => "3",
            KeyCode.Alpha4 => "4",
            KeyCode.Alpha5 => "5",
            KeyCode.Alpha6 => "6",
            KeyCode.Alpha7 => "7",
            KeyCode.Alpha8 => "8",
            KeyCode.Alpha9 => "9",
            _ => keyCode.ToString()
        };
    }

    private static bool IsShortcutDown(KeyboardShortcut shortcut)
    {
        return shortcut.MainKey != KeyCode.None &&
               Input.GetKeyDown(shortcut.MainKey) &&
               shortcut.Modifiers.All(Input.GetKey);
    }
}
