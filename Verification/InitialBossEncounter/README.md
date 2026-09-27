# Initial boss approach regression checks

Run the mod's normal Debug build first, then from the repository root:

```powershell
dotnet build Verification/InitialBossEncounter/InitialBossEncounter.Tests.csproj -c Debug
& ./Verification/InitialBossEncounter/bin/Debug/net48/InitialBossEncounter.Tests.exe $PWD.Path 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll'
```

The executable compiles the production `InitialBossEncounter.cs` with small game doubles and exercises provenance, first-approach geometry, throttling, owner changes, delayed marker receipt and cleanup. It also reads the unmodified game DLL and final merged mod DLL with Cecil to check required access/signatures and integration points. It does not load or modify the game assembly.

This does not test Unity/Harmony execution, actual ZDO synchronization or world serialization. In game, check Full/Ghost generation for both supported locations, waiting beyond the configured delay before first approach, ground-level players under the Queen dungeon, height boundaries, nearby players outside a wall, remote boss ownership, save/reconnect, explicit YAML rules, and altar resummon refunds. After approach, existing XZ-only despawn behavior must remain unchanged.
