using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using OpenFo3.ESM;
using OpenFo3.NIF;

public partial class Megaton : Node3D
{
	private ConcurrentDictionary<string, ArrayMesh> _meshCache = new();
	private ConcurrentDictionary<string, NIFReader> _nifCache = new();
	private OpenFo3.BSA.BSAReader _bsa;
	private ESMReader _esm;

	private Dictionary<uint, RecordEntry> _masterFormIDIndex;
	private Dictionary<uint, RecordEntry> _refrFormIDIndex;
	private List<OpenFo3.BSA.BSAFile> _bsaFiles;

	private const int MaxObjectsToLoad = 5000;
	private const float CellSize = 8192.0f;
	private const float WorldScale = 0.015f;

	private ConcurrentQueue<InstanceRequest> _instantiateQueue = new();
	private Vector2 _megatonCenter = new Vector2(-14200f, -3800f);
	private float _loadRadius = 120000f;

	private struct InstanceRequest
	{
		public string Path;
		public Vector3 Position;
		public Vector3 Rotation;
	}

	public override void _Process(double delta)
	{
		// Drain the instantiate queue (max N per frame to avoid stalls)
		const int MaxPerFrame = 200;
		for (int i = 0; i < MaxPerFrame && _instantiateQueue.TryDequeue(out var req); i++)
		{
			CreateAndAddInstance(req);
		}
	}

	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		try
		{
			string bsaPath = Path.Combine(GamePaths.DataPath, "Fallout - Meshes.bsa");
			_bsa = new OpenFo3.BSA.BSAReader(bsaPath);
			_bsaFiles = _bsa.ExtractFileList();

			_esm = new ESMReader(GamePaths.EsmPath);
			_masterFormIDIndex = _esm.BuildFormIdIndex(new[]
			{
				"STAT","DOOR","FURN","ACTI","MSTT","LIGH","TERM",
				"CONT","MISC","WEAP","ARMO","CLOT","TREE","ALCH","INGR","BOOK"
			});

			_refrFormIDIndex = _esm.BuildFormIdIndex(new[] { "REFR" });

			// 1. Find Megaton Worldspace
			uint megatonWorldId = 0;
			var wrldIndex = _esm.BuildFormIdIndex(new[] { "WRLD" });
			foreach (var kvp in wrldIndex)
			{
				var rec = _esm.GetRecordAtOffset(kvp.Value.Offset);
				var subs = _esm.GetSubRecords(rec);
				var edid = subs.FirstOrDefault(s => s.Type == "EDID");
				if (edid != null)
				{
					string name = Encoding.ASCII.GetString(edid.Data).TrimEnd('\0');
					if (name == "MegatonWorld")
					{
						megatonWorldId = rec.FormId;
						GD.Print($"[Megaton] Found MegatonWorld: 0x{megatonWorldId:X8}");
						break;
					}
				}
			}

			if (megatonWorldId == 0) GD.PrintErr("[Megaton] Could not find MegatonWorld worldspace!");

			// Start background loading
			_ = Task.Run(() => LoadWorldAsync(megatonWorldId));
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Init error: {e.Message}");
		}
	}

	private async Task LoadWorldAsync(uint targetWorldId)
	{
		// 1. Group by Chunks
		var chunks = new Dictionary<Vector2I, List<long>>();
		int totalRefrs = 0;
		foreach (var kvp in _refrFormIDIndex)
		{
			var entry = kvp.Value;

			if (entry.WorldFormId != targetWorldId) continue;

			var offset = entry.Offset;
			ESMRecord record;
			List<SubRecord> subs;

			lock (_esm)
			{
				record = _esm.GetRecordAtOffset(offset);
				subs = _esm.GetSubRecords(record);
			}

			var dataSub = subs.FirstOrDefault(s => s.Type == "DATA");
			if (dataSub == null) continue;

			float px = BitConverter.ToSingle(dataSub.Data, 0);
			float py = BitConverter.ToSingle(dataSub.Data, 4);

			var chunkCoords = Vector2I.Zero;
			if (!chunks.ContainsKey(chunkCoords)) chunks[chunkCoords] = new List<long>();
			chunks[chunkCoords].Add(offset);

			totalRefrs++;
		}

		GD.Print($"[Megaton] Found {totalRefrs} REFRs in Megaton hierarchy.");

		// 2. Process Chunks (Parallel NIF Parsing — no Godot API calls)
		await Task.Run(() =>
		{
			Parallel.ForEach(chunks, chunkKvp =>
			{
				foreach (var offset in chunkKvp.Value)
				{
					ProcessRecord(offset);
				}
			});
		});

		GD.Print($"[Megaton] Parsing done. Queue: {_instantiateQueue.Count}, NIFCache: {_nifCache.Count}, MeshCache: {_meshCache.Count}");
		// Give _Process time to drain the queue and build meshes
		await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
		GD.Print($"[Megaton] Scene children: {GetChildCount()} (after 3s drain)");

		var positions = new List<Vector3>();
		for (int i = 0; i < GetChildCount(); i++)
			if (GetChild(i) is Node3D n3d) positions.Add(n3d.GlobalPosition);
		if (positions.Count > 0)
		{
			var minP = positions[0]; var maxP = positions[0];
			foreach (var p in positions)
			{
				minP = new Vector3(Math.Min(minP.X, p.X), Math.Min(minP.Y, p.Y), Math.Min(minP.Z, p.Z));
				maxP = new Vector3(Math.Max(maxP.X, p.X), Math.Max(maxP.Y, p.Y), Math.Max(maxP.Z, p.Z));
			}
			GD.Print($"[Megaton] Position range: {minP} to {maxP}");
		}
	}

	private void ProcessRecord(long offset)
	{
		try
		{
			ESMRecord record;
			List<SubRecord> subs;

			lock (_esm)
			{
				record = _esm.GetRecordAtOffset(offset);
				subs = _esm.GetSubRecords(record);
			}

			var dataSub = subs.FirstOrDefault(s => s.Type == "DATA");
			var nameSub = subs.FirstOrDefault(s => s.Type == "NAME");
			if (dataSub == null || nameSub == null) return;

			uint formId = BitConverter.ToUInt32(nameSub.Data, 0);

			RecordEntry baseEntry;
			string nifPath;

			lock (_esm)
			{
				if (!_masterFormIDIndex.TryGetValue(formId, out baseEntry)) return;
				var baseRecord = _esm.GetRecordAtOffset(baseEntry.Offset);
				var baseSubs = _esm.GetSubRecords(baseRecord);
				var modl = baseSubs.FirstOrDefault(s => s.Type == "MODL");
				if (modl == null) return;
				nifPath = Encoding.ASCII.GetString(modl.Data).TrimEnd('\0').Replace('\\', '/');
			}

			if (!nifPath.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) nifPath = "meshes/" + nifPath;

			// Ensure NIF is parsed and cached (worker thread)
			EnsureNifParsed(nifPath);

			float px = BitConverter.ToSingle(dataSub.Data, 0);
			float py = BitConverter.ToSingle(dataSub.Data, 4);
			float pz = BitConverter.ToSingle(dataSub.Data, 8);
			float rx = BitConverter.ToSingle(dataSub.Data, 12);
			float ry = BitConverter.ToSingle(dataSub.Data, 16);
			float rz = BitConverter.ToSingle(dataSub.Data, 20);

			_instantiateQueue.Enqueue(new InstanceRequest
			{
				Path = nifPath,
				Position = new Vector3((px - _megatonCenter.X) * WorldScale, pz * WorldScale, -(py - _megatonCenter.Y) * WorldScale),
				Rotation = new Vector3(rx, ry, rz),
			});
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Error processing record at 0x{offset:X8}: {e.Message}");
		}
	}

	/// <summary>
	/// Parse NIF on worker thread and cache the NIFReader.
	/// No Godot API calls here (thread-safe).
	/// </summary>
	private void EnsureNifParsed(string path)
	{
		if (_nifCache.ContainsKey(path)) return;

		var file = _bsaFiles.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
		if (file == null) return;

		byte[] nifData = _bsa.ReadFileData(file);
		if (nifData == null) return;

		var nif = new NIFReader();
		nif.Parse(nifData);

		if (nif.Blocks.Count > 0)
		{
			_nifCache.TryAdd(path, nif);
		}
	}

	private void CreateAndAddInstance(InstanceRequest req)
	{
		// Build or retrieve mesh on main thread
		var mesh = GetOrBuildMesh(req.Path);
		if (mesh == null) return;

		var inst = new MeshInstance3D { Mesh = mesh };

		// FO3 stores rotations in RADIANS (not degrees)
		var basis = Basis.Identity;
		basis = basis.Rotated(Vector3.Up, req.Rotation.Z);
		basis = basis.Rotated(Vector3.Right, req.Rotation.X);
		basis = basis.Rotated(Vector3.Back, req.Rotation.Y);
		inst.Transform = new Transform3D(basis, req.Position);

		AddChild(inst);
	}

	/// <summary>
	/// Build ArrayMesh from cached NIFReader on the main thread.
	/// Thread-safe: only called from _Process → CreateAndAddInstance.
	/// </summary>
	private ArrayMesh GetOrBuildMesh(string path)
	{
		if (_meshCache.TryGetValue(path, out var cached)) return cached;

		if (!_nifCache.TryGetValue(path, out var nif)) return null;

		// Extract geometry (pure data, no Godot API — safe but not needed to be on worker)
		var geom = NIFMeshBuilder.ExtractGeometry(nif);
		if (geom.Surfaces.Count == 0) return null;

		// Build ArrayMesh (MUST be on main thread)
		var mesh = NIFMeshBuilder.BuildArrayMesh(geom);
		if (mesh.GetSurfaceCount() > 0)
		{
			_meshCache.TryAdd(path, mesh);
			return mesh;
		}

		return null;
	}
}
