using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Valheim.UI;

internal static class Program
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int Checks, Opens, Closes;
    private static bool Animate, Instant;
    private static IRadialConfig? OpenedConfig;
    private static RadialBase? ClosedRadial;
    private static Type? ColdOpenType;
    private static int ColdOpens;
    private static bool ColdDataWasNull;
    private static readonly Dictionary<object, string> DisplayedText = new();
    private static readonly Dictionary<object, bool> ActiveObjects = new();
    private static Localization? FixtureLocalization;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ObjectOffsetLookup();
    private static readonly ObjectOffsetLookup UnusedObjectOffset = () => 0;
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mono_add_internal_call(string name, IntPtr method);

    private static bool SkipNative() => false;
    // Original Player static initialization needs Unity animation hashing. The
    // fixture has no native engine; IDs are unused in these UI contract probes.
    private static bool AnimationHash(string __0, ref int __result)
    {
        __result = __0.GetStableHashCode();
        return false;
    }
    private static bool RecordAnimate(bool __0) { Animate = __0; return false; }
    private static bool RecordOpen(IRadialConfig __0) { Opens++; OpenedConfig = __0; return false; }
    private static bool RecordClose(RadialBase __instance, bool __0)
    {
        Closes++; ClosedRadial = __instance; Instant = __0; return false;
    }
    private static bool HoverTrue(ref bool __result) { __result = true; return false; }
    private static bool NativeFalse(ref bool __result) { __result = false; return false; }
    private static bool FixtureAlive(UnityEngine.Object __0, ref bool __result)
    {
        __result = !ReferenceEquals(__0, null);
        return false;
    }
    private static bool CreateFixture(Type __0, ref ScriptableObject __result)
    {
        __result = (ScriptableObject)FormatterServices.GetUninitializedObject(__0);
        return false;
    }
    private static bool ColdOpen()
    {
        ColdOpens++;
        ColdDataWasNull = RadialData.SO == null;
        // Stop at the real activation API boundary. There is no Unity scene in
        // this runner; do not simulate Awake or claim to render the menu.
        ColdOpenType!.GetField("_session", Static)!.SetValue(null, null);
        return false;
    }
    private static bool RecordText(object __instance, string __0)
    {
        DisplayedText[__instance] = __0;
        return false;
    }
    private static bool UseFixtureLocalization(ref Localization __result) { __result = FixtureLocalization!; return false; }
    private static bool RecordActive(object __instance, bool __0) { ActiveObjects[__instance] = __0; return false; }
    private static bool NoProfilerMarker(ref IntPtr __result) { __result = IntPtr.Zero; return false; }
    private static bool FixtureDataPath(ref string __result) { __result = "unused-fixture-path"; return false; }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Checks++;
    }
    private static int Main(string[] args)
    {
        string[] dirs = { Path.GetDirectoryName(Path.GetFullPath(args[0]))!, args[1], args[2] };
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string? path = dirs.Select(d => Path.Combine(d, new AssemblyName(e.Name).Name + ".dll")).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        try { Run(args); return 0; }
        catch (Exception ex) { System.Console.Error.WriteLine(ex); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string[] args)
    {
        Assembly plugin = Assembly.LoadFrom(args[0]);
        ToggleChecks(plugin);
        NativeContractChecks(plugin);
        ColdOpenAndDescriptionChecks(plugin);
        DescriptionPositionChecks(plugin);
        System.Console.WriteLine($"PASS: {Checks} merged-plugin input and original-game Mono contract checks. Native UI bodies substituted; no Unity scene/input/device execution.");
    }

    private static void ToggleChecks(Assembly plugin)
    {
        Type type = plugin.GetType("BossRules.ForsakenPowerShortcutInput", true)!;
        object state = Activator.CreateInstance(type, true)!;
        MethodInfo step = type.GetMethod("Step", Instance)!;
        void Reset() => type.GetMethod("Reset", Instance)!.Invoke(state, null);
        void Cancel() => type.GetMethod("Cancel", Instance)!.Invoke(state, null);
        void Frame(string expected, bool down = false, bool held = false, bool clickDown = false, bool clickUp = false, bool allowed = true)
        {
            string action = step.Invoke(state, new object[] { allowed, down, held, clickDown, clickUp })!.ToString()!;
            Check(action == expected, $"input: {expected}, got {action}");
        }

        Frame("Open", down: true, held: true); // immediate; no hold threshold
        Frame("None", held: true);
        Frame("None"); // release shortcut: stay open
        Frame("None");
        Check((bool)type.GetProperty("IsOpen", Instance)!.GetValue(state)!, "menu remains open after shortcut release");
        Frame("None", clickDown: true);
        Frame("Commit", clickUp: true);
        Frame("None", clickUp: true); // no duplicate selection

        Reset();
        Frame("Open", down: true); // a very short shortcut press still opens
        Frame("Commit", clickDown: true, clickUp: true); // short complete click after opening
        Reset();
        Frame("Open", down: true, held: true, clickDown: true, clickUp: true);
        Frame("None", clickUp: true); // opening frame's click is never carried forward
        Frame("None", clickDown: true);
        Frame("Commit", clickUp: true);
        Reset();
        Frame("Open", down: true, held: true);
        Frame("None", clickUp: true); // mouse held before open: ignore release
        Frame("None");
        Frame("Cancel", down: true, held: true); // shortcut toggles closed
        Frame("None", held: true); // no repeated open while held
        Frame("None");
        Frame("Open", down: true, held: true);
        Frame("None", clickDown: true, held: true);
        Frame("Commit", clickUp: true, held: true); // selection can precede shortcut release
        Frame("None", held: true);
        Frame("None");

        // UI/focus, rebind, feature off, player/HUD identity and explicit cancel
        // feed the eligibility gate. Cancellation always wins over confirmation.
        foreach (bool held in new[] { false, true })
        {
            Reset();
            Frame("Open", down: true, held: true);
            Frame("None", clickDown: true, held: held);
            Frame("Cancel", clickUp: true, held: held, allowed: false);
            Frame("None", held: held); // clearing blocker does not reopen
            Frame("None");
            Frame("Open", down: true, held: true);
        }
        Reset();
        Frame("Open", down: true);
        Frame("None", clickDown: true);
        Frame("Cancel", down: true, held: true, clickUp: true); // toggle beats selection
        Frame("None");
        Reset();
        Frame("None", down: true, held: true, allowed: false);
        Frame("None", held: true);
        Frame("None");
        Frame("None", held: true); // no opening without fresh qualified shortcut down
        Reset();
        Frame("Open", down: true, held: true);
        Cancel(); // failed open, no powers, component disable or focus notification
        Frame("None", held: true);
        Frame("None");
        Frame("Open", down: true, held: true);
        Reset();
        Cancel(); // no in-flight press
        Frame("None", held: true);
        Frame("None");
        Frame("Open", down: true, held: true);
    }

    private static void NativeContractChecks(Assembly plugin)
    {
        Harmony harness = new("bossrules.verification.forsaken-selection");
        MethodInfo close = typeof(RadialBase).GetMethod("Close", Instance, null, new[] { typeof(bool) }, null)!;
        FieldInfo highlighter = typeof(RadialBase).GetField("m_highlighter", Instance)!;
        Check(close.IsPrivate && highlighter.IsPrivate, "original Close/highlighter are private");
        MethodInfo tooltip = typeof(StatusEffect).GetMethod("GetTooltipString", Instance)!;
        Check(tooltip.IsPublic && tooltip.IsVirtual, "original status effect tooltip is a public virtual API");
        try
        {
            Type animator = Assembly.Load("UnityEngine.AnimationModule").GetType("UnityEngine.Animator", true)!;
            harness.Patch(animator.GetMethod("StringToHash", Static),
                prefix: new HarmonyMethod(typeof(Program), nameof(AnimationHash)));
            // Suppress native UI bodies only. Dispatch and access go through the
            // production merged DLL and original unmodified game metadata in Mono.
            harness.Patch(close, prefix: new HarmonyMethod(typeof(Program), nameof(RecordClose)));
            harness.Patch(typeof(RadialBase).GetProperty("ShouldAnimateIn")!.SetMethod,
                prefix: new HarmonyMethod(typeof(Program), nameof(RecordAnimate)));
            harness.Patch(typeof(RadialBase).GetMethod("Open"),
                prefix: new HarmonyMethod(typeof(Program), nameof(RecordOpen)));
            harness.Patch(typeof(RadialBase).GetProperty("UseHoverSelect")!.GetMethod,
                prefix: new HarmonyMethod(typeof(Program), nameof(HoverTrue)));
            harness.Patch(typeof(KeyHints).GetMethod("UpdateHints", Instance),
                prefix: new HarmonyMethod(typeof(Program), nameof(SkipNative)));
            Type radialType = plugin.GetType("BossRules.ForsakenPowerRadial", true)!;
            RuntimeHelpers.RunClassConstructor(radialType.TypeHandle);
            foreach (string name in new[] { "ForsakenPowerRadialHoverPatch", "ForsakenPowerRadialHintsPatch" })
                Check(harness.CreateClassProcessor(plugin.GetType("BossRules." + name, true)!).Patch().Count == 1,
                    "bind production " + name);
            RadialBase radial = (RadialBase)FormatterServices.GetUninitializedObject(typeof(RadialBase));
            var closeDelegate = (Action<RadialBase, bool>)radialType.GetField("CloseImmediately", Static)!.GetValue(null)!;
            closeDelegate(radial, true);
            Check(Closes == 1 && Instant && ReferenceEquals(ClosedRadial, radial), "cached private Close delegate dispatch");
            var getHighlighter = (AccessTools.FieldRef<RadialBase, RectTransform>)radialType.GetField("Highlighter", Static)!.GetValue(null)!;
            RectTransform marker = (RectTransform)FormatterServices.GetUninitializedObject(typeof(RectTransform));
            highlighter.SetValue(radial, marker);
            Check(ReferenceEquals(getHighlighter(radial), marker), "cached private highlighter field access");

            Type rootType = plugin.GetType("BossRules.ForsakenPowerRadialRoot", true)!;
            Check(typeof(OpenRadialConfig).IsAssignableFrom(rootType), "native root branch type contract");
            object root = FormatterServices.GetUninitializedObject(rootType);
            Config content = new();
            rootType.GetField("Content", Instance)!.SetValue(root, content);
            ((IRadialConfig)root).InitRadialConfig(radial);
            Check(Animate && Opens == 1 && ReferenceEquals(OpenedConfig, content), "explicit interface routes to content once");
            Check(((IRadialConfig)root).LocalizedName == "fixture", "root label delegation");
            Check(radial.UseHoverSelect, "unowned menu retains native hover result");
            typeof(KeyHints).GetMethod("UpdateHints", Instance)!.Invoke(
                FormatterServices.GetUninitializedObject(typeof(KeyHints)), null);
            Check(!(bool)radialType.GetProperty("IsOpen", Static)!.GetValue(null)!, "hint patch leaves no session outside owned menu");

            // Restoration must recover our overrides without erasing another mod's intervening override.
            Func<bool> original = () => true;
            Func<bool> otherMod = () => false;
            Func<Vector2> originalMouse = () => new Vector2(1, 0);
            Func<Vector2> ownMouse = () => Vector2.zero;
            radial.GetConfirm = radial.GetBack = original;
            radial.GetMouseDirection = originalMouse;
            Type controls = radialType.GetNestedType("Controls", BindingFlags.NonPublic)!;
            object snapshot = Activator.CreateInstance(controls, Instance, null, new object[] { radial }, null)!;
            radial.GetConfirm = (Func<bool>)radialType.GetField("NoInput", Static)!.GetValue(null)!;
            radial.GetBack = otherMod;
            radial.GetMouseDirection = ownMouse;
            controls.GetMethod("Restore", Instance)!.Invoke(snapshot, new object[] { radial, ownMouse });
            Check(radial.GetConfirm == original && radial.GetMouseDirection == originalMouse, "restore owned input delegates");
            Check(radial.GetBack == otherMod, "preserve intervening other-mod delegate");
        }
        finally { harness.UnpatchSelf(); }
    }

    private static void ColdOpenAndDescriptionChecks(Assembly plugin)
    {
        // UnityEngine.Object's static initializer queries an engine memory offset.
        // This fixture never dereferences native objects and substitutes liveness
        // below; supply the unused offset only in this isolated Mono process.
        mono_add_internal_call("UnityEngine.Object::GetOffsetOfInstanceIDInCPlusPlusObject",
            Marshal.GetFunctionPointerForDelegate(UnusedObjectOffset));
        Harmony harness = new("bossrules.verification.radial-cold-open");
        Type radialType = plugin.GetType("BossRules.ForsakenPowerRadial", true)!;
        RadialDataSO previousData = RadialData.SO;
        object previousSession = radialType.GetField("_session", Static)!.GetValue(null)!;
        try
        {
            Type animator = Assembly.Load("UnityEngine.AnimationModule").GetType("UnityEngine.Animator", true)!;
            harness.Patch(animator.GetMethod("StringToHash", Static),
                prefix: new HarmonyMethod(typeof(Program), nameof(AnimationHash)));
            harness.Patch(typeof(UnityEngine.Object).GetMethod("IsNativeObjectAlive", Static),
                prefix: new HarmonyMethod(typeof(Program), nameof(FixtureAlive)));
            harness.Patch(typeof(RadialBase).GetProperty("Active")!.GetMethod,
                prefix: new HarmonyMethod(typeof(Program), nameof(NativeFalse)));
            harness.Patch(typeof(Player).GetMethod("InPlaceMode", Instance),
                prefix: new HarmonyMethod(typeof(Program), nameof(NativeFalse)));
            harness.Patch(typeof(ScriptableObject).GetMethod("CreateInstance", new[] { typeof(Type) }),
                prefix: new HarmonyMethod(typeof(Program), nameof(CreateFixture)));
            harness.Patch(typeof(RadialBase).GetMethod("Open"),
                prefix: new HarmonyMethod(typeof(Program), nameof(ColdOpen)));
            ColdOpenType = radialType;
            RadialData.SO = null!;
            Hud hud = (Hud)FormatterServices.GetUninitializedObject(typeof(Hud));
            hud.m_radialMenu = (RadialBase)FormatterServices.GetUninitializedObject(typeof(RadialBase));
            Player player = (Player)FormatterServices.GetUninitializedObject(typeof(Player));
            radialType.GetMethod("TryOpen", Static)!.Invoke(null, new object[] { hud, player, new[] { "GP_Eikthyr" } });
            Check(ColdOpens == 1 && ColdDataWasNull, "cold data reaches native Open without G/T warm-up");

            // Exercise effects-only presentation. Text setters/layout/activation
            // are recorded or skipped; prefab construction is inspected separately.
            Type panelType = radialType.GetNestedType("DescriptionPanel", BindingFlags.NonPublic)!;
            Type textType = Assembly.Load("Unity.TextMeshPro").GetType("TMPro.TMP_Text", true)!;
            Type uiTextType = textType.Assembly.GetType("TMPro.TextMeshProUGUI", true)!;
            Type profiler = typeof(UnityEngine.Object).Assembly.GetType("Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility", true)!;
            harness.Patch(profiler.GetMethods(Static).Single(m => m.Name == "CreateMarker" && m.GetParameters()[0].ParameterType == typeof(string)),
                prefix: new HarmonyMethod(typeof(Program), nameof(NoProfilerMarker)));
            harness.Patch(textType.GetProperty("text")!.SetMethod,
                prefix: new HarmonyMethod(typeof(Program), nameof(RecordText)));
            harness.Patch(typeof(Localization).GetProperty("instance", Static)!.GetMethod,
                prefix: new HarmonyMethod(typeof(Program), nameof(UseFixtureLocalization)));
            harness.Patch(typeof(Application).GetProperty("persistentDataPath", Static)!.GetMethod,
                prefix: new HarmonyMethod(typeof(Program), nameof(FixtureDataPath)));
            harness.Patch(typeof(GameObject).GetMethod("SetActive"),
                prefix: new HarmonyMethod(typeof(Program), nameof(RecordActive)));
            Type layout = Assembly.Load("UnityEngine.UI").GetType("UnityEngine.UI.LayoutRebuilder", true)!;
            harness.Patch(layout.GetMethod("ForceRebuildLayoutImmediate", Static),
                prefix: new HarmonyMethod(typeof(Program), nameof(SkipNative)));
            object panel = FormatterServices.GetUninitializedObject(panelType);
            object body = FormatterServices.GetUninitializedObject(uiTextType);
            object root = FormatterServices.GetUninitializedObject(typeof(GameObject));
            panelType.GetField("_root", Instance)!.SetValue(panel, root);
            panelType.GetField("_text", Instance)!.SetValue(panel, body);
            MethodInfo show = panelType.GetMethod("Show", Instance)!;
            const string effect = "<color=orange>+60%</color> Eitr regeneration\nMining +100%";
            show.Invoke(panel, new object[] { effect });
            Check(DisplayedText[body] == effect && ActiveObjects[root], "effects-only panel retains multiline rich text without adding a name");
            show.Invoke(panel, new object[] { "  \n " });
            Check(!ActiveObjects[root], "empty effects hide panel");
            show.Invoke(panel, new object[] { effect });
            Check(ActiveObjects[root] && DisplayedText[body] == effect, "effects panel reopens after an empty effect");
            EffectDescriptionChecks(plugin);
        }
        finally
        {
            RadialData.SO = previousData;
            radialType.GetField("_session", Static)!.SetValue(null, previousSession);
            ColdOpenType = null;
            FixtureLocalization = null;
            DisplayedText.Clear();
            ActiveObjects.Clear();
            harness.UnpatchSelf();
        }
    }

    private static void EffectDescriptionChecks(Assembly plugin)
    {
        MethodInfo describe = plugin.GetType("BossRules.ForsakenPowerRadial", true)!.GetMethod("GetEffectDescription", Static)!;
        string Describe(StatusEffect effect) => (string)describe.Invoke(null, new object[] { effect })!;
        MethodInfo translations = plugin.GetType("BossRules.Localizer", true)!.GetMethod("ReadEmbeddedTranslations", Static)!;
        foreach (string language in new[] { "English", "Korean" })
        {
            // Use the original localization/token parser with small fixture game
            // dictionaries and the plugin's actual embedded attribute translations.
            var words = new Dictionary<string, string>((Dictionary<string, string>)translations.Invoke(null, new object[] { language })!);
            string lore = language == "English" ? "The Emerald Flame never relents.\nLong introductory lore." : "버프의 배경 이야기입니다.";
            words["fixture_lore"] = lore;
            words["se_eitrregen"] = language == "English" ? "Eitr regeneration" : "에이트르 회복";
            words["se_ttl"] = language == "English" ? "Effect duration" : "효과 지속 시간";
            FixtureLocalization = (Localization)FormatterServices.GetUninitializedObject(typeof(Localization));
            typeof(Localization).GetField("m_translations", Instance)!.SetValue(FixtureLocalization, words);
            typeof(Localization).GetField("m_stringBuilder", Instance)!.SetValue(FixtureLocalization, new StringBuilder());
            typeof(Localization).GetField("m_endChars", Instance)!.SetValue(FixtureLocalization, " (){}[]+-!?/\\&%,.:-=<>\n".ToCharArray());
            typeof(Localization).GetField("m_cache", Instance)!.SetValue(FixtureLocalization, new LRUCache<string>(100));

            SE_Stats stats = (SE_Stats)FormatterServices.GetUninitializedObject(typeof(SE_Stats));
            stats.m_name = "Duplicate power name";
            stats.m_tooltip = "$fixture_lore";
            stats.m_mods = new List<HitData.DamageModPair>();
            stats.m_healthRegenMultiplier = stats.m_staminaRegenMultiplier = 1;
            stats.m_eitrRegenMultiplier = 2;
            stats.m_ttl = 31;
            string before = stats.GetTooltipString();
            string expected = words["se_eitrregen"] + ": <color=orange>+100%</color>\n" +
                              words["se_ttl"] + ": <color=orange>31</color>";
            Check(Describe(stats) == expected, language + ": remove lore/name, retain original localized stats and duration");
            Check(stats.m_tooltip == "$fixture_lore" && stats.GetTooltipString() == before, language + ": shared effect and ordinary tooltip remain unchanged");
            stats.m_attributes = StatusEffect.StatusAttribute.SailingPower;
            Check(Describe(stats) == expected + "\n" + words["bossrules_effect_sailing"], language + ": preserve sailing effect from attribute");
            stats.m_attributes = StatusEffect.StatusAttribute.TamingBoost | StatusEffect.StatusAttribute.DoubleImpactDamage;
            Check(Describe(stats) == expected + "\n" + words["bossrules_effect_taming"] + "\n" + words["bossrules_effect_collision"], language + ": preserve combined taming/incoming collision effects");
            stats.m_attributes = 0;
            Check(Describe(stats) == expected, language + ": live attribute removal removes its description");

            CustomTooltip custom = (CustomTooltip)FormatterServices.GetUninitializedObject(typeof(CustomTooltip));
            custom.m_tooltip = "$fixture_lore";
            custom.Text = lore + "\r\n\r\nAlready localized effect without a number";
            Check(Describe(custom) == "Already localized effect without a number", language + ": already-localized override loses only exact leading lore");
            custom.Text = "Custom effect header\n$fixture_lore\n<color=orange>+15%</color>";
            Check(Describe(custom) == FixtureLocalization.Localize(custom.Text), language + ": do not delete unmatched/interior custom text");
            custom.Text = lore + " extended custom effect";
            Check(Describe(custom) == custom.Text, language + ": require a complete introductory boundary");
            custom.Text = "$fixture_lore";
            Check(Describe(custom) == "", language + ": intro-only tooltip yields no fabricated effects");
            custom.m_tooltip = "";
            custom.Text = "An actual effect without a numeric value";
            Check(Describe(custom) == custom.Text, language + ": empty intro preserves nonnumeric effects");
        }
        FixtureLocalization = null;
    }

    private static void DescriptionPositionChecks(Assembly plugin)
    {
        MethodInfo position = plugin.GetType("BossRules.ForsakenPowerRadial", true)!.GetMethod("DescriptionPosition", Static)!;
        Vector2 size = new(300, 180);
        Vector2 screen = new(1920, 1080);
        Vector2 center = screen * .5f;
        Vector2 Place(Rect icon) => (Vector2)position.Invoke(null, new object[] { icon, size, center, screen })!;
        Rect right = new(1060, 500, 64, 64);
        Rect left = new(800, 500, 64, 64);
        Check(Place(right).x > right.xMax, "right power: description outside icon");
        Check(Place(left).x + size.x < left.xMin, "left power: description outside icon");
        Rect rightEdge = new(1850, 500, 64, 64);
        Rect leftEdge = new(6, 500, 64, 64);
        Check(Place(rightEdge).x + size.x < rightEdge.xMin, "right edge: flip left");
        Check(Place(leftEdge).x > leftEdge.xMax, "left edge: flip right");
        Check(Place(new Rect(1060, 0, 64, 64)).y == 8, "bottom edge padding");
        Check(Place(new Rect(1060, 1050, 64, 64)).y + size.y == screen.y - 8, "top edge padding");
        screen = new Vector2(640, 480);
        center = screen * .5f;
        size = new Vector2(624, 464); // oversized descriptions after screen-fit scaling
        Check(Place(new Rect(400, 380, 64, 64)) == new Vector2(8, 8), "fitted long tooltip stays within small display");
    }

    private sealed class Config : IRadialConfig
    {
        public string LocalizedName => "fixture";
        public Sprite Sprite => null!;
        public void InitRadialConfig(RadialBase radial) => throw new Exception("Native Open body must be suppressed in this fixture.");
    }

    private sealed class CustomTooltip : StatusEffect
    {
        internal string Text = "";
        public override string GetTooltipString() => Text;
    }
}
