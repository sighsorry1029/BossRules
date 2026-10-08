using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valheim.UI;

namespace BossRules;

// Borrow the HUD's renderer for one menu. Keep its LastUsed and global radial
// settings intact; only this session's controls, elements and materials are owned.
internal static class ForsakenPowerRadial
{
    private static readonly Action<RadialBase, bool> CloseImmediately =
        AccessTools.MethodDelegate<Action<RadialBase, bool>>(
            AccessTools.Method(typeof(RadialBase), "Close", new[] { typeof(bool) }));
    private static readonly AccessTools.FieldRef<RadialBase, RectTransform> Highlighter =
        AccessTools.FieldRefAccess<RadialBase, RectTransform>("m_highlighter");
    private static readonly Func<bool> NoInput = () => false;
    private static readonly Func<Vector2> NoDirection = () => Vector2.zero;
    private static Session? _session;

    internal static bool IsOpen => _session != null && _session.Radial != null &&
        _session.Radial.Active && Owns(_session.Radial);

    internal static bool Owns(RadialBase? radial) => _session != null && radial != null &&
        radial == _session.Radial && ReferenceEquals(radial.CurrentConfig, _session);

    internal static bool TryOpen(Hud hud, Player player, IReadOnlyList<string> powers)
    {
        RadialBase radial = hud.m_radialMenu;
        if (_session != null || radial == null || radial.Active || powers.Count == 0 ||
            player.InPlaceMode())
        {
            return false;
        }
        Session session = new(hud, player, radial, powers);
        _session = session;
        try
        {
            // OpenRadialConfig's native root branch resets state, loads controls and
            // subscribes to death. A plain IRadialConfig Open would bypass that branch.
            // Open also activates the initially inactive HUD radial: its initializer
            // supplies RadialData.SO in Awake, so do not require that data beforehand.
            session.Root = ScriptableObject.CreateInstance<ForsakenPowerRadialRoot>();
            session.Root.Content = session;
            radial.CanOpen = true;
            radial.Open(session.Root);
            if (!Owns(radial) || !radial.Active)
            {
                Close();
                return false;
            }
            // Native construction moves the pointer onto the first entry. Start at
            // the neutral center so opening the menu doesn't preselect a power.
            ZInput.SetMousePosition(radial.InfoPosition);
            session.Opening = false;
            return true;
        }
        catch (Exception ex)
        {
            BossRulesPlugin.BossRulesLogger.LogWarning($"Could not open Forsaken Power radial: {ex.Message}");
            Close();
            return false;
        }
    }

    internal static void PollLifetime()
    {
        if (_session != null && (!IsOpen || _session.Hud == null || Hud.instance != _session.Hud ||
            Player.m_localPlayer != _session.Player || _session.Hud.m_radialMenu != _session.Radial))
        {
            Close();
        }
    }

    internal static void UpdateDescription()
    {
        if (IsOpen) _session!.UpdateDescription();
    }

    internal static void SelectAndClose(Player player)
    {
        Session? session = _session;
        try
        {
            if (session == null || !IsOpen || player != session.Player || !session.HasPointerSelection()) return;
            RadialMenuElement selected = session.Radial.Selected;
            foreach (Choice choice in session.Choices)
            {
                if (choice.Element != null && choice.Element == selected)
                {
                    ForsakenPowerSelectionRuntime.TrySelectPower(player, choice.Power);
                    break;
                }
            }
        }
        finally { Close(); }
    }

    internal static void Close()
    {
        Session? session = _session;
        if (session == null) return;
        bool closingOwnedMenu = false;
        try
        {
            // Do not close a menu another mod opened on this shared renderer.
            if (session.Radial != null && session.Radial.Active &&
                (Owns(session.Radial) || ReferenceEquals(session.Radial.CurrentConfig, session.Root) ||
                 (session.Opening && session.Radial.CurrentConfig == null)))
            {
                closingOwnedMenu = true;
                CloseImmediately(session.Radial, true);
            }
        }
        catch (Exception ex)
        {
            BossRulesPlugin.BossRulesLogger.LogWarning($"Could not close Forsaken Power radial normally: {ex.Message}");
            if (closingOwnedMenu && session.Radial != null &&
                (Owns(session.Radial) || session.Radial.CurrentConfig == null ||
                 ReferenceEquals(session.Radial.CurrentConfig, session.Root)))
            {
                session.Radial.gameObject.SetActive(false);
                GameCamera.instance?.UpdateMouseCapture();
            }
        }
        finally
        {
            _session = null;
            session.Dispose();
        }
    }

