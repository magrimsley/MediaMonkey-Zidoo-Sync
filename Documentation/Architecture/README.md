# Architecture maps and dependency validation

Open either Visual Studio solution. **Architecture Maps** contains the six DGML maps; **MM Zidoo Sync Dependency Validation** contains the native layer diagram. Start with Layers.dgml and Synchronization.dgml. Visual Studio Enterprise 2022 with Code Map and Live Dependency Validation is installed on the development computer.

| File | What it shows |
| --- | --- |
| Layers.dgml | Observed dependencies between four logical layers |
| Synchronization.dgml | Major UI, synchronization, recovery, exporter and device types |
| SynchronizationCalls.dgml | Cross-type explicit calls among those major components |
| Types.dgml | All application types and compiler-resolved source references |
| Calls.dgml | All resolved explicit internal method/constructor calls |
| Projects.dgml | Tracked C# project references and explicit linked application sources |
| DependencyValidation.layerdiagram | Maintained layer assignments and permitted dependency arrows |
| source-evidence.json | Source hashes, coverage and graph counts for this snapshot |

The maps cover 65 authored application C# files and use generated WPF/global-using code to resolve symbols. They exclude archived source, generated nodes, commented-out code and external library internals. Projects.dgml reads tracked project XML; it shows explicit includes, not expanded wildcards or conditional build evaluation. Arrow direction is caller/user to callee/dependency. SourceLocation and SourceLineNumber properties identify source evidence; method descriptions retain overload signatures. These are generated DGML snapshots, not Visual Studio's continuously refreshed semantic database.

## Enforced boundaries

Presentation may reference Services, Models and Branding. Services may reference Models. Models and Branding may not reference the other layers. UI helpers GridSelectionBehavior and WindowsThemeService belong to Presentation even though their source files live under Services. The Services layer currently groups production, planning and isolated recovery implementations; this diagram does not impose finer internal service boundaries.

The application references Microsoft's Microsoft.DependencyValidation.Analyzers 0.11.0 as a private build dependency and supplies the diagram as an AdditionalFiles item. DV dependency, mapping, missing-diagram and internal-analysis diagnostics fail the build. The analyzer runs in the editor and ordinary application builds, including CI builds; the modeling project is a designer container, not the enforcement engine. No runtime package dependency is introduced. Do not suppress DV errors to make the build green. Map new types and review intended dependencies before changing the contract. Regeneration deliberately leaves an existing layer diagram unchanged.

Verified enforcement: temporarily inserting a Models-to-SyncRunner field produced DV0001; restoring the exact source bytes produced a clean build. See validation-result.json. This change does not replace or rebuild the published production installer.

## Refresh the maps

From the repository root:

```text
dotnet build MediaMonkeyZidooSync/MediaMonkeyZidooSync.csproj
dotnet run --project Documentation/Architecture/Tools/ArchitectureMaps.csproj -- .
```

The tool uses the pinned SDK's Roslyn assemblies and the app's evaluated Compile/ReferencePath inputs. It expects the standard Debug/net9.0-windows generated files from the first command and stops on compiler errors. Commit reviewed maps and evidence together. Review source changes against the diagram separately; the tool will not automatically allow new layer dependencies. Debug preprocessor symbols determine this snapshot; other conditional configurations require separate analysis.

## Call graphs versus runtime call stacks

Calls.dgml and SynchronizationCalls.dgml show possible explicit static calls. They do not record execution order, active frames, event/delegate invocation targets, reflection, implicit property accessor calls or every possible interface/virtual implementation. An async call graph does not establish the physical runtime stack. Types.dgml includes references to members/types used in expressions; it does not claim every runtime dependency is statically knowable.

For an actual call-stack map, debug an isolated/offline test under Visual Studio Enterprise, pause at the relevant breakpoint, then use **Debug > Show Call Stack on Code Map**. Save that DGML separately with the scenario, build and breakpoint recorded. Do not run a real synchronization just to obtain a diagram. No runtime stack trace or live Zidoo session was captured for these maps.

## Microsoft references

- [Install Code Map and Live Dependency Validation](https://learn.microsoft.com/en-us/visualstudio/modeling/install-architecture-tools?view=visualstudio)
- [Validate code with dependency diagrams](https://learn.microsoft.com/en-us/visualstudio/modeling/validate-code-with-layer-diagrams?view=visualstudio)
- [Use code maps to debug applications](https://learn.microsoft.com/en-us/visualstudio/debugger/use-code-maps-to-debug-your-applications?view=visualstudio)

Local solution integration also corrected an existing duplicate root folder name: the historical Packaging folder now displays as Local Packaging Archive. Its GUID, child membership and archived file paths remain intact. Both public and local solution builds pass.
