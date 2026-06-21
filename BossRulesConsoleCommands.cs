using System.Collections.Generic;

namespace BossRules;

internal static class BossRulesConsoleCommands
{
    private const string InspectCommandName = "bossrules:inspect";
    private const string BossStoneCommandName = "bossrules:bossstone";

    private static readonly List<string> InspectTabOptions = new()
    {
        "bossstone"
    };

    private static readonly List<string> BossStoneTabOptions = new()
    {
        "reset"
    };

    private static bool _registered;

    internal static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        new Terminal.ConsoleCommand(
            InspectCommandName,
            "Inspect the current hovered/aimed BossRules runtime target. Currently supports: bossstone.",
            InspectRuntimeTarget,
            optionsFetcher: GetInspectTabOptions);
        new Terminal.ConsoleCommand(
            BossStoneCommandName,
            "Reset per-player boss stone state. Syntax: bossrules:bossstone reset <exactPlayerName>",
            HandleBossStoneCommand,
            isCheat: true,
            optionsFetcher: GetBossStoneTabOptions,
            onlyAdmin: true);
    }

    private static List<string> GetInspectTabOptions()
    {
        return InspectTabOptions;
    }

    private static List<string> GetBossStoneTabOptions()
    {
        return BossStoneTabOptions;
    }

    private static void InspectRuntimeTarget(Terminal.ConsoleEventArgs args)
    {
        string scope = args.Length >= 2 ? (args[1] ?? "").Trim().ToLowerInvariant() : "";
        if (scope != "bossstone")
        {
            args.Context?.AddString($"Syntax: {InspectCommandName} bossstone");
            return;
        }

        if (BossStonePerPlayerRuntime.TryInspectCurrentTarget(out string[] bossStoneLines, out string bossStoneError))
        {
            foreach (string line in bossStoneLines)
            {
                args.Context?.AddString(line);
            }
        }
        else
        {
            args.Context?.AddString(bossStoneError);
        }
    }

    private static void HandleBossStoneCommand(Terminal.ConsoleEventArgs args)
    {
        string action = args.Length >= 2 ? (args[1] ?? "").Trim().ToLowerInvariant() : "";
        if (action != "reset")
        {
            args.Context?.AddString($"Syntax: {BossStoneCommandName} reset <exactPlayerName>");
            return;
        }

        const string resetPrefix = BossStoneCommandName + " reset";
        string targetPlayerName = args.FullLine.Length > resetPrefix.Length
            ? args.FullLine.Substring(resetPrefix.Length).Trim()
            : "";
        BossStonePerPlayerRuntime.TryRequestReset(targetPlayerName, out string resetMessage);
        args.Context?.AddString(resetMessage);
    }
}