    // Publicly replaceable native input delegates are restored only if they still
    // carry our value. This avoids overwriting another mod's intervening changes.
    private sealed class Controls
    {
        internal readonly Func<bool> Confirm, Release, Back, Close, Throw, OpenThrow, Flick, DoubleTap;
        internal readonly Func<Vector2> Mouse, Controller;
        internal Controls(RadialBase radial)
        {
            Confirm = radial.GetConfirm; Release = radial.GetReleaseToUse;
            Back = radial.GetBack; Close = radial.GetClose; Throw = radial.GetThrow;
            OpenThrow = radial.GetOpenThrowMenu; Flick = radial.GetFlick; DoubleTap = radial.GetDoubleTap;
            Mouse = radial.GetMouseDirection; Controller = radial.GetControllerDirection;
        }
        internal void Restore(RadialBase radial, Func<Vector2> mouse)
        {
            if (radial.GetConfirm == NoInput) radial.GetConfirm = Confirm;
            if (radial.GetReleaseToUse == NoInput) radial.GetReleaseToUse = Release;
            if (radial.GetBack == NoInput) radial.GetBack = Back;
            if (radial.GetClose == NoInput) radial.GetClose = Close;
            if (radial.GetThrow == NoInput) radial.GetThrow = Throw;
            if (radial.GetOpenThrowMenu == NoInput) radial.GetOpenThrowMenu = OpenThrow;
            if (radial.GetFlick == NoInput) radial.GetFlick = Flick;
            if (radial.GetDoubleTap == NoInput) radial.GetDoubleTap = DoubleTap;
            if (radial.GetMouseDirection == mouse) radial.GetMouseDirection = Mouse;
            if (radial.GetControllerDirection == NoDirection) radial.GetControllerDirection = Controller;
        }
    }

    private sealed class Session : IRadialConfig, IDisposable
    {
        internal readonly Hud Hud;
        internal readonly Player Player;
        internal readonly RadialBase Radial;
        internal readonly List<Choice> Choices = new();
        private readonly List<RadialMenuElement> _elements = new();
        private readonly List<Material> _materials = new();
        internal ForsakenPowerRadialRoot? Root;
        internal bool Opening = true;
        private readonly Controls _controls;
        private readonly Func<Vector2> _mouseDirection;
        private readonly IReadOnlyList<string> _powers;
        private DescriptionPanel? _description;
        private bool _descriptionUnavailable;
        private Choice? _describedChoice;
        public string LocalizedName => BossRulesLocalization.Text(BossRulesLocalization.MessageForsakenPowerMenuKey);
        public Sprite Sprite => null!;

        internal Session(Hud hud, Player player, RadialBase radial, IReadOnlyList<string> powers)
        {
            Hud = hud; Player = player; Radial = radial; _powers = powers;
            _controls = new Controls(radial);
            _mouseDirection = MouseDirection;
        }

