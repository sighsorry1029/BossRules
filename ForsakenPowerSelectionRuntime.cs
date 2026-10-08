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
    private static int _cachedUnlockedGuardianPowersObjectDbInstanceId;
    private static readonly ForsakenPowerShortcutInput ShortcutInput = new();
    private static KeyboardShortcut _pressedShortcut;
    private static Player? _pressedPlayer;
    private static Hud? _pressedHud;
    private static KeyboardShortcut _cachedHintShortcut;
    private static string _cachedHintLabel = "";
    private static bool _cachedHintRadialOpen;
    private static string _cachedHintText = "";

    internal static void InvalidateGuardianPowerCache()
    {
        _unlockedGuardianPowersDirty = true;
    }

    internal static void Shutdown()
    {
        CancelInput();
        ShortcutInput.Reset();
        DestroyGuardianPowerHint();
        CachedUnlockedGuardianPowers.Clear();
        _cachedUnlockedGuardianPowersPlayerInstanceId = 0;
        _cachedUnlockedGuardianPowersObjectDbInstanceId = 0;
        _unlockedGuardianPowersDirty = true;
        _cachedHintShortcut = default;
        _cachedHintLabel = "";
        _cachedHintText = "";
    }

    internal static void UpdateInput()
    {
        ForsakenPowerRadial.PollLifetime();
        Player? player = Player.m_localPlayer;
        Hud? hud = Hud.instance;
        KeyboardShortcut configured = BossRulesConfig.GetRotateForsakenPowerShortcut();
        if (ShortcutInput.IsIdle)
        {
            _pressedShortcut = configured;
            _pressedPlayer = player;
            _pressedHud = hud;
        }

        KeyCode key = _pressedShortcut.MainKey;
        bool held = key != KeyCode.None && Input.GetKey(key);
        // Modifiers qualify a new shortcut press; releasing them never closes an
        // already open menu. Keep the captured binding until this session ends.
        bool down = key != KeyCode.None && Input.GetKeyDown(key) && _pressedShortcut.Modifiers.All(Input.GetKey);
        if (ShortcutInput.IsIdle && !down) return;
        bool allowed = CanUseSelection(player, hud) && player == _pressedPlayer && hud == _pressedHud &&
                       configured.Equals(_pressedShortcut) &&
                       !Input.GetKeyDown(KeyCode.Escape) && !Input.GetMouseButtonUp(1) &&
                       !ZInput.GetButton("OpenRadial") && !ZInput.GetButton("OpenEmote") &&
                       !ZInput.GetButtonDown("JoyRadial") && !ZInput.IsExclusiveGamepadActive() &&
                       (!ShortcutInput.IsOpen || ForsakenPowerRadial.IsOpen);

        ForsakenPowerShortcutInput.Action action = ShortcutInput.Step(allowed, down, held,
            Input.GetMouseButtonDown(0), Input.GetMouseButtonUp(0));
        switch (action)
        {
            case ForsakenPowerShortcutInput.Action.Open:
                InvalidateGuardianPowerCache();
                List<string> powers = GetCachedUnlockedGuardianPowers(player!);
                if (powers.Count == 0)
                {
                    ShowNoUnlockedPowers(player!);
                    CancelInput();
                }
                else if (!ForsakenPowerRadial.TryOpen(hud!, player!, powers))
                {
                    CancelInput();
                }
                break;
            case ForsakenPowerShortcutInput.Action.Commit:
                ForsakenPowerRadial.SelectAndClose(player!);
                break;
            case ForsakenPowerShortcutInput.Action.Cancel:
                ForsakenPowerRadial.Close();
                break;
        }
        ForsakenPowerRadial.UpdateDescription();
        if (ShortcutInput.IsIdle)
        {
            _pressedPlayer = null;
            _pressedHud = null;
        }
    }

    internal static void CancelInput()
    {
        ShortcutInput.Cancel();
        ForsakenPowerRadial.Close();
        _pressedPlayer = null;
        _pressedHud = null;
    }

    internal static bool TrySelectPower(Player player, string power)
    {
        if (!CanUseSelection(player, Hud.instance) ||
            ObjectDB.instance?.GetStatusEffect(power.GetStableHashCode()) == null ||
            !BossStonePerPlayerRuntime.GetUnlockedGuardianPowerNames(player).Contains(power))
        {
            return false;
        }
        if (!string.Equals(player.GetGuardianPowerName(), power, StringComparison.OrdinalIgnoreCase))
        {
            player.SetGuardianPower(power);
            player.Message(MessageHud.MessageType.TopLeft,
                BossRulesLocalization.Text(BossRulesLocalization.MessageForsakenPowerSelectedKey)
                    .Replace("{name}", GetDisplayName(power)));
        }
        return true;
    }

    private static void ShowNoUnlockedPowers(Player player) => player.Message(MessageHud.MessageType.TopLeft,
        BossRulesLocalization.Text(BossRulesLocalization.MessageForsakenPowerNoneKey));

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

    private static bool CanUseSelection(Player? player, Hud? hud)
    {
        if (!BossRulesConfig.IsRemoteForsakenPowerSelectionEnabled() ||
            player == null ||
            player != Player.m_localPlayer || !Application.isFocused ||
            hud == null || !hud.isActiveAndEnabled || ObjectDB.instance == null)
        {
            return false;
        }

        if (player.IsDead() || player.InCutscene() || player.IsTeleporting() || player.IsSleeping())
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
            (Hud.InRadial() && !ForsakenPowerRadial.Owns(hud.m_radialMenu)) ||
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
        int objectDbInstanceId = ObjectDB.instance != null ? ObjectDB.instance.GetInstanceID() : 0;
        if (_unlockedGuardianPowersDirty ||
            _cachedUnlockedGuardianPowersPlayerInstanceId != playerInstanceId ||
            _cachedUnlockedGuardianPowersObjectDbInstanceId != objectDbInstanceId)
        {
            CachedUnlockedGuardianPowers.Clear();
            CachedUnlockedGuardianPowers.AddRange(BuildUnlockedGuardianPowers(player));
            _cachedUnlockedGuardianPowersPlayerInstanceId = playerInstanceId;
            _cachedUnlockedGuardianPowersObjectDbInstanceId = objectDbInstanceId;
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

    internal static string GetDisplayName(string guardianPowerName)
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
        bool radialOpen = ForsakenPowerRadial.IsOpen;
        string label = BossRulesLocalization.Text(radialOpen
            ? BossRulesLocalization.MessageForsakenPowerControlsKey
            : BossRulesLocalization.MessageForsakenPowerOpenKey);
        if (_cachedHintText.Length == 0 ||
            !_cachedHintShortcut.Equals(shortcut) ||
            _cachedHintRadialOpen != radialOpen ||
            !string.Equals(_cachedHintLabel, label, StringComparison.Ordinal))
        {
            _cachedHintShortcut = shortcut;
            _cachedHintRadialOpen = radialOpen;
            _cachedHintLabel = label;
            string keyLabel = $"[{GetShortcutLabel(shortcut)}]";
            _cachedHintText = radialOpen ? label.Replace("{key}", keyLabel) : $"{keyLabel} {label}";
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

}

// The shortcut toggles the menu; it never selects a power on release. A mouse
// click must begin after opening, so the opening press cannot also confirm.
internal sealed class ForsakenPowerShortcutInput
{
    internal enum Action { None, Open, Commit, Cancel }
    private enum Phase { Idle, Open, WaitForRelease }
    private Phase _phase;
    private bool _confirmPressed;
    internal bool IsIdle => _phase == Phase.Idle;
    internal bool IsOpen => _phase == Phase.Open;

    internal Action Step(bool allowed, bool down, bool held, bool confirmDown, bool confirmUp)
    {
        if (_phase == Phase.WaitForRelease)
        {
            if (!held) _phase = Phase.Idle;
            return Action.None;
        }
        if (!allowed)
        {
            bool active = _phase != Phase.Idle;
            _phase = held ? Phase.WaitForRelease : Phase.Idle;
            _confirmPressed = false;
            return active ? Action.Cancel : Action.None;
        }
        if (_phase == Phase.Idle)
        {
            if (down)
            {
                _phase = Phase.Open;
                _confirmPressed = false;
                return Action.Open;
            }
            return Action.None;
        }
        if (down)
        {
            Cancel();
            return Action.Cancel;
        }
        if (confirmDown) _confirmPressed = true;
        // Native mouse radial interaction also confirms on mouse-up. Keeping the
        // menu open through mouse-down prevents that click becoming a held attack.
        if (confirmUp && _confirmPressed)
        {
            _phase = held ? Phase.WaitForRelease : Phase.Idle;
            _confirmPressed = false;
            return Action.Commit;
        }
        return Action.None;
    }

    internal void Cancel() { _phase = Phase.WaitForRelease; _confirmPressed = false; }
    internal void Reset() { _phase = Phase.Idle; _confirmPressed = false; }
}
