using Godot;
using System.Collections.Generic;
using System.IO;

namespace OpenFo3.NIF
{
    /// <summary>
    /// Holds pre-parsed geometry data from a NIF file, safe to create on worker threads.
    /// Call BuildArrayMesh() on the main thread to create the Godot ArrayMesh.
    /// </summary>
    public class NIFGeometryData
    {
        public List<(Vector3[] Vertices, int[] Indices, Transform3D Transform)> Surfaces = new();
    }

    public static class NIFMeshBuilder
    {
        /// <summary>
        /// Parse NIF hierarchy and extract geometry data. Thread-safe (no Godot API calls).
        /// </summary>
        public static NIFGeometryData ExtractGeometry(NIFReader nif)
        {
            var geom = new NIFGeometryData();
            if (nif.Blocks.Count == 0) return geom;

            foreach (int rootIdx in nif.RootBlockIndices)
            {
                TraverseExtract(nif, rootIdx, Transform3D.Identity, geom);
            }

            return geom;
        }

        /// <summary>
        /// Build an ArrayMesh from pre-extracted geometry. MUST be called on main thread.
        /// </summary>
        public static ArrayMesh BuildArrayMesh(NIFGeometryData geom)
        {
            var mesh = new ArrayMesh();
            float worldScale = 0.015f;

            foreach (var surface in geom.Surfaces)
            {
                if (surface.Vertices == null || surface.Indices == null || surface.Indices.Length < 3)
                    continue;

                Vector3[] godotVertices = new Vector3[surface.Vertices.Length];
                for (int i = 0; i < surface.Vertices.Length; i++)
                {
                    var v = surface.Transform * surface.Vertices[i];
                    v *= worldScale;
                    godotVertices[i] = new Vector3(v.X, v.Z, -v.Y);
                }

                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = godotVertices;
                arrays[(int)Mesh.ArrayType.Index] = surface.Indices;

                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            }

            return mesh;
        }

        /// <summary>
        /// Convenience: parse and build on main thread (for single-threaded usage).
        /// </summary>
        public static ArrayMesh Build(NIFReader nif)
        {
            var geom = ExtractGeometry(nif);
            return BuildArrayMesh(geom);
        }

        private static void TraverseExtract(NIFReader nif, int blockIdx, Transform3D parentTransform, NIFGeometryData geom)
        {
            if (blockIdx < 0 || blockIdx >= nif.Blocks.Count) return;
            var block = nif.Blocks[blockIdx];

            var node = NIFBlockResolver.Resolve(block);
            if (node == null) return;

            // Local transform
            var localTransform = new Transform3D(node.Rotation, node.Translation);
            localTransform.Basis = localTransform.Basis.Scaled(new Vector3(node.Scale, node.Scale, node.Scale));
            var globalTransform = parentTransform * localTransform;

            if (node.DataIndex != -1)
            {
                if (node.DataIndex >= 0 && node.DataIndex < nif.Blocks.Count)
                {
                    var dataBlock = nif.Blocks[node.DataIndex];
                    Vector3[] verts = null;
                    int[] inds = null;

                    if (dataBlock.Type == "NiTriStripsData")
                    {
                        (verts, inds) = NiTriStripsDataParser.Parse(dataBlock.Data);
                    }
                    else if (dataBlock.Type == "NiTriShapeData")
                    {
                        (verts, inds) = NiTriShapeDataParser.Parse(dataBlock.Data);
                    }

                    if (verts != null && inds != null && inds.Length >= 3)
                    {
                        geom.Surfaces.Add((verts, inds, globalTransform));
                    }
                }
            }

            foreach (int childIdx in node.Children)
            {
                TraverseExtract(nif, childIdx, globalTransform, geom);
            }
        }
    }
}