        public void InitRadialConfig(RadialBase radial)
        {
            // Refreshing a live choice menu must not repeat a pending selection.
            if (_elements.Count != 0)
            {
                radial.QueuedClose();
                return;
            }
            if (RadialData.SO == null || RadialData.SO.GroupElement == null)
                throw new InvalidOperationException("Radial group template is unavailable after initialization.");
            TakeControls();
            foreach (string power in _powers)
            {
                StatusEffect? effect = ObjectDB.instance?.GetStatusEffect(power.GetStableHashCode());
                if (effect == null) continue;
                Choice choice = new(power, effect.m_icon);
                Choices.Add(choice);
                choice.Element = UnityEngine.Object.Instantiate(RadialData.SO.GroupElement);
                _elements.Add(choice.Element);
                _materials.Add(choice.Element.BackgroundMaterial);
                choice.Element.Init(choice, null, radial);
                // All actual selections go through the late click path, after the
                // renderer samples this frame's pointer. No native LastUsed write.
                choice.Element.Interact = NoInput;
                choice.Element.CloseOnInteract = NoInput;
            }
            if (_elements.Count == 0)
            {
                radial.QueuedClose();
                return;
            }
            radial.ConstructRadial(_elements);
            // ConstructRadial may insert native back buttons according to the user's
            // settings. They belong to this opening too, not to the original menu.
            foreach (RadialMenuElement element in _elements)
            {
                Material material = element.BackgroundMaterial;
                if (!_materials.Contains(material)) _materials.Add(material);
            }
        }

        internal void TakeControls()
        {
            Radial.GetConfirm = Radial.GetReleaseToUse = Radial.GetBack = Radial.GetClose = NoInput;
            Radial.GetThrow = Radial.GetOpenThrowMenu = Radial.GetFlick = Radial.GetDoubleTap = NoInput;
            Radial.GetMouseDirection = _mouseDirection;
            Radial.GetControllerDirection = NoDirection;
        }

        internal bool HasPointerSelection() => MouseDirection() != Vector2.zero;

        private Vector2 MouseDirection()
        {
            Vector2 delta = (Vector2)ZInput.pointerPosition - (Vector2)Radial.InfoPosition;
            return delta.sqrMagnitude <= 24f * 24f ? Vector2.zero : delta.normalized;
        }

