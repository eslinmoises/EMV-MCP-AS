# CONTRACT-009: Parametric Trussed Warehouse Generator (`version1.dwg`)

- **Contract ID**: CONTRACT-009
- **Status**: ACTIVE
- **Assigned Worker Agent**: Lead Director / Claude Code
- **Spec Reference**: `docs/specs/004-mcp-tools-specification.md` §9, `docs/specs/002-data-models-and-schemas.md` §11

---

## 1. Context & Motivation

Inspection of real-world production model `version1.dwg` revealed a 7-frame industrial warehouse constructed entirely with square hollow sections (`RHS_Sections_square_c nach DIN#@§@#Q90X3`):
- **Span $L$**: 31,000 mm (31.0 m)
- **Length $Y$**: 30,000 mm (30.0 m) with 7 frames modulated every 5,000 mm ($Y = 0, 5000, 10000, 15000, 20000, 25000, 30000$)
- **Eave Height**: 6,000 mm to 7,897 mm (constant vertical truss depth $D \approx 2,000$ mm)
- **Ridge Apex Height**: 9,500 mm (10.34% roof slope)
- **Columns**: Double chord lattice columns (width 1,000 mm) with horizontal struts every 1,000 mm and 45° lacing diagonals ($L=1414$ mm)
- **Rafters**: Double pitch Warren truss with vertical struts ($L=1989$ mm) and diagonals ($L=2820$ mm)
- **Total Members**: 532 bars.

The goal of this contract is to parameterize this exact structural topology into a generative macro `create_trussed_warehouse` allowing the user to generate warehouses of arbitrary spans, lengths, bay spacings, and heights in a single atomic transaction.

---

## 2. API Endpoint & Model

### `POST /api/v1/elements/trussed-warehouse`

#### Request Parameters
| Field | Type | Default | Description |
|---|---|---|---|
| `span` | double | 31000.0 | Transverse span in mm |
| `length` | double | 30000.0 | Longitudinal length in mm |
| `bay_spacing` | double | 5000.0 | Distance between frames along Y |
| `eave_height` | double | 6000.0 | Height to lower rafter chord at eave |
| `ridge_height` | double | 9500.0 | Height to apex ridge |
| `column_width` | double | 1000.0 | Width of double chord column |
| `truss_depth` | double | 2000.0 | Vertical depth of roof truss |
| `profile` | string | `"RHS_Sections_square_c nach DIN#@§@#Q90X3"` | Advance Steel profile key |
| `material` | string | `"S235JR"` | Steel material grade |
| `create_grids_and_levels` | bool | true | Automatically generate matching grids & levels |

#### Advance Steel Modeling Rules
1. **Single-Threaded AutoCAD/AS Law**: Guard all database insertions with `DocumentLock` and `Transaction` on the main thread via `MainSyncContext.Post`.
2. **Out-of-Plane Member Orientation**: For all members in the vertical XZ plane (chords, columns, struts, diagonals), pass `Vector3d.kYAxis` as the reference vector to keep web orientations vertical and prevent profile twisting.
3. **Base Plates and Anchor Bolts**: Column bases receive base plates ($400\times 400\times 25$ mm) and 4 anchor bolts directed downward ($\vec{n} = [0, 0, -1]$).
4. **Spatial Grids**: Generate transverse grid lines 1..N at each bay and longitudinal grid lines A..D at column lines.
5. **Building Levels**: Register levels at 0.00m, eave, and ridge in `BuildingStructureManager`.

---

## 3. Whitelist of Files

```text
src/as_plugin/Commands/Handlers/TrussedWarehouseCommandHandler.cs
src/as_plugin/Commands/CommandDispatcher.cs
src/mcp_server/tools/modeling_tools.py
src/mcp_server/server.py
tests/mocks/mock_as_plugin.py
tests/mcp/test_trussed_warehouse.py
```

---

## 4. Acceptance Criteria

1. C# builds with 0 warnings: `dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release`.
2. Test suite passes: `pytest tests/mcp/test_trussed_warehouse.py -v`.
3. Offline mock returns valid response matching schema in SPEC-002 §11.
4. Generates warehouse of 7 frames and 532 bars when default parameters are supplied.
