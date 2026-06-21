Project Structure Overview
Top-Level Layout
A:\Project\openfo3-reengine\
├── .editorconfig
├── .gitattributes
├── .gitignore # Redot 4+ specific ignores
├── config.json # Runtime config (BSA paths, world settings, scale)
├── icon.svg # Project icon
├── icon.svg.import
├── OpenFo3- ReEngine.csproj # Redot.NET.Sdk/26.1.0, .NET 8.0
├── OpenFo3- ReEngine.sln # Visual Studio solution
├── project.godot # Engine config: Redot 26.1, C#, Forward Plus
├── README.md # Project README (Japanese)
├── AI/ # 40+ Python scripts for reverse engineering/verification
├── Assets/ # Screenshots, task.md, image.png
├── config.json # Runtime configuration
├── doc/ # 18 feature/fix documentation folders (Markdown)
├── FalloutData/ # (empty or data)
├── fopdoc/ # Fallout Plugin File Format documentation submodule
├── Scenes/ # World scenes
│ └── World/
│ └── Megaton.cs # Main game script (1440 lines)
├── Scripts/ # C# source code
│ ├── BSA/ # Bethesda BSA archive reader
│ ├── Core/ # GamePaths.cs, FlyCam.gd
│ ├── ESM/ # Fallout3.esm parser
│ ├── NIF/ # NIF 3D model parser
│ └── World/ # World loading utilities
Project Purpose (from README.md and AI/doc.md)
Fallout 3: Re Engine is an experimental project to reimplement the Fallout 3 game engine on top of the Redot Engine (a Godot fork). It directly reads original game assets (ESM, BSA, NIF, DDS) without conversion. The project currently successfully loads and displays Megaton's geometry, textures, and object placements.
Tech Stack:

