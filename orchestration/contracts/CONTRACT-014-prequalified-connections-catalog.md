# CONTRACT-014: AISC 358-16 & Standard Fabrication Connections Catalog

- **Contract ID**: CONTRACT-014
- **Status**: ACTIVE
- **Assigned Worker Agent**: Claude Code CLI / Codex CLI
- **Supervising Architect**: Antigravity (Lead Director)
- **Spec Reference**: `docs/specs/004-mcp-tools-specification.md` §12, `docs/specs/002-data-models-and-schemas.md` §14

---

## 1. Context & Motivation

Engineers and fabricators require reliable, pre-engineered recipes for standard structural joints without having to recalculate plate coordinates from scratch every time. 

AISC 358-16 defines prequalified moment connections for seismic and wind applications, while the AISC Steel Construction Manual (15th/16th Ed) defines standard shear tabs and column base plates.

`CONTRACT-014` introduces parameterized connection recipes directly accessible via MCP:
- **`BFP`**: Bolted Flange Plate (AISC 358-16 Chapter 7)
- **`4E` / `4ES`**: 4-Bolt Unstiffened and Stiffened Extended End-Plate (AISC 358-16 Chapter 6)
- **`8ES`**: 8-Bolt Stiffened Extended End-Plate (AISC 358-16 Chapter 6)
- **`SHEAR_TAB`**: Single-Plate Shear Connection on Beam Web (AISC Manual Part 10)
- **`BASE_PLATE`**: Heavy Column Base Plate with Anchor Rods & Stiffeners

---

## 2. Whitelist of Files

```text
src/mcp_server/catalogs/__init__.py
src/mcp_server/catalogs/aisc_connections.py
src/mcp_server/tools/connection_catalog_tools.py
src/mcp_server/server.py
tests/mcp/test_aisc_catalog.py
```

---

## 3. Scope & Requirements

1. **Recipe Builders (`src/mcp_server/catalogs/aisc_connections.py`)**:
   - Compute exact 3D geometry given beam handle, column handle, section dimensions, and user parameters.
   - Assemble `plates`, `bolt_groups`, and `shop_welds` (`kInShop`) conforming to `model_engineered_connection` schema.
2. **End-Plate Moment Connections (4E, 4ES, 8ES)**:
   - End-plate thickness $t_p$, width $b_p$, extension height $p_{ext}$.
   - Bolt pitch $p_b$, gauge $g$, edge distances $l_e$.
   - Stiffener geometry (triangular stiffener with chamfer cut) welded to beam flange and end-plate.
3. **MCP Tool Integration**:
   - `create_prequalified_connection(client, connection_type, beam_handle, column_handle, parameters)`.
4. **Automated Testing**:
   - Pytest unit tests in `test_aisc_catalog.py` checking geometry generation for 4E, 4ES, and Shear Tab.

---

## 4. Acceptance Criteria

1. 100% test pass rate in pytest (`tests/mcp/test_aisc_catalog.py`).
2. Exact shop welds (`kInShop`) created for all attached plates.
3. Zero unmanaged Advance Steel thread access violations.
