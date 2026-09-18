# Advance Steel Domain Modeling Rules (`rules/advance-steel-modeling.md`)

## 1. Units and Coordinate Systems
- **Default Unit System**: Millimeters (`mm`) for length, Radians (`rad`) or Degrees (`deg`) explicitly marked for angles.
- **Coordinate Reference**:
  - Always verify whether inputs are relative to **WCS (World Coordinate System)** or active **UCS (User Coordinate System)**.
  - Profile reference axes: `Center` (default for columns/beams), `TopCenter` (standard for purlins/floor beams), `BottomCenter` (overhead cranes).

## 2. Structural Member Roles (`ModelRole`)
Every element in Advance Steel must have a valid `ModelRole` assigned to ensure automated drawing styles and numbering prefixes trigger properly:
- `Column`: Vertical main compression members.
- `Beam`: Horizontal floor/girder members.
- `Rafter`: Sloped roof gable members.
- `Bracing`: Diagonal tension/compression cross members.
- `Purlin`: Secondary roof envelope members.
- `BasePlate`: Plate connecting column base to foundation.
- `EndPlate`: Connection plate welded to beam/column ends.
- `Stiffener`: Web or flange reinforcement plate.
- `GussetPlate`: Connection plate for bracing trusses.

## 3. Welds and Assembly Composition Rules (CRITICAL)
In Autodesk Advance Steel, the **Assembly (Conjunto de taller)** is strictly defined by workshop welds and workshop bolts:
1. **Weld Location**:
   - **`Workshop` (Taller)**: Binds connected members into the **same shop assembly**. Used for shop-welded end plates, stiffeners, cleats, and base plates.
   - **`Site` (Obra/Campo)**: Connects elements in the field without merging them into the same shop assembly. Used for field welds between distinct shop pieces.
2. **Main Part Designation**:
   - Every shop assembly **must have exactly one Main Part**.
   - The Main Part dictates the assembly prefix (e.g. `C1` for Column 1), the orientation in shop fabrication drawings, and the CNC/DSTV coordinate origin.
   - **RULE**: The Main Part must ALWAYS be the primary profile (e.g. the column shaft or rafter beam). **Never designate a stiffener, clip angle, or end plate as the Main Part.**

## 4. Modeling Tolerances
- Point Coincidence: 0.1 mm.
- Coplanarity tolerance for plate contours: 0.05 mm.
- Minimum plate thickness: 3.0 mm.