- Game logic: C# (.NET 8.0, Redot.NET.Sdk/26.1.0)
- Engine: Redot Engine 26.1 (Godot fork, Forward Plus)
- Camera: GDScript (FlyCam.gd)
- Analysis/verification: Python 3 (40+ scripts)
- No external NuGet packages
  Rotation/Transform/Quaternion/Axis Conversion - All Files Found
  Core Rotation Math Files (C#)

1. A:\Project\openfo3-reengine\Scripts\NIF\NIFMeshBuilder.cs
   The central rotation conversion file.

- Line 53-57: Defines R_fo3_to_godot -- the fundamental axis conversion matrix:
  private static readonly Basis R_fo3_to_godot = new Basis(
  new Vector3(1, 0, 0), // FO3 X -> Godot X
  new Vector3(0, 0, -1), // FO3 Y -> Godot Z (negated)
  new Vector3(0, 1, 0) // FO3 Z -> Godot Y
  );
- Line 188-194: Converts FO3 transform to Godot space: godotBasis = R_fo3_to_godot \* fo3Transform.Basis
- Line 239-267: Builds ArrayMesh with FO3->Godot conversion:
- v_godot = (R_conv _ R_fo3) _ v_fo3_local + R_conv \* t_fo3
- Applies WorldScale = 0.015
- Line 410-420: TraverseExtract hierarchy traversal with parent-child transform accumulation
- Line 349-357: Skinned vertex conversion using same R_fo3_to_godot

2. A:\Project\openfo3-reengine\Scenes\World\Megaton.cs
   REFR (object placement) rotation application.

- Line 49-51: InstanceRequest struct with Vector3 Position and Vector3 Rotation
- Line 965-969: Skinned node transform: Basis(Up, -rz) _ Basis(Forward, -ry) _ Basis(Right, -rx)
- Line 985-989: Mesh transform: same formula Basis(Up, -rz) _ Basis(Forward, -ry) _ Basis(Right, -rx)
- Line 1020-1024: Particle system transform: same formula
- Line 712-717: Reads raw FO3 rotation values from DATA subrecord (rx, ry, rz)
- Line 729-733: Position conversion: (px-cx)*scale, pz*scale, -(py-cy)\*scale
- Line 806-848: SCOL part placement with full Euler angle matrix composition (sin/cos of parent + local rotations combined)

3. A:\Project\openfo3-reengine\Scripts\NIF\NIFBlockResolver.cs
   NIF node rotation matrix reading.

- Line 40-41: Node struct has Vector3 Translation and Basis Rotation
- Line 398-408: Reads 3x3 rotation matrix (row-major) and constructs Godot Basis column-wise:
  node.Rotation = new Basis(
  new Vector3(m11, m21, m31), // colX = first column of stored matrix
  new Vector3(m12, m22, m32), // colY = second column
  new Vector3(m13, m23, m33) // colZ = third column
  );

4. A:\Project\openfo3-reengine\Scripts\NIF\NIFCollisionBuilder.cs
   Collision body rotation with quaternion.

- Line 216-247: Havok rigid body translation and quaternion rotation:
  bodyTranslation = new Vector3(tx, tz, -ty); // FO3 -> Godot
  bodyRotation = new Basis(new Quaternion(qx, qz, -qy, qw)); // Quaternion with axis swap

5. A:\Project\openfo3-reengine\Scripts\NIF\NIFSkinningData.cs
   Skinning transform data with rotation.

- Line 10: NiTransformData struct has Basis Rotation
- Line 117-138: ReadNiTransform reads 3x3 rotation matrix + translation + scale

6. A:\Project\openfo3-reengine\Scripts\World\LightingLoader.cs
   Light rotation conversion.

- Line 229-232: Directional light rotation: dir.Rotation = new Vector3(zRad, xyRad, 0)
- Line 258, 310-314: Omni/Spot light rotation: Basis(Up, -rz) _ Basis(Forward, -ry) _ Basis(Right, -rx) with comment: "OpenMW-style Ry(-rz) @ Rz(ry) @ Rx(-rx)"

7. A:\Project\openfo3-reengine\Scripts\World\TerrainBuilder.cs
   Terrain vertex coordinate conversion.

- Line 496-502: Terrain position: godotX = (originX + col*step - center.X) * scale, godotY = height * heightScale, godotZ = -(originY + row*step - center.Y) \* scale

8. A:\Project\openfo3-reengine\Scripts\World\NavMeshBuilder.cs
   Navigation mesh coordinate conversion.

- Line 97-98: Navmesh vertex: new Vector3(x, z, -y) - FO3 (X,Y,Z) -> Godot (X,Z,-Y)
  Rotation Verification Scripts (Python)

9. A:\Project\openfo3-reengine\AI\verify_refr_math.py
   Tests REFR rotation math. Compares 4 methods:

- Method 1 (current C#): Ru(Rz) @ Rf(Ry) @ Rx(Rx)
- Method 2 (similarity transform): R_conv @ R_fo3 @ R_conv^-1
- Method 3 (mapped extrinsic): Rz_godot @ Ry_godot @ Rx_godot
- Method 4 (Claude suggestion using Back instead of Forward)
- Tests with real Megaton REFR data (wall yaw 91.67, 199.57, complex tilts)

10. A:\Project\openfo3-reengine\AI\verify_refr_math2.py
    Critical analysis confirming similarity transform. Proves mathematically that:

- R_node = R_conv @ R_fo3_refr @ R_conv^-1 is the correct Node3D basis
- Compares "correct" similarity transform vs current code for yaw=90 degree test case

11. A:\Project\openfo3-reengine\AI\verify_refr_math3.py
    Verifies NIF rotation matrix storage order. Confirms:

- NIF stores Matrix33 in row-major order
- C# code correctly constructs Basis from columns of the row-major matrix
- Searches all NIF dumps for non-identity rotation matrices and extracts Euler angles

12. A:\Project\openfo3-reengine\AI\verify_nif_transform.py
    Tests NIFMeshBuilder bake of NIF internal rotation + R_conv.

- Tests scenario: NIF rotation = 90deg around FO3 Z-axis
- Verifies position conversion: R_conv @ (x*scale, y*scale, z*scale) = (x*scale, z*scale, -y*scale)
- Confirms: ">>> Position conversion is CORRECT <<<"

13. A:\Project\openfo3-reengine\AI\verify_fix.py
    Tests proposed rotation fix: Right(rx) -> Forward(ry) -> Up(rz) sequence vs current Up(-rz) -> Forward(ry) -> Right(rx) vs similarity transform. Tests 24 real rotation samples from Megaton.
14. A:\Project\openfo3-reengine\AI\verify_full_pipeline.py
    Tests full pipeline with real NIF rotation matrix. Tests NIF rotation (90deg around X) + REFR rotation + coordinate conversion.
15. A:\Project\openfo3-reengine\AI\verify_circular.py
    Verifies REFR rotation by checking circular wall arrangement. Analyzes walls by Z-rotation to check if they point radially.
16. A:\Project\openfo3-reengine\AI\dump_refr_rotations.py
    Dumps REFR rotations from ESM. Reads REFR DATA subrecords, extracts rx, ry, rz values.
17. A:\Project\openfo3-reengine\AI\dump_megaton_refr.py
    Dumps Megaton REFR data. Reports rotation statistics (X-only/Y-only/Z-only/multi-axis counts).
18. A:\Project\openfo3-reengine\AI\scan_wall_nifs.py
    Scans wall NIFs for non-identity rotations. Checks NIF header rotation matrices.
19. A:\Project\openfo3-reengine\AI\verify_nif_transform.py, verify_sign.py, verify_wire02.py, trace_node.py, trace_node2.py
    Additional verification scripts dealing with NIF transform data and matrix layout.
    Rodrigues Rotation Formula (used across all Python scripts)
    The rot_matrix(axis, angle_rad) function appears in multiple verification scripts:
    def rot_matrix(axis, angle_rad):
    axis = np.array(axis, dtype=float)
    axis = axis / np.linalg.norm(axis)
    c, s = math.cos(angle_rad), math.sin(angle_rad)
    K = np.array([[0, -axis[2], axis[1]],
    [axis[2], 0, -axis[0]],
    [-axis[1], axis[0], 0]])
    return np.eye(3) + s*K + (1-c)*(K @ K)
    References to OpenMW, OpenNW, Godot, Redot
    OpenMW (1 match)

- A:\Project\openfo3-reengine\Scripts\World\LightingLoader.cs line 310:
  // No re-conversion needed. Rotation: OpenMW-style Ry(-rz) @ Rz(ry) @ Rx(-rx).
  This is a comment referencing OpenMW's rotation convention as an implementation note.
  OpenNW (0 matches)
  No references found anywhere in the codebase.
  Godot (89 matches across all file types)
  Key references by category:
- All C# source files (using Godot;) -- 13+ files
- project.godot: config/features=PackedStringArray("26.1", "C#", "Forward Plus", "Redot")
- Documentation: Discusses Godot coordinate system, Godot API, Godot units conversion
- Verification scripts: Extensively reference Godot Basis, Godot space, Godot vectors
  Redot (found across .md, .csproj, .gitignore, project.godot)
- README.md: "Fallout 3をRedot Engineで再構成する実験的なプロジェクト"
- project.godot: config/features=PackedStringArray("26.1", "C#", "Forward Plus", "Redot")
- OpenFo3- ReEngine.csproj: <Project Sdk="Redot.NET.Sdk/26.1.0">
- AI/doc.md: References Redot Engine 26.1 as the target platform
- All doc/.md files\*: Reference Redot API mappings and behavior
  Summary of the Rotation/Transform Pipeline
  The coordinate conversion from Fallout 3 (Gamebryo) to Godot/Redot follows this pattern:

1. FO3 coordinate system: X=right, Y=forward (north), Z=up
2. Godot coordinate system: X=right, Y=up, Z=forward
3. Axis conversion matrix (R_fo3_to_godot):

- FO3 (x, y, z) -> Godot (x, z, -y)
- As a Basis matrix: colX=(1,0,0), colY=(0,0,-1), colZ=(0,1,0)

4. NIF internal transforms: godotBasis = R_conv _ fo3Basis, godotOrigin = R_conv _ fo3Origin
5. REFR (object placement) rotation: Three formulas are tested:

- Current code: Basis(Up, -rz) _ Basis(Forward, -ry) _ Basis(Right, -rx)
- Proposed fix: Basis(Up, rz) _ Basis(Forward, ry) _ Basis(Right, rx) (reverse order, remove negation)
- Similarity transform (theoretically correct): R_conv @ R_fo3_refr @ R_conv^-1

6. Collision rotation: Uses quaternion new Basis(new Quaternion(qx, qz, -qy, qw))
7. World scale: WorldScale = 0.015 (1 FO3 unit = 1.5 cm in Godot)
