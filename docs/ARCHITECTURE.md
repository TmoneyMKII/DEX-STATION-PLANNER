# Architecture

## Projects

| Project | Purpose |
|---|---|
| `DexStationPlanner.Core` | Domain model, geometry, statistics, mesh generation, globe scene building, serialization. No UI dependencies, so it's ready for future PDF generation, simulation, a CLI or other front ends. |
| `DexStationPlanner.App` | WPF (.NET 10) desktop app, MVVM via CommunityToolkit.Mvvm. Custom-drawn plan canvases, HelixToolkit 3D preview, CesiumJS globe in WebView2. |
| `DexStationPlanner.Core.Tests` | xUnit tests for Core. |

## Data model (Core/Model)

```
StationProject                    (.dexstation file root)
├── CustomEquipment[]             project-specific additions/overrides to the built-in catalog
├── Modules[]   ModuleDefinition  reusable designs
│   ├── Shell[]        ShellVertex     closed outer envelope; Bulge curves the edge to the next vertex
│   ├── InteriorWalls[]                free-standing partitions (straight or curved)
│   ├── Openings[]     door/window/hatch/vent; HostId = shell vertex (its edge) or interior wall
│   ├── Connectors[]   join points on shell edges, facing outward
│   └── Equipment[]    placements referencing catalog ids
└── Stations[]  Station
    ├── Site           lat/lon, ground elevation, grid heading
    ├── Modules[]      ModuleInstance: definition id + pose on the site grid
    ├── Links[]        connector to connector (mated, or a corridor when apart)
    └── UtilityRuns[]  typed polylines between modules
```

Model classes are `ObservableObject`s, so the UI binds to them directly. Derived data (footprints, stats, meshes) is always computed, never stored.

## Conventions

- **Units:** SI everywhere in the model (m, kg, W). `UnitFormatter` and `UnitContext` convert for display only.
- **Plan axes:** X right/east, Y up/north, rotations counterclockwise in degrees. Screen Y is flipped in `PlanCanvasBase`.
- **Arcs:** CAD/DXF *bulge*: `bulge = tan(sweep/4)`. 0 is straight, ±1 is a semicircle, positive sweeps counterclockwise. See `Geometry/Segment.cs`.
- **Shell winding:** may be drawn either way; `ModuleGeometry.Footprint` normalizes to counterclockwise, and connector "outward" accounts for winding.
- **Site grid:** `GridHeadingDegrees` rotates grid north clockwise from true north. `GlobeSceneBuilder` applies it, producing true east/north offsets from the site origin.
- **Globe placement:** C# sends local east/north meters; the page converts them with Cesium's `eastNorthUpToFixedFrame` at the site origin, which stays correct at the poles (no lat/lon math in C#).

## File format

`.dexstation` is indented camelCase JSON (`ProjectSerializer`). Points are written as `[x, y]` arrays rounded to 0.1 mm. `schemaVersion` guards against opening files from newer versions. Saves write to a temp file and then atomically replace the original. Secrets such as the Cesium ion token are kept in `%AppData%\DEX Station Planner\settings.json`, never in projects.

## App structure

- `MainViewModel`: owns the project, file commands, dirty state, and **project-wide snapshot undo**. A `DeepChangeObserver` watches the whole object graph; changes are checkpointed after 400 ms of quiet (or immediately when an editor gesture ends), and never while the mouse is captured mid-drag.
- `ModulesViewModel` / `StationsViewModel`: page state, selection, and per-element property editors (`ElementEditors.cs`), which expose lengths in display units.
- `FloorPlanCanvas` / `SitePlanCanvas` (on `PlanCanvasBase`): immediate-mode `DrawingContext` rendering with pan/zoom, grid, snapping, and tool state machines.
- `ModulePreview3D`: rebuilds a HelixToolkit scene from `ModuleMeshBuilder` output (throttled).
- `GlobePage`: hosts WebView2, maps `wwwroot/` to `https://dex.local/`, and exchanges JSON messages with `Web/globe.js`.

### Globe message protocol

| Direction | Message | Payload |
|---|---|---|
| App → page | `init` | `ionToken`, `theme` |
| App → page | `scene` | `GlobeScene` (volumes and lines in ENU meters) |
| App → page | `flyTo` | `lat`, `lon`, `range` or `toStation` |
| App → page | `placeMode` | `enabled`, `name` |
| App → page | `theme` | `dark` or `light` |
| Page → app | `ready`, `placed {lat, lon, height}`, `placeCancelled`, `log {level, message}` | |

Messages sent before `init` finishes are queued by the page and replayed.

### Imagery
The base layer is Cesium's bundled Natural Earth II (offline, geographic projection, so it covers the poles). Esri World Imagery or OSM sits on top; Esri's "Map data not yet available" placeholder tiles are discarded so coarser imagery shows instead. With an ion token, Bing imagery and Cesium World Terrain are used.

## Build notes
CesiumJS is pinned in `src/DexStationPlanner.App/Web/package.json`. The App build runs `npm ci` on first build and copies `node_modules/cesium/Build/Cesium` to `wwwroot/cesium` in the output (and in publish output).