        internal void UpdateDescription()
        {
            if (_descriptionUnavailable) return;
            Choice? selected = null;
            if (HasPointerSelection())
            {
                foreach (Choice choice in Choices)
                    if (choice.Element != null && choice.Element == Radial.Selected) { selected = choice; break; }
            }
            if (selected == null)
            {
                _describedChoice = null;
                _description?.Hide();
                return;
            }
            try
            {
                if (selected != _describedChoice)
                {
                    _describedChoice = selected;
                    StatusEffect? effect = ObjectDB.instance?.GetStatusEffect(selected.Power.GetStableHashCode());
                    if (effect == null) { _description?.Hide(); return; }
                    _description ??= new DescriptionPanel(Hud, Radial);
                    _description.Show(GetEffectDescription(effect));
                }
                GroupElement element = selected.Element!;
                RectTransform icon = element.Icon != null ? element.Icon.rectTransform : element.ElementTransform;
                _description?.Position(icon, Radial.InfoPosition);
            }
            catch (Exception ex)
            {
                // A missing/custom UI template or third-party tooltip must not
                // prevent click-to-select or trap the radial's input overrides.
                _descriptionUnavailable = true;
                _description?.Hide();
                BossRulesPlugin.BossRulesLogger.LogWarning($"Could not display Forsaken Power description: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _description?.Dispose();
            if (Radial != null) _controls.Restore(Radial, _mouseDirection);
            // Native selection reparents this shared ornament under the selected
            // element. Close only hides it; rescue it before destroying our objects.
            RectTransform? highlighter = Radial != null ? Highlighter(Radial) : null;
            if (highlighter != null && Radial != null)
            {
                foreach (RadialMenuElement element in _elements)
                {
                    if (element != null && highlighter.IsChildOf(element.transform))
                    {
                        highlighter.SetParent(Radial.transform);
                        break;
                    }
                }
            }
            foreach (RadialMenuElement element in _elements)
            {
                if (element != null) UnityEngine.Object.Destroy(element.gameObject);
            }
            foreach (Material material in _materials)
                if (material != null) UnityEngine.Object.Destroy(material);
            _elements.Clear();
            _materials.Clear();
            Choices.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }
    }

    internal static string GetEffectDescription(StatusEffect effect)
    {
        // Preserve native/modded stat formatting without mutating the shared
        // ObjectDB effect. Remove only its exact leading introduction, not arbitrary
        // prose or lines without numbers: those can describe real effects too.
        string text = effect.GetTooltipString() ?? "";
        string introduction = effect.m_tooltip ?? "";
        Localization? localization = Localization.instance;
        if (localization != null)
        {
            text = localization.Localize(text);
            introduction = localization.Localize(introduction);
        }
        if (introduction.Length > 0 && text.StartsWith(introduction, StringComparison.Ordinal) &&
            (text.Length == introduction.Length || text[introduction.Length] == '\n' || text[introduction.Length] == '\r'))
        {
            text = text.Substring(introduction.Length);
        }
        text = text.Trim();

        // SE_Stats does not describe attributes. Use confirmed game consumers,
        // not a prefab name or the lore, which may no longer match live settings.
        AddAttribute(StatusEffect.StatusAttribute.SailingPower, BossRulesLocalization.EffectSailingKey);
        AddAttribute(StatusEffect.StatusAttribute.TamingBoost, BossRulesLocalization.EffectTamingKey);
        AddAttribute(StatusEffect.StatusAttribute.DoubleImpactDamage, BossRulesLocalization.EffectCollisionKey);
        return text;

        void AddAttribute(StatusEffect.StatusAttribute attribute, string key)
        {
            if (!effect.HaveAttribute(attribute)) return;
            string line = BossRulesLocalization.Text(key);
            text = text.Length == 0 ? line : text + "\n" + line;
        }
    }

    // Clone only the vanilla tooltip presentation, not UITooltip's global hover
    // controller. A radial sector remains selected outside the icon's rectangle.
    private sealed class DescriptionPanel : IDisposable
    {
        private readonly GameObject _root;
        private readonly RectTransform _layoutRoot, _panel;
        private readonly TMP_Text _text;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector3 _baseScale;

        internal DescriptionPanel(Hud hud, RadialBase radial)
        {
            UITooltip? template = hud.m_pieceIconPrefab != null ? hud.m_pieceIconPrefab.GetComponent<UITooltip>() : null;
            if (template == null || template.m_tooltipPrefab == null)
                throw new InvalidOperationException("HUD tooltip template is unavailable.");
            _root = UnityEngine.Object.Instantiate(template.m_tooltipPrefab, radial.transform);
            try
            {
                _root.name = "BossRules_ForsakenPowerDescription";
                _root.SetActive(false);
                _root.transform.SetAsLastSibling();
                _baseScale = _root.transform.localScale;
                _layoutRoot = _root.transform as RectTransform ??
                              throw new InvalidOperationException("HUD tooltip layout is unavailable.");
                _panel = _root.transform.GetChild(0) as RectTransform ??
                         throw new InvalidOperationException("HUD tooltip panel is unavailable.");
                // The radial center already names the selected power. Hide a title
                // supplied by custom tooltip templates as well as omitting our own.
                Transform? topic = Utils.FindChild(_root.transform, "Topic");
                if (topic != null) topic.gameObject.SetActive(false);
                _text = Utils.FindChild(_root.transform, "Text")?.GetComponent<TMP_Text>() ??
                        throw new InvalidOperationException("HUD tooltip text is unavailable.");
                if (topic == null)
                {
                    // The compact vanilla tooltip reserves a single line. Size the
                    // clone to all effect lines so placement uses their actual bounds.
                    _panel = _layoutRoot;
                    HorizontalOrVerticalLayoutGroup? layout = _root.GetComponent<HorizontalOrVerticalLayoutGroup>();
                    if (layout != null) layout.childControlHeight = true;
                    ContentSizeFitter? fitter = _root.GetComponent<ContentSizeFitter>();
                    if (fitter != null) fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
                // Descriptions never take focus or intercept the mouse over a power.
                foreach (Graphic graphic in _root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            }
            catch { UnityEngine.Object.Destroy(_root); throw; }
        }

        internal void Show(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { Hide(); return; }
            _text.text = text;
            _root.SetActive(true);
            // Only content changes rebuild layout; tracking the animated icon below
            // reuses transforms/corner storage and does not regenerate strings or UI.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_layoutRoot);
        }

        internal void Position(RectTransform icon, Vector2 center)
        {
            if (!_root.activeSelf) return;
            icon.GetWorldCorners(_corners);
            Rect iconBounds = Rect.MinMaxRect(_corners[0].x, _corners[0].y, _corners[2].x, _corners[2].y);
            _root.transform.localScale = _baseScale;
            _panel.GetWorldCorners(_corners);
            Vector2 size = (Vector2)(_corners[2] - _corners[0]);
            // Keep long modded descriptions on screen too, preserving their text.
            float fit = Mathf.Min(1f, Mathf.Min((Screen.width - 16f) / Mathf.Max(1f, size.x),
                                               (Screen.height - 16f) / Mathf.Max(1f, size.y)));
            _root.transform.localScale = _baseScale * Mathf.Max(0.01f, fit);
            _panel.GetWorldCorners(_corners);
            size = (Vector2)(_corners[2] - _corners[0]);
            Vector2 lowerLeft = DescriptionPosition(iconBounds, size, center, new Vector2(Screen.width, Screen.height));
            _root.transform.position += (Vector3)(lowerLeft - (Vector2)_corners[0]);
        }

        internal void Hide() { if (_root != null) _root.SetActive(false); }
        public void Dispose()
        {
            if (_root == null) return;
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
        }
    }

    // Screen-space bounds match the native HUD radial's InfoPosition convention.
    internal static Vector2 DescriptionPosition(Rect icon, Vector2 size, Vector2 center, Vector2 screen)
    {
        const float margin = 8f;
        bool right = icon.center.x >= center.x;
        float x = right ? icon.xMax + margin : icon.xMin - margin - size.x;
        // Prefer the opposite side if the outward placement cannot fit.
        if (right && x + size.x > screen.x - margin) x = icon.xMin - margin - size.x;
        else if (!right && x < margin) x = icon.xMax + margin;
        return new Vector2(Mathf.Clamp(x, margin, Mathf.Max(margin, screen.x - margin - size.x)),
            Mathf.Clamp(icon.center.y - size.y * 0.5f, margin, Mathf.Max(margin, screen.y - margin - size.y)));
    }

    private sealed class Choice : IRadialConfig
    {
        internal readonly string Power;
        internal GroupElement? Element;
        public string LocalizedName { get; }
        public Sprite Sprite { get; }
        internal Choice(string power, Sprite sprite)
        {
            Power = power;
            LocalizedName = ForsakenPowerSelectionRuntime.GetDisplayName(power);
            Sprite = sprite;
        }
        public void InitRadialConfig(RadialBase radial) { }
    }
}

// Reimplement the interface rather than calling the base's contextual item/emote
// routing. Native Open still recognizes this as a root and performs its full setup.
internal sealed class ForsakenPowerRadialRoot : OpenRadialConfig, IRadialConfig
{
    internal IRadialConfig Content = null!;
    string IRadialConfig.LocalizedName => Content.LocalizedName;
    Sprite IRadialConfig.Sprite => Content.Sprite;
    void IRadialConfig.InitRadialConfig(RadialBase radial)
    {
        radial.ShouldAnimateIn = true;
        radial.Open(Content);
    }
}

[HarmonyPatch(typeof(RadialBase), nameof(RadialBase.UseHoverSelect), MethodType.Getter)]
internal static class ForsakenPowerRadialHoverPatch
{
    private static void Postfix(RadialBase __instance, ref bool __result)
    {
        if (ForsakenPowerRadial.Owns(__instance)) __result = false;
    }
}

[HarmonyPatch(typeof(KeyHints), "UpdateHints")]
internal static class ForsakenPowerRadialHintsPatch
{
    private static void Postfix(KeyHints __instance)
    {
        if (ForsakenPowerRadial.IsOpen && __instance.m_radialHints != null)
            __instance.m_radialHints.SetActive(false);
    }
}
