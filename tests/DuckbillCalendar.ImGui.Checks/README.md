# Native ImGui layout regression checks

Run on Windows x64 from the repository root:

```powershell
dotnet run --project tests/DuckbillCalendar.ImGui.Checks --property:DalamudLibPath="$env:APPDATA/XIVLauncherKR/addon/Hooks/dev"
```

The project links the production MainWindow, UiWidgets, ThemeScope, UiText and CalendarModel source files. It loads the inspected local Dalamud bindings and `cimgui.dll`, uses installed Windows Malgun Gothic, and runs native ImGui frames without a GPU renderer or game process. No native binaries or fonts are committed. Missing local references, native DLL or Korean font are errors; these checks do not silently skip them.

At 1x, 1.5x and 2x font/style scales, the 30 assertions cover:

- Negative controls reproducing the previous expanding auto-sized input form and the zero-wrap-width Korean tooltip.
- The actual editor's stable width over 100 frames and bounded viewport dimensions.
- The actual Korean tooltip's readable width and bounded height.
- Real ImGui text input events into the actual editor, preserving complete `2026-11-12` and `23:59` values. Native navigation activation is used only by the test harness.
- Ten full MainWindow frames with the real bundled event feed at each scale.

Geometry CSV reports go to `.preview/imgui-checks/<run-id>/`. Passing geometry checks verifies layout calculation and input behavior. GPU rendering, in-game fonts, Dalamud integration and visual appearance still require game validation.
