# CS2 Flight Takeover Alpha 0.6 — React UI + centered camera

## What changed
- Removed Unity IMGUI (`FlightPanel.cs`, `PanelBootstrapSystem.cs`).
- Added native CS2 React/Coherent UI launcher in `GameTopLeft`, aircraft list, selection, controls, telemetry.
- Added `FlightUISystem` C# UI bindings and trigger handlers.
- Added `FlightInputSystem` to preserve keyboard flight controls without the MonoBehaviour panel.
- Camera now follows the **live** Game.Objects.Transform every update, with no +50m vertical offset; direct tracking without a 50m offset or smoothing lag.
- Kept existing experimental aircraft movement system; native AI can still fight it.

## Build
1. Install CS2 official modding toolchain and set `CSII_TOOLPATH` and `CSII_USERDATAPATH` (the latter is used by webpack).
2. Install Node.js/npm and use Visual Studio 2022 to build the C# project. The project runs `npm.cmd install` once, then `npm.cmd run build` for React UI.
3. The webpack output is placed in `%CSII_USERDATAPATH%\Mods\CS2FlightTakeover` as `CS2FlightTakeover.js` and `.css`. Ensure the toolchain packages the C# assembly and UI files as a single mod, and check the in-game mod load log.
4. In a city, use the `FLIGHT` button in the game's upper-left UI. Select aircraft, TAKE CONTROL. W/S pitch, A/D roll, Q/E yaw, R/F throttle, Esc stop.

## Limitations
Not compiled or runtime-tested in this environment. Camera orbit distance and orientation are still controlled by the game's camera; pivot centering corrects the target but is not a guaranteed chase camera. The UI uses native CS2 UI bundling conventions from FlightTrackerCS2. The camera may still have update-order lag if the game runs camera updates before this system. React/Coherent UI module loading requires correct mod packaging and matching game version.

## Alpha 0.6 fixes
- Corrected obsolete/invalid `Time.deltaTime` to `SystemAPI.Time.DeltaTime` in aircraft control.
- Camera uses live aircraft transform every UI update with direct pivot tracking.
- Flight input registered in GameSimulation, independent of Unity IMGUI.
- No `FlightPanel.cs` or `PanelBootstrapSystem.cs` in the project.
- Requires CS2 modding toolchain and Node.js; not compiled or runtime-tested here.
