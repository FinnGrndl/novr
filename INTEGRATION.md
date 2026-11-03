# NOVR Integrations-Branch `claude/project-thread-sy6vhv`

Stand: 2026-11-03. Basis: `main` (v0.4.3, inkl. Nuclear-Option-0.34-Kompatibilität).

## Wie die Branches zusammenhängen

Fast alle `codex/*`-Branches sind **ein einziger gestapelter Stack** (Upstream-PRs in
nubtron/novr, jede PR baut auf der vorherigen auf). Jede höhere Stufe enthält alle
darunter. Die Reihenfolge von unten nach oben:

| Stufe | Branch | Upstream-PR | Inhalt |
|---|---|---|---|
| 1 | `codex/config-sections` | #20 | Features binden ihre Config selbst (`[ConfigSection]`) |
| 2 | `codex/vr-cursor-enhancer` | #22 / #1 | Besser sichtbarer VR-Cursor |
| 3 | `codex/vr-hud-scale` | #2 | HUD-Skalierung, Pitch-Ladder |
| 4 | `codex/game-api-compat` | #3 | Glideslope-API-Fix |
| 5 | `codex/color-grade` | #4 | Kontrast/Sättigung/Gamma |
| 6 | `codex/motion-cursor` | #5 | Controller-Cursor + Laserpointer |
| 7 | `codex/hmd-gaze-cursor` | #6 | Kopf-Blick-Cursor |
| 8 | `codex/vr-harness` | #10 | Test-Harness (Debug, standardmäßig aus) |
| 9 | `codex/escalation-overrides` | #9 | `[Escalation]`-Config-Overrides |
| 10 | `codex/vr-disable-option` | #11 | VR per Config / `--no-vr` abschalten, Fallback ohne XR |
| 11 | `codex/hud-marker-blank-quad` | #13 | Fix: HUD-Marker als volle Kästen |
| 12 | `codex/vr-menu-capture` | #14 (+#16, #19 hineingemerged) | Menüs über Capture-Backend |
| 13 | `codex/offscreen-ui-cull` | #21 | Fix: offscreen-UI als Szenerie |
| 14 | `codex/hud-capture` | #23 (+#24, #26 zusammengefasst) | Original-HUD in VR, HMD-Visor, Blickauswahl, View-Icons |
| 15 | `codex/spectator-camera` | #25 | Zuschauerkamera |
| 16 | `codex/mission-editor` | #27 | Missionseditor in VR |
| 17 | `codex/stick-cursor` | #30 | Cursor per Stick |
| 18 | `codex/map-hold-recenter` | #31 | Recenter durch Halten der Kartentaste |
| 19 | `codex/focus-warning` | #32 | Warnung, wenn das Spielfenster den Fokus verloren hat |
| 20 | `feature/vr-enhancements` | – | Stack + VR-Zoom (Mark Foley), Kopf-Offset Schleudersitz, Recenter-Shortcut, Cockpit-Offset pro Flugzeug |

## Was in diesem Branch ist

1. `feature/vr-enhancements` (= der komplette Stack oben, Stufe 1–20)
2. `codex/near-tree-detail` — zusätzlich gemerged, konfliktfrei. Echte 3D-Bäume in der Nähe
   statt gekreuzter Flächen. **Standardmäßig aus**: `[...] Near Trees = true` in der Config setzen.

## Fixes aus dem ersten Test (2026-11-03)

| Commit | Problem | Fix |
|---|---|---|
| `afa7cb8` | Build bricht gegen Nuclear Option 0.34 ab (`MissionsPicker`, `QuickLoadMany`, Workshop-`Tag`) | an die neue API angepasst, `MissionQuickLoadHelper` baut `QuickLoadMany` auf `QuickLoadOne` nach |
| `7c34aa4`, `b83eb64` | Gras und Bäume nur in einem Kegel vor dem Piloten | GPU-Culling nutzt das Sichtfeld beider Augen plus 10° Rand statt des Monitor-Frustums |
| `b83eb64` | ~250 Fehler/s „RenderTexture.Create failed“, Hänger | Capture-Ziele bekommen einen 24-Bit-Tiefenpuffer |
| `b83eb64` | Patcher bricht an gesperrter `Unity.XR.Management.dll` ab, XR-Dateien veralten | identische Dateien überspringen, gesperrte Dateien auslassen statt abbrechen |
| `0f359d6` | Waffenanzeige und Minimap fehlen bei HMD-Breite > 1920 px | herausragende Elemente werden in die Aufnahme zurückgezogen |
| `0f359d6` | Stick lenkt bei offener Karte nicht | reine Tastatur-Überschneidung sperrt die Flugachsen nicht mehr |
| `1ee5bb3`, `ae8fed7` | F1 Configuration Manager nur auf dem Monitor | IMGUI wird auf das Menü-Panel im Headset umgeleitet, VR-Cursor 1:1, VR-Klick per `SendInput` |

Bekannt: `Captured HMD Panel Shading` (Standard 0,65) erzeugt schwarze Schlieren um Minimap und Waffenanzeige; mit 0 sind sie weg. Die Hinterlegung müsste auf die Form der Hintergrund-Sprites beschnitten werden.

## Was bewusst NICHT gemerged ist

| Branch | Grund |
|---|---|
| `codex/vr-map-3d` (PR #28) | Auf einem alten Stand des Stacks entwickelt; 16 Dateien / ~125 Konflikt-Hunks mit dem neueren HUD-Capture- und Cursor-Code. Nicht ohne Kompilieren und Testen im Spiel seriös auflösbar. Kandidat für einen späteren eigenen Schritt (Rebase auf den Stack). |
| `codex/hud-alpha-blending` (PR #12) | Vom Autor ausdrücklich „off-stack“: kaum Gewinn, verschlechtert kleine Icons. |
| `codex/vr-masked-ui-queue` (#15), `codex/vr-menu-draw-order` (#18), `codex/gaze-anchor-baseline` (#17), `codex/debug-buffer-dump` (#7) | PRs geschlossen; durch #19/#23/Harness ersetzt bzw. zurückgenommen. |
| `codex/pr37-agent-vr-zoom`, `codex/vr-enhancements-zoom` | Ältere Fassungen des VR-Zooms, der schon in `feature/vr-enhancements` steckt. |
| `rebuild/feature`, `experimental/*`, `backup/*` | Alte Zwischenstände/Experimente bzw. Sicherungskopien. |
| `nubtron/test-create-pr-tool` (#29) | Nur ein Test-Marker. |

## Bauen und installieren (Windows)

Voraussetzungen: Visual Studio 2022 Build Tools bzw. .NET SDK mit .NET-Framework-4.8-Targeting-Pack,
BepInEx 5.x im Spielordner (`D:\SteamLibrary\steamapps\common\Nuclear Option\BepInEx\core` muss existieren),
das Spiel geschlossen.

```powershell
cd C:\Users\finng\Documents\projects\NOVR_DEV\novr
git fetch origin
git checkout claude/project-thread-sy6vhv
git pull
dotnet build NuclearOptionVirtualRealityMod.sln -c Release
```

Der Spielordner unter `D:\SteamLibrary\...` wird automatisch erkannt. Der Build kopiert
`NOVR.dll` nach `BepInEx\plugins\NOVR` und den Patcher nach `BepInEx\patchers\NOVR`.
Danach Nuclear Option über Steam starten (Quest 3 per Link/Air Link bzw. SteamVR/OpenXR-Runtime aktiv).
