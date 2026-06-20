using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenFo3.NIF
{
    public static class NIFCollisionBuilder
    {
        private const float WorldScale = 0.015f;

        // Havok Motion Type → Godot body type
        private enum HavokMotionType : byte
        {
            MO_SYS_INVALID = 0,
            MO_SYS_DYNAMIC = 1,
            MO_SYS_SPHERE_INERTIA = 2,
            MO_SYS_SPHERE_STABILIZED = 3,
            MO_SYS_BOX_INERTIA = 4,
            MO_SYS_BOX_STABILIZED = 5,
            MO_SYS_KEYFRAMED = 6,
            MO_SYS_FIXED = 7,
            MO_SYS_THIN_BOX = 8,
            MO_SYS_CHARACTER = 9,
        }

        private struct RigidBodyInfo
        {
            public float Mass;
            public float LinearDamping;
            public float AngularDamping;
            public HavokMotionType MotionType;
        }

        /// <summary>
        /// Parse bhkRigidBody CInfo fields from FO3 binary data.
        /// Layout (FO3): ShapeRef(4) + UnknownInt(4) + HavokFilter(4) + WorldObjCInfo(8) + EntityCInfo(8)
        ///   + CInfo550_660: Unused01(4) + HavokFilter(4) + Unused02(4) + CollisionResponse(1) + Unused03(1)
        ///   + ProcessContact(2) + Unused04(4) + Translation(16) + Rotation(16) + LinearVelocity(16)
        ///   + AngularVelocity(16) + InertiaTensor(36) + Center(16) + Mass(4) + LinearDamping(4)
        ///   + AngularDamping(4) + Friction(4) + Restitution(4) + MaxLinearVelocity(4) + MaxAngularVelocity(4)
        ///   + PenetrationDepth(4) + MotionSystem(1) + DeactivatorType(1) + SolverDeactivation(1) + QualityType(1) + Unused05(12)
        /// </summary>
        private static RigidBodyInfo ParseRigidBodyInfo(byte[] data)
        {
            // Before CInfo: Shape(4) + Unknown(4) + Filter(4) + WorldObj(8) + Entity(8) = 28 bytes
            const int preCInfoSize = 28;

            if (data.Length < preCInfoSize + 137) // need at least up to MotionSystem
                return new RigidBodyInfo { MotionType = HavokMotionType.MO_SYS_FIXED, Mass = 0 };

            // CInfo starts at offset 28
            // Mass at CInfo[136]
            float mass = BitConverter.ToSingle(data, preCInfoSize + 136);
            float linearDamping = BitConverter.ToSingle(data, preCInfoSize + 140);
            float angularDamping = BitConverter.ToSingle(data, preCInfoSize + 144);

            byte motionType = data[preCInfoSize + 168];

            return new RigidBodyInfo
            {
                Mass = mass,
                LinearDamping = linearDamping,
                AngularDamping = angularDamping,
                MotionType = (HavokMotionType)motionType,
            };
        }

        // Havok material -> (Friction, Restitution) mapping based on FO3 game data
        private static readonly (float Friction, float Restitution)[] HavokMaterialMap = new (float, float)[]
        {
            (0.70f, 0.15f), // 0: Stone
            (0.50f, 0.10f), // 1: Cloth
            (0.60f, 0.20f), // 2: Dirt
            (0.60f, 0.05f), // 3: Glass
            (0.50f, 0.25f), // 4: Grass
            (0.65f, 0.20f), // 5: Metal
            (0.55f, 0.15f), // 6: Organic
            (0.70f, 0.10f), // 7: Skin
            (0.20f, 0.05f), // 8: Water
            (0.60f, 0.20f), // 9: Wood
            (0.75f, 0.10f), // 10: Heavy Stone
            (0.70f, 0.15f), // 11: Heavy Metal
            (0.65f, 0.15f), // 12: Heavy Wood
            (0.55f, 0.25f), // 13: Chain
            (0.65f, 0.10f), // 14: Snow
            (0.80f, 0.00f), // 15: Elevator
            (0.60f, 0.15f), // 16: Hollow Metal
            (0.55f, 0.20f), // 17: Sheet Metal
            (0.70f, 0.10f), // 18: Sand
            (0.65f, 0.12f), // 19: Broken Concrete
            (0.72f, 0.18f), // 20: Iron
        };

        private static void ApplyHavokMaterial(uint material, Node3D body)
        {
            // Havok material mapping not supported in this Redot version
            // PhysicsMaterialOverride is not available on PhysicsBody3D
        }

        public struct CollisionResult
        {
            public List<CollisionShape3D> Shapes;
            public Node3D Body;
        }

        public static CollisionResult? BuildCollision(NIFReader nif, Node3D parent)
        {
            // Find bhkCollisionObject blocks
            foreach (int rootIdx in nif.RootBlockIndices)
            {
                if (rootIdx < 0 || rootIdx >= nif.Blocks.Count) continue;
                var rootBlock = nif.Blocks[rootIdx];

                var result = TraverseCollision(nif, rootBlock, parent, skipAdd: parent == null);
                if (result.HasValue)
                {
                    if (parent != null && result.Value.Body != null)
                        parent.AddChild(result.Value.Body);
                    return result;
                }
            }

            return null;
        }

        private static CollisionResult? TraverseCollision(NIFReader nif, NIFBlock block, Node3D parent, bool skipAdd = false)
        {
            var node = NIFBlockResolver.Resolve(block, nif);
            if (node == null) return null;

            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                br.ReadUInt32(); // Name index
                uint numExtra = br.ReadUInt32();
                for (int i = 0; i < numExtra; i++) br.ReadInt32();
                br.ReadInt32(); // Controller

                br.ReadUInt32(); // Flags
                br.ReadBytes(12); // Translation (3 floats)
                br.ReadBytes(36); // Rotation (9 floats)
                br.ReadSingle(); // Scale
                uint numProps = br.ReadUInt32();
                br.ReadBytes((int)numProps * 4); // Property refs
                int collisionRef = br.ReadInt32();

                if (collisionRef >= 0 && collisionRef < nif.Blocks.Count)
                {
                    var collisionBlock = nif.Blocks[collisionRef];
                    return ParseCollisionObject(nif, collisionBlock, parent, skipAdd);
                }
            }
            catch { }

            foreach (int childIdx in node.Children)
            {
                if (childIdx < 0 || childIdx >= nif.Blocks.Count) continue;
                var result = TraverseCollision(nif, nif.Blocks[childIdx], parent, skipAdd);
                if (result.HasValue) return result;
            }

            return null;
        }

        private static CollisionResult? ParseCollisionObject(NIFReader nif, NIFBlock block, Node3D parent, bool skipAdd = false)
        {
            if (block.Type == "bhkCollisionObject" || block.Type == "bhkBlendCollisionObject")
            {
                return ParseBhkCollisionObject(nif, block, parent, skipAdd);
            }

            return null;
        }

        private static CollisionResult? ParseBhkCollisionObject(NIFReader nif, NIFBlock block, Node3D parent, bool skipAdd = false)
        {
            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                ushort flags = br.ReadUInt16();
                int bodyRef = br.ReadInt32();

                if (bodyRef >= 0 && bodyRef < nif.Blocks.Count)
                {
                    var bodyBlock = nif.Blocks[bodyRef];
                    return ParseRigidBody(nif, bodyBlock, parent, skipAdd);
                }
            }
            catch { }

            return null;
        }

        private static CollisionResult? ParseRigidBody(NIFReader nif, NIFBlock block, Node3D parent, bool skipAdd = false)
        {
            if (block.Type != "bhkRigidBody" && block.Type != "bhkRigidBodyT")
                return null;

            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                // bhkWorldObject: Shape Ref
                int shapeRef = br.ReadInt32();

                // Skip rest of bhkWorldObject, bhkEntity, bhkRigidBody fields
                // to find the translation for bhkRigidBodyT
                Vector3 bodyTranslation = Vector3.Zero;
                Basis bodyRotation = Basis.Identity;

                if (block.Type == "bhkRigidBodyT")
                {
                    // bhkRigidBodyT has Translation and Rotation embedded after CInfo
                    // Read to the right offset: skip the full rigid body info
                    ms.Seek(0, SeekOrigin.Begin);

                    // Skip shape ref + unknown int + havok filter + world obj info + entity info
                    br.ReadInt32(); // shape ref
                    br.ReadInt32(); // unknown int
                    br.ReadUInt32(); // havok filter
                    br.ReadBytes(8); // world obj info (bhkWorldObjectCInfo)
                    br.ReadBytes(8); // entity info (bhkEntityCInfo)

                    // Skip the CInfo struct up to Translation
                    // The CInfo550_660 starts with the translation
                    // First skip: Unused01(4) + HavokFilter(4) + Unused02(4) + CollisionResponse(1+1?) + ProcessCallbacks(2) + Unused04(4) = ~20 bytes
                    br.ReadBytes(20);

                    float tx = br.ReadSingle();
                    float ty = br.ReadSingle();
                    float tz = br.ReadSingle();
                    float tw = br.ReadSingle(); // w component of Vector4
                    bodyTranslation = new Vector3(tx, tz, -ty);

                    // Read quaternion rotation
                    float qx = br.ReadSingle();
                    float qy = br.ReadSingle();
                    float qz = br.ReadSingle();
                    float qw = br.ReadSingle();
                    bodyRotation = new Basis(new Quaternion(qx, qz, -qy, qw));
                }

                if (shapeRef >= 0 && shapeRef < nif.Blocks.Count)
                {
                    // Determine motion type from rigid body info
                    var rbInfo = ParseRigidBodyInfo(block.Data);
                    bool isDynamic = rbInfo.MotionType is HavokMotionType.MO_SYS_DYNAMIC
                        or HavokMotionType.MO_SYS_SPHERE_INERTIA
                        or HavokMotionType.MO_SYS_SPHERE_STABILIZED
                        or HavokMotionType.MO_SYS_BOX_INERTIA
                        or HavokMotionType.MO_SYS_BOX_STABILIZED
                        or HavokMotionType.MO_SYS_THIN_BOX;
                    bool isCharacter = rbInfo.MotionType == HavokMotionType.MO_SYS_CHARACTER;

                    PhysicsBody3D body;
                    if (isCharacter)
                    {
                        var cb = new CharacterBody3D();
                        cb.Name = $"Character_{block.Type}";
                        body = cb;
                    }
                    else if (isDynamic)
                    {
                        var rb = new RigidBody3D();
                        rb.Name = $"RigidBody_{block.Type}";
                        rb.Mass = Mathf.Max(rbInfo.Mass, 0.001f);
                        rb.GravityScale = 1.0f;
                        rb.LinearDamp = rbInfo.LinearDamping;
                        rb.AngularDamp = rbInfo.AngularDamping;
                        body = rb;
                    }
                    else
                    {
                        var sb = new StaticBody3D();
                        sb.Name = $"Collision_{block.Type}";
                        body = sb;
                    }

                    if (skipAdd)
                    {
                        body.Position = bodyTranslation * WorldScale;
                        body.Basis = bodyRotation;
                    }

                    if (!skipAdd && parent != null)
                    {
                        body.Position = bodyTranslation * WorldScale;
                        body.Basis = bodyRotation;
                        parent.AddChild(body);
                    }

                    var result = new CollisionResult
                    {
                        Body = body,
                        Shapes = new List<CollisionShape3D>(),
                    };

                    BuildShape(nif, nif.Blocks[shapeRef], body, result.Shapes);
                    return result;
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[NIFCollisionBuilder] Error parsing {block.Type}: {e.Message}");
            }

            return null;
        }

        private static void BuildShape(NIFReader nif, NIFBlock block, Node3D body, List<CollisionShape3D> shapes)
        {
            try
            {
                switch (block.Type)
                {
                    case "bhkBoxShape":
                        BuildBoxShape(block.Data, body, shapes);
                        break;
                    case "bhkSphereShape":
                        BuildSphereShape(block.Data, body, shapes);
                        break;
                    case "bhkCapsuleShape":
                        BuildCapsuleShape(block.Data, body, shapes);
                        break;
                    case "bhkMoppBvTreeShape":
                        BuildMoppShape(nif, block, body, shapes);
                        break;
                    case "bhkPackedNiTriStripsShape":
                        BuildPackedTriStripsShape(nif, block, body, shapes);
                        break;
                    case "bhkListShape":
                        BuildListShape(nif, block, body, shapes);
                        break;
                    case "bhkConvexVerticesShape":
                        BuildConvexVerticesShape(block.Data, body, shapes);
                        break;
                    case "bhkNiTriStripsShape":
                        BuildNiTriStripsShape(nif, block, body, shapes);
                        break;
                    case "bhkTransformShape":
                        BuildTransformShape(nif, block, body, shapes);
                        break;
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[NIFCollisionBuilder] Error building {block.Type}: {e.Message}");
            }
        }

        private static void BuildBoxShape(byte[] data, Node3D body, List<CollisionShape3D> shapes)
        {
            using var br = new BinaryReader(new MemoryStream(data));
            uint material = br.ReadUInt32();
            float radius = br.ReadSingle();
            br.ReadBytes(4); // unused
            ApplyHavokMaterial(material, body);

            float hx = br.ReadSingle();
            float hy = br.ReadSingle();
            float hz = br.ReadSingle();

            var shape = new CollisionShape3D();
            var box = new BoxShape3D();
            box.Size = new Vector3(hx * 2 * WorldScale, hz * 2 * WorldScale, hy * 2 * WorldScale);
            shape.Shape = box;
            body.AddChild(shape);
            shapes.Add(shape);
        }

        private static void BuildSphereShape(byte[] data, Node3D body, List<CollisionShape3D> shapes)
        {
            using var br = new BinaryReader(new MemoryStream(data));
            uint material = br.ReadUInt32();
            float radius = br.ReadSingle();
            ApplyHavokMaterial(material, body);

            var shape = new CollisionShape3D();
            var sphere = new SphereShape3D();
            sphere.Radius = Mathf.Max(radius * WorldScale, 0.001f);
            shape.Shape = sphere;
            body.AddChild(shape);
            shapes.Add(shape);
        }

        private static void BuildCapsuleShape(byte[] data, Node3D body, List<CollisionShape3D> shapes)
        {
            using var br = new BinaryReader(new MemoryStream(data));
            uint material = br.ReadUInt32();
            float shapeRadius = br.ReadSingle();
            br.ReadBytes(4); // unused
            ApplyHavokMaterial(material, body);

            float x1 = br.ReadSingle();
            float y1 = br.ReadSingle();
            float z1 = br.ReadSingle();
            float r1 = br.ReadSingle();
            float x2 = br.ReadSingle();
            float y2 = br.ReadSingle();
            float z2 = br.ReadSingle();
            float r2 = br.ReadSingle();

            float midY = (y1 + y2) / 2f;
            float height = Mathf.Abs(y2 - y1) * WorldScale;
            float capsuleRadius = Mathf.Max(r1, r2) * WorldScale;

            var shape = new CollisionShape3D();
            var capsule = new CapsuleShape3D();
            capsule.Height = Mathf.Max(height, 0.001f);
            capsule.Radius = Mathf.Max(capsuleRadius, 0.001f);

            shape.Shape = capsule;
            shape.Position = new Vector3(x1 * WorldScale, z1 * WorldScale, -midY * WorldScale);
            body.AddChild(shape);
            shapes.Add(shape);
        }

        private static void BuildConvexVerticesShape(byte[] data, Node3D body, List<CollisionShape3D> shapes)
        {
            try
            {
                using var br = new BinaryReader(new MemoryStream(data));

                uint material = br.ReadUInt32();
                float radius = br.ReadSingle();
                ApplyHavokMaterial(material, body);

                // bhkWorldObjCInfoProperty for vertices (12 bytes: Data + Size + CapacityAndFlags)
                br.ReadBytes(12);
                // bhkWorldObjCInfoProperty for normals (12 bytes)
                br.ReadBytes(12);

                uint numVerts = br.ReadUInt32();
                if (numVerts == 0 || numVerts > 10000) return;

                Vector3[] verts = new Vector3[numVerts];
                for (int i = 0; i < numVerts; i++)
                {
                    float x = br.ReadSingle();
                    float y = br.ReadSingle();
                    float z = br.ReadSingle();
                    br.ReadSingle(); // w = 0 (Vector4 padding)
                    verts[i] = new Vector3(x * WorldScale, z * WorldScale, -y * WorldScale);
                }

                var shape = new CollisionShape3D();
                var hull = new ConvexPolygonShape3D();
                hull.SetPoints(verts);
                shape.Shape = hull;
                body.AddChild(shape);
                shapes.Add(shape);
            }
            catch { }
        }

        private static void BuildMoppShape(NIFReader nif, NIFBlock block, Node3D body, List<CollisionShape3D> shapes)
        {
            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                br.ReadInt32(); // Shape ref
                br.ReadBytes(12); // unused
                float scale = br.ReadSingle();

                // Skip MOPP code data (we use the shape ref instead)
                // Seek back and read shape ref properly
                ms.Seek(0, SeekOrigin.Begin);
                int shapeRef = br.ReadInt32();

                if (shapeRef >= 0 && shapeRef < nif.Blocks.Count)
                {
                    BuildShape(nif, nif.Blocks[shapeRef], body, shapes);
                }
            }
            catch { }
        }

        private static void BuildPackedTriStripsShape(NIFReader nif, NIFBlock block, Node3D body, List<CollisionShape3D> shapes)
        {
            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                if (block.Data.Length < 10) return;
                ushort numSubShapes = br.ReadUInt16();

                // Skip to the Data ref
                ms.Seek(0, SeekOrigin.Begin);
                br.ReadBytes(4); // Skip numSubShapes + 2 bytes
                // Actually, read the full structure:
                ms.Seek(0, SeekOrigin.Begin);

                numSubShapes = br.ReadUInt16();
                // Skip subshapes: each is 12 bytes (HavokFilter(4) + NumVerts(4) + Material(4))
                br.ReadBytes(numSubShapes * 12);
                br.ReadUInt32(); // User Data
                br.ReadBytes(4); // Unused
                br.ReadSingle(); // Radius
                br.ReadBytes(4); // Unused
                br.ReadBytes(16); // Scale (Vector4)
                br.ReadSingle(); // Radius Copy
                br.ReadBytes(16); // Scale Copy (Vector4)
                int dataRef = br.ReadInt32();

                if (dataRef >= 0 && dataRef < nif.Blocks.Count)
                {
                    var dataBlock = nif.Blocks[dataRef];
                    if (dataBlock.Type == "hkPackedNiTriStripsData")
                    {
                        BuildPackedTriStripsData(dataBlock.Data, body, shapes);
                    }
                }
            }
            catch { }
        }

        private static void BuildPackedTriStripsData(byte[] data, Node3D body, List<CollisionShape3D> shapes)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                uint numTriangles = br.ReadUInt32();

                // Skip triangle data: each triangle is 16 bytes (3 uint16 indices + uint16 welding + 3 float normal)
                br.ReadBytes((int)numTriangles * 16);

                uint numVertices = br.ReadUInt32();
                bool compressed = br.ReadByte() != 0;

                Vector3[] verts = new Vector3[numVertices];
                if (compressed)
                {
                    for (int i = 0; i < numVertices; i++)
                    {
                        ushort hx = br.ReadUInt16();
                        ushort hy = br.ReadUInt16();
                        ushort hz = br.ReadUInt16();
                        // Half-float to float conversion
                        verts[i] = new Vector3(
                            HalfToFloat(hx) * WorldScale,
                            HalfToFloat(hz) * WorldScale,
                            -HalfToFloat(hy) * WorldScale);
                        br.ReadUInt16(); // w = 0
                    }
                }
                else
                {
                    for (int i = 0; i < numVertices; i++)
                    {
                        float vx = br.ReadSingle();
                        float vy = br.ReadSingle();
                        float vz = br.ReadSingle();
                        verts[i] = new Vector3(vx * WorldScale, vz * WorldScale, -vy * WorldScale);
                    }
                }

                // Re-read triangle indices
                ms.Seek(4, SeekOrigin.Begin); // Back to start after numTriangles
                int[] indices = new int[numTriangles * 3];
                for (int i = 0; i < numTriangles; i++)
                {
                    ushort i0 = br.ReadUInt16();
                    ushort i1 = br.ReadUInt16();
                    ushort i2 = br.ReadUInt16();
                    br.ReadUInt16(); // welding info
                    br.ReadBytes(12); // normal
                    indices[i * 3] = i0;
                    indices[i * 3 + 1] = i2; // flip winding
                    indices[i * 3 + 2] = i1;
                }

                var shape = new CollisionShape3D();
                var trimesh = new ConcavePolygonShape3D();
                Vector3[] faces = new Vector3[indices.Length];
                for (int i = 0; i < indices.Length; i++)
                {
                    faces[i] = verts[indices[i]];
                }
                trimesh.SetFaces(faces);
                shape.Shape = trimesh;
                body.AddChild(shape);
                shapes.Add(shape);
            }
            catch { }
        }

        private static void BuildListShape(NIFReader nif, NIFBlock block, Node3D body, List<CollisionShape3D> shapes)
        {
            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                uint numSubShapes = br.ReadUInt32();
                int[] shapeRefs = new int[numSubShapes];
                for (int i = 0; i < numSubShapes; i++)
                    shapeRefs[i] = br.ReadInt32();

                foreach (int refIdx in shapeRefs)
                {
                    if (refIdx >= 0 && refIdx < nif.Blocks.Count)
                        BuildShape(nif, nif.Blocks[refIdx], body, shapes);
                }
            }
            catch { }
        }

        private static void BuildNiTriStripsShape(NIFReader nif, NIFBlock block, Node3D body, List<CollisionShape3D> shapes)
        {
            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                uint material = br.ReadUInt32();
                br.ReadSingle(); // Radius
                ApplyHavokMaterial(material, body);
                br.ReadBytes(20); // Unused
                br.ReadUInt32(); // Grow By
                br.ReadBytes(16); // Scale (Vector4)

                uint numStripsData = br.ReadUInt32();
                int[] stripDataRefs = new int[numStripsData];
                for (int i = 0; i < numStripsData; i++)
                    stripDataRefs[i] = br.ReadInt32();

                var allVerts = new List<Vector3>();
                var allIndices = new List<int>();

                foreach (int refIdx in stripDataRefs)
                {
                    if (refIdx < 0 || refIdx >= nif.Blocks.Count) continue;
                    var dataBlock = nif.Blocks[refIdx];
                    if (dataBlock.Type != "NiTriStripsData") continue;

                    (Vector3[] verts, _, int[] inds, _) = NiTriStripsDataParser.Parse(dataBlock.Data);
                    if (verts == null || verts.Length == 0 || inds == null || inds.Length < 3) continue;

                    int baseIdx = allVerts.Count;
                    foreach (var v in verts)
                        allVerts.Add(new Vector3(v.X * WorldScale, v.Z * WorldScale, -v.Y * WorldScale));
                    foreach (int idx in inds)
                        allIndices.Add(baseIdx + idx);
                }

                if (allVerts.Count < 3 || allIndices.Count < 3) return;

                var shape = new CollisionShape3D();
                var trimesh = new ConcavePolygonShape3D();
                Vector3[] faces = new Vector3[allIndices.Count];
                for (int i = 0; i < allIndices.Count; i++)
                    faces[i] = allVerts[allIndices[i]];
                trimesh.SetFaces(faces);
                shape.Shape = trimesh;
                body.AddChild(shape);
                shapes.Add(shape);
            }
            catch { }
        }

        private static void BuildTransformShape(NIFReader nif, NIFBlock block, Node3D body, List<CollisionShape3D> shapes)
        {
            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                uint material = br.ReadUInt32();
                br.ReadSingle(); // Radius
                br.ReadBytes(8); // Unused
                ApplyHavokMaterial(material, body);

                // Transform: 4x3 matrix (12 floats) - column major
                float[] mat = new float[12];
                for (int i = 0; i < 12; i++) mat[i] = br.ReadSingle();

                int shapeRef = br.ReadInt32();

                if (shapeRef >= 0 && shapeRef < nif.Blocks.Count)
                {
                    // Apply transform to the child shape
                    var childBody = new StaticBody3D();
                    childBody.Name = "TransformChild";
                    childBody.Position = new Vector3(mat[3], mat[7], mat[11]) * WorldScale;
                    body.AddChild(childBody);
                    BuildShape(nif, nif.Blocks[shapeRef], childBody, shapes);
                }
            }
            catch { }
        }

        private static float HalfToFloat(ushort h)
        {
            int sign = (h >> 15) & 1;
            int exp = (h >> 10) & 31;
            int mant = h & 1023;

            if (exp == 0)
            {
                // Denormalized
                if (mant == 0) return 0f;
                exp = -14;
                while ((mant & 1024) == 0) { mant <<= 1; exp--; }
                mant &= 1023;
                exp += 25;
            }
            else if (exp == 31)
            {
                return mant == 0 ? (sign == 0 ? float.PositiveInfinity : float.NegativeInfinity) : float.NaN;
            }
            else
            {
                exp += 15; // bias = 15 for half, 127 for float, so add 112
                exp += 112;
            }

            uint f = (uint)((sign << 31) | (exp << 23) | (mant << 13));
            return BitConverter.ToSingle(BitConverter.GetBytes(f), 0);
        }
    }
}
