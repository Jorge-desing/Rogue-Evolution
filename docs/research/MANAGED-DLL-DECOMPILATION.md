# Decompiling managed game assemblies

Use this when REA identifies a managed .NET assembly but its configured pseudocode provider is unavailable on this host. The repository helper uses a pinned ILSpyCmd release compatible with the installed .NET 8 runtime and keeps the tool and generated source under ignored `artifacts/`.

Before relying on reflected signatures, metadata tokens, or serialized game data, recheck the game version, `Assembly-CSharp.dll` SHA-256, and MVID together as required by [AGENT-HANDOFF.md](AGENT-HANDOFF.md). Decompilation is static inspection; it does not establish that a plugin or content works in a live run.

## Decompile one type

From the repository root:

```powershell
.\tools\Decompile-ManagedType.ps1 `
  -AssemblyPath 'C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Rogue Command_Data\Managed\Assembly-CSharp.dll' `
  -TypeName 'EntityAttack'
```

The helper installs `ilspycmd` version `9.1.0.7988` into `artifacts/rea-tools/ilspycmd` on first use, supplies the assembly directory as the dependency search path, disables update checks, and writes the selected type to `artifacts/rea-tools/decompiled/<TypeName>.cs`. Pass a namespace-qualified `-TypeName` when the assembly contains ambiguous short names. Optional `-ReferenceDirectory` and `-OutputDirectory` parameters support other managed assemblies and destination folders.

To list known type names if selection fails:

```powershell
artifacts\rea-tools\ilspycmd\ilspycmd.exe --list c 'C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Rogue Command_Data\Managed\Assembly-CSharp.dll'
```

Use the exact assembly path inspected by REA. Keep generated decompilation, the local CLI, and any game assemblies out of commits and distributable packages.
