# CONTRACT-010: Spatial Grids and Building Levels Engine

- **Contract ID**: CONTRACT-010
- **Status**: ACTIVE
- **Assigned Worker Agent**: Lead Director / Claude Code Worker 1
- **Spec Reference**: `docs/specs/004-mcp-tools-specification.md` §8, `docs/specs/002-data-models-and-schemas.md` §10

---

## 1. Context & Motivation

In BIM workflows (particularly when interfacing with Autodesk Revit or generating shop/arrangement drawings), spatial references are mandatory:
1. **Grids**: Structural column lines (`Grid1D` / `Grid`) are required so that beams, columns, and foundations have coordinate reference marks (e.g. Axis 1..7, Axis A..D) on 2D drawings and Revit synchronizations.
2. **Building Levels**: Elevation planes (`LevelObject`) registered in Advance Steel's `BuildingStructureManager` establish floor levels (e.g. `+0.00m` Foundation, `+6.00m` Eave, `+9.50m` Ridge).

---

## 2. API Endpoints

### Endpoint A: `POST /api/v1/spatial/grid`
Creates 1D or multi-axis grid lines.

#### Request Parameters
- `origin` (`list[float]`, default `[0, 0, 0]`): 3D insertion point.
- `axis_direction` (`list[float]`, default `[0, 1, 0]`): Direction of each grid line.
- `spacing_direction` (`list[float]`, default `[1, 0, 0]`): Direction along which lines repeat.
- `line_length` (`float`, default `30000.0`): Length of each grid line.
- `count` (`int`, optional, default 2): Number of parallel lines.
- `spacing` (`float`, optional, default `5000.0`): Distance between adjacent lines.
- `spacings` (`list[float]`, optional): List of distances between adjacent lines.
- `labels` (`list[str]`, optional): Explicit names for each grid axis.
- `label_prefix` (`str`, optional, default `"1"`): Prefix if auto-numbering.
- `text_location` (`str`, default `"Both"`): Bubble text location (`"Start"`, `"End"`, `"Both"`).

#### Response
```json
{
  "grid_handle": "94E",
  "grid_type": "k1DGrid",
  "axis_count": 7,
  "axes": [
    { "name": "1", "start": [0, 0, 0], "end": [0, 30000, 0], "length": 30000 },
    ...
  ]
}
```

---

### Endpoint B: `POST /api/v1/spatial/level`
Creates a `LevelObject` registered in the building structure tree.

#### Request Parameters
- `name` (`str`): Level designation (e.g. `"Nivel +6.00m (Alero)"`).
- `elevation` (`float`): Absolute height in mm along global Z.
- `level_below_handle` (`str`, optional): Handle of lower level.
- `level_above_handle` (`str`, optional): Handle of upper level.

#### Response
```json
{
  "handle": "955",
  "name": "Nivel +6.00m (Alero)",
  "elevation": 6000.0,
  "tree_parent": "BuildingStructureTreeObject",
  "registered": true
}
```

---

## 3. Whitelist of Files

```text
src/as_plugin/Commands/Handlers/SpatialCommandHandler.cs
src/as_plugin/Commands/CommandDispatcher.cs
src/mcp_server/tools/spatial_tools.py
src/mcp_server/server.py
tests/mocks/mock_as_plugin.py
tests/mcp/test_grids_and_levels.py
tests/mcp/test_tools.py
```

---

## 4. Acceptance Criteria

1. C# builds with 0 warnings: `dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release`.
2. Pytest suite passes: `pytest tests/mcp/test_grids_and_levels.py -v`.
3. Offline mock server returns valid payload.
4. Live CAD execution successfully registers axes and levels in Advance Steel database.
