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
using OpenFo3.BSA;
using OpenFo3.World;

public partial class Megaton : Node3D
{
	private ConcurrentDictionary<string, ArrayMesh> _meshCache = new();
	private ConcurrentDictionary<string, NIFReader> _nifCache = new();
	private ConcurrentDictionary<string, Texture2D> _textureCache = new();

	private List<BSAReader> _bsaReaders = new();
	private BSAReader _texturesBsa;
	private ESMReader _esm;

	private Dictionary<uint, RecordEntry> _masterFormIDIndex;
	private Dictionary<uint, RecordEntry> _refrFormIDIndex;

	private List<(BSAReader BSA, List<BSAFile> Files)> _meshBsaList = new();
	private List<(BSAReader BSA, List<BSAFile> Files)> _textureBsaList = new();

	private const float WorldScale = 0.015f;

	private ConcurrentQueue<InstanceRequest> _instantiateQueue = new();
	private LightingLoader _lightingLoader;
	private TerrainBuilder _terrainBuilder;

	private struct InstanceRequest
	{
		public string Path;
		public Vector3 Position;
		public Vector3 Rotation;
		public uint FormId;
		public string BaseType;
		public float Scale;
		public uint WorldFormId;
	}

	private struct WorldData
	{
		public uint FormId;
		public string Name;
		public float DefaultLandHeight;
		public int NwCellX, NwCellY, SeCellX, SeCellY;
		public Vector2 Center;
		public bool HasMnam;
	}

	private Dictionary<string, WorldData> _worldDataByName = new();
	private Dictionary<uint, string> _worldNameById = new();
	private Dictionary<string, Node3D> _worldContainers = new();
	private Dictionary<string, bool> _worldLoading = new();
	private List<string> _worldNameList = new();
	private string _currentWorldName;
	private Label3D _worldLabel;
	private bool _initialized = false;

	private const float CellSize = 4096f;
	private const int MinTerrainHalf = 10;

	public override void _Process(double delta)
	{
		if (!_initialized) return;

		const int MaxPerFrame = 100;
		for (int i = 0; i < MaxPerFrame && _instantiateQueue.TryDequeue(out var req); i++)
		{
			CreateAndAddInstance(req);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (!_initialized) return;
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key9)
			{
				int idx = (int)(key.Keycode - Key.Key1);
				if (idx < _worldNameList.Count)
				{
					SwitchToWorld(_worldNameList[idx]);
				}
			}
		}
	}

	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		try
		{
			LoadBSAArchives();

			_esm = new ESMReader(GamePaths.EsmPath);
			_masterFormIDIndex = _esm.BuildFormIdIndex(new[]
			{
				"STAT", "DOOR", "FURN", "ACTI", "MSTT", "LIGH", "TERM",
				"CONT", "MISC", "WEAP", "ARMO", "CLOT", "TREE", "ALCH",
				"INGR", "BOOK", "GRAS", "LAND", "DEBR", "SCOL",
				"KEYM", "ARMA", "NOTE", "PWAT", "TACT", "AMMO"
			});

			_refrFormIDIndex = _esm.BuildFormIdIndex(new[] { "REFR" });

			_lightingLoader = new LightingLoader(_esm);
			_terrainBuilder = new TerrainBuilder(_esm);

			DiscoverWorlds();

			string targetWorld = GamePaths.GetTargetWorld();
			if (!_worldDataByName.ContainsKey(targetWorld))
			{
				GD.PrintErr($"[Megaton] Target world '{targetWorld}' not found! Falling back to first available world.");
				targetWorld = _worldNameList.Count > 0 ? _worldNameList[0] : null;
			}

			if (targetWorld != null)
			{
				CreateWorldLabel();
				_ = LoadWorldAsync(targetWorld);
			}

			_initialized = true;
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Init error: {e.Message}");
		}
	}

	private void CreateWorldLabel()
	{
		_worldLabel = new Label3D();
		_worldLabel.Name = "WorldLabel";
		_worldLabel.Position = new Vector3(0, 30, 0);
		_worldLabel.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		_worldLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_worldLabel.Modulate = new Color(1, 1, 0);
		_worldLabel.PixelSize = 0.02f;
		AddChild(_worldLabel);
	}

	private void LoadBSAArchives()
	{
		string[] bsaPaths = GamePaths.GetBSAFilePaths();
		foreach (string bsaPath in bsaPaths)
		{
			try
			{
				var bsa = new BSAReader(bsaPath);
				var files = bsa.ExtractFileList();
				string name = System.IO.Path.GetFileNameWithoutExtension(bsaPath);

				if (name.IndexOf("mesh", StringComparison.OrdinalIgnoreCase) >= 0 ||
					name.IndexOf("misc", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					_meshBsaList.Add((bsa, files));
				}
				if (name.IndexOf("texture", StringComparison.OrdinalIgnoreCase) >= 0 ||
					name.IndexOf("misc", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					_textureBsaList.Add((bsa, files));
				}

				_bsaReaders.Add(bsa);
				GD.Print($"[Megaton] Loaded BSA: {name} ({files.Count} files)");
			}
			catch (Exception e)
			{
				GD.PrintErr($"[Megaton] Failed to load BSA {bsaPath}: {e.Message}");
			}
		}

		_texturesBsa = _bsaReaders.FirstOrDefault(b =>
			System.IO.Path.GetFileNameWithoutExtension(b.FilePath)
				.IndexOf("texture", StringComparison.OrdinalIgnoreCase) >= 0);
	}

	private void DiscoverWorlds()
	{
		var wrldIndex = _esm.BuildFormIdIndex(new[] { "WRLD" });
		GD.Print($"[Megaton] Discovering worlds: {wrldIndex.Count} WRLD records found");

		foreach (var kvp in wrldIndex)
		{
			try
			{
				var rec = _esm.GetRecordAtOffset(kvp.Value.Offset);
				var subs = _esm.GetSubRecords(rec);
				var edid = subs.FirstOrDefault(s => s.Type == "EDID");
				if (edid == null) continue;

				string name = Encoding.ASCII.GetString(edid.Data).TrimEnd('\0');
				if (string.IsNullOrEmpty(name)) continue;

				var dnam = subs.FirstOrDefault(s => s.Type == "DNAM");
				float defaultLandHeight = 0;
				if (dnam != null && dnam.Data.Length >= 8)
				{
					defaultLandHeight = BitConverter.ToSingle(dnam.Data, 0);
				}

				int nwX = 0, nwY = 0, seX = 0, seY = 0;
				bool hasMnam = false;
				var mnam = subs.FirstOrDefault(s => s.Type == "MNAM");
				if (mnam != null && mnam.Data.Length >= 16)
				{
					nwX = BitConverter.ToInt16(mnam.Data, 8);
					nwY = BitConverter.ToInt16(mnam.Data, 10);
					seX = BitConverter.ToInt16(mnam.Data, 12);
					seY = BitConverter.ToInt16(mnam.Data, 14);
					hasMnam = true;
				}

				Vector2 center;
				if (hasMnam)
				{
					float cx = (nwX + seX) * CellSize / 2f;
					float cy = (nwY + seY) * CellSize / 2f;
					center = new Vector2(cx, cy);
				}
				else
				{
					center = GamePaths.GetWorldCenter();
				}

				// Expand MNAM bounds for terrain coverage
				int ctrX = (nwX + seX) / 2;
				int ctrY = (nwY + seY) / 2;
				int hx = Math.Max((seX - nwX) / 2, MinTerrainHalf) + 5;
				int hy = Math.Max((seY - nwY) / 2, MinTerrainHalf) + 5;

				var wd = new WorldData
				{
					FormId = rec.FormId,
					Name = name,
					DefaultLandHeight = defaultLandHeight,
					NwCellX = ctrX - hx,
					NwCellY = ctrY - hy,
					SeCellX = ctrX + hx,
					SeCellY = ctrY + hy,
					Center = center,
					HasMnam = hasMnam,
				};

				_worldDataByName[name] = wd;
				_worldNameById[rec.FormId] = name;
				_worldNameList.Add(name);

				GD.Print($"[Megaton]   World: {name} (0x{rec.FormId:X8})" +
					$" center=({center.X:F0},{center.Y:F0})" +
					$" landH={defaultLandHeight}" +
					$" cells=({wd.NwCellX},{wd.NwCellY})-({wd.SeCellX},{wd.SeCellY})");
			}
			catch (Exception e)
			{
				GD.PrintErr($"[Megaton] Error discovering WRLD at 0x{kvp.Value.Offset:X8}: {e.Message}");
			}
		}

		// Sort world list: put common worlds first for easy keyboard access
		_worldNameList = _worldNameList
			.OrderByDescending(n =>
			{
				if (n.Contains("Megaton")) return 100;
				if (n.Contains("Wasteland") || n.Contains("WasteLand")) return 90;
				if (n.Contains("DC") || n.Contains("DCTallGrass")) return 80;
				if (n.Contains("Interior")) return 70;
				return 0;
			})
			.ThenBy(n => n)
			.ToList();

		GD.Print($"[Megaton] Total worlds discovered: {_worldNameList.Count}");
	}

	private async Task LoadWorldAsync(string worldName)
	{
		if (_worldLoading.ContainsKey(worldName) && _worldLoading[worldName]) return;
		if (_worldContainers.ContainsKey(worldName))
		{
			ShowWorld(worldName);
			return;
		}

		if (!_worldDataByName.TryGetValue(worldName, out var wd))
		{
			GD.PrintErr($"[Megaton] Unknown world: {worldName}");
			return;
		}

		_worldLoading[worldName] = true;
		GD.Print($"[Megaton] Loading world: {worldName} (0x{wd.FormId:X8})...");

		var container = new Node3D();
		container.Name = $"World_{worldName}";
		AddChild(container);

		_worldContainers[worldName] = container;

		// Load terrain (synchronous, main thread)
		LoadTerrain(wd, container);

		// Load cell lighting (synchronous, main thread)
		LoadCellLighting(wd, container);

		// Load REFRs on background thread
		await Task.Run(() => LoadWorldRefrs(wd));

		ShowWorld(worldName);
		_worldLoading[worldName] = false;
	}

	private void LoadTerrain(WorldData wd, Node3D container)
	{
		try
		{
			var tiles = _terrainBuilder.BuildTerrainForWorld(wd.FormId, wd.Center, LoadTexture,
				wd.DefaultLandHeight, wd.NwCellX, wd.NwCellY, wd.SeCellX, wd.SeCellY);
			GD.Print($"[Megaton] World '{wd.Name}': loaded {tiles.Count} terrain tiles.");

			foreach (var tile in tiles)
			{
				var name = $"Terrain_{tile.CellCoord.X}_{tile.CellCoord.Y}";

				var inst = new MeshInstance3D();
				inst.Mesh = tile.Mesh;
				inst.Name = name;
				container.AddChild(inst);

				if (tile.CollisionShape != null)
				{
					var body = new StaticBody3D();
					body.Name = $"{name}_Collision";
					var colShape = new CollisionShape3D();
					colShape.Shape = tile.CollisionShape;
					body.AddChild(colShape);
					container.AddChild(body);
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Terrain load error for '{wd.Name}': {e.Message}");
		}
	}

	private void LoadCellLighting(WorldData wd, Node3D container)
	{
		try
		{
			var cellIndex = _esm.BuildFormIdIndex(new[] { "CELL" });
			foreach (var kvp in cellIndex)
			{
				if (kvp.Value.WorldFormId != wd.FormId) continue;

				var lighting = _lightingLoader.GetCellLighting(kvp.Key);
				if (lighting != null)
				{
					GD.Print($"[Megaton] Found lighting for CELL 0x{kvp.Key:X8} in '{wd.Name}'");
					LightingLoader.ApplyCellLighting(container, lighting);
					break;
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Lighting load error for '{wd.Name}': {e.Message}");
		}
	}

	private void LoadWorldRefrs(WorldData wd)
	{
		var refrsToProcess = new List<long>();
		foreach (var kvp in _refrFormIDIndex)
		{
			if (kvp.Value.WorldFormId == wd.FormId)
				refrsToProcess.Add(kvp.Value.Offset);
		}

		GD.Print($"[Megaton] World '{wd.Name}': {refrsToProcess.Count} REFRs found.");

		Parallel.ForEach(refrsToProcess, offset =>
		{
			ProcessRecord(offset, wd);
		});

		GD.Print($"[Megaton] World '{wd.Name}': parsing done. Queue: {_instantiateQueue.Count}");
	}

	private void ProcessRecord(long offset, WorldData wd)
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
			string nifPath = null;
			string baseType = null;
			List<SubRecord> baseSubs = null;

			lock (_esm)
			{
				if (!_masterFormIDIndex.TryGetValue(formId, out baseEntry)) return;
				var baseRecord = _esm.GetRecordAtOffset(baseEntry.Offset);
				baseType = baseRecord.Type;
				baseSubs = _esm.GetSubRecords(baseRecord);
				var modl = baseSubs.FirstOrDefault(s => s.Type == "MODL");
				if (modl != null)
				{
					nifPath = Encoding.ASCII.GetString(modl.Data).TrimEnd('\0').Replace('\\', '/');
				}
			}

			if (nifPath == null && baseType != "LIGH") return;

			if (nifPath != null)
			{
				if (!nifPath.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase))
					nifPath = "meshes/" + nifPath;

				string nifLower = nifPath.ToLowerInvariant();
				string fname = System.IO.Path.GetFileName(nifLower);
				bool isDebug =
					nifLower.StartsWith("meshes/marker") ||
					nifLower.Contains("editormarker") ||
					fname.Contains("shadow") ||
					fname.StartsWith("cone") ||
					(nifLower.Contains("/effects/") && fname.StartsWith("fxlight"));

				if (isDebug) return;

				EnsureNifParsed(nifPath);
			}

			float px = BitConverter.ToSingle(dataSub.Data, 0);
			float py = BitConverter.ToSingle(dataSub.Data, 4);
			float pz = BitConverter.ToSingle(dataSub.Data, 8);
			float rx = BitConverter.ToSingle(dataSub.Data, 12);
			float ry = BitConverter.ToSingle(dataSub.Data, 16);
			float rz = BitConverter.ToSingle(dataSub.Data, 20);

			var xclSub = subs.FirstOrDefault(s => s.Type == "XSCL");
			float scale = 1f;
			if (xclSub != null && xclSub.Data.Length >= 4)
			{
				scale = BitConverter.ToSingle(xclSub.Data, 0);
			}

			_instantiateQueue.Enqueue(new InstanceRequest
			{
				Path = nifPath,
				Position = new Vector3(
					(px - wd.Center.X) * WorldScale,
					pz * WorldScale,
					-(py - wd.Center.Y) * WorldScale),
				Rotation = new Vector3(rx, ry, rz),
				FormId = formId,
				BaseType = baseType,
				Scale = scale,
				WorldFormId = wd.FormId,
			});

			if (baseType == "SCOL")
			{
				EmitScolParts(baseSubs, formId, px, py, pz, rx, ry, rz, wd);
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Error processing REFR at 0x{offset:X8}: {e.Message}");
		}
	}

	private void EmitScolParts(List<SubRecord> baseSubs, uint scolFormId,
		float scolPx, float scolPy, float scolPz,
		float scolRx, float scolRy, float scolRz, WorldData wd)
	{
		int i = 0;
		while (i < baseSubs.Count)
		{
			var sub = baseSubs[i];
			if (sub.Type == "ONAM")
			{
				if (sub.Data.Length < 4) { i++; continue; }
				uint statFormId = BitConverter.ToUInt32(sub.Data, 0);

				string partPath = null;
				lock (_esm)
				{
					if (_masterFormIDIndex.TryGetValue(statFormId, out var statEntry))
					{
						var statRec = _esm.GetRecordAtOffset(statEntry.Offset);
						var statSubs = _esm.GetSubRecords(statRec);
						var modl = statSubs.FirstOrDefault(s => s.Type == "MODL");
						if (modl != null)
						{
							partPath = Encoding.ASCII.GetString(modl.Data).TrimEnd('\0').Replace('\\', '/');
							if (!partPath.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase))
								partPath = "meshes/" + partPath;
						}
					}
				}

				if (partPath == null) { i++; continue; }

				if (i + 1 >= baseSubs.Count || baseSubs[i + 1].Type != "DATA")
				{
					i++;
					continue;
				}

				var dataSub = baseSubs[i + 1];
				i += 2;

				if (dataSub.Data.Length < 28) continue;

				int numPlacements = dataSub.Data.Length / 28;
				for (int p = 0; p < numPlacements; p++)
				{
					int off = p * 28;
					float lx = BitConverter.ToSingle(dataSub.Data, off);
					float ly = BitConverter.ToSingle(dataSub.Data, off + 4);
					float lz = BitConverter.ToSingle(dataSub.Data, off + 8);
					float lrx = BitConverter.ToSingle(dataSub.Data, off + 12);
					float lry = BitConverter.ToSingle(dataSub.Data, off + 16);
					float lrz = BitConverter.ToSingle(dataSub.Data, off + 20);
					float scale = BitConverter.ToSingle(dataSub.Data, off + 24);

					float sRz = (float)Math.Sin(Mathf.DegToRad(scolRz)), cRz = (float)Math.Cos(Mathf.DegToRad(scolRz));
					float sRy = (float)Math.Sin(Mathf.DegToRad(scolRy)), cRy = (float)Math.Cos(Mathf.DegToRad(scolRy));
					float sRx = (float)Math.Sin(Mathf.DegToRad(scolRx)), cRx = (float)Math.Cos(Mathf.DegToRad(scolRx));
					float s00 = cRz * cRy;
					float s01 = cRz * sRy * sRx - sRz * cRx;
					float s02 = cRz * sRy * cRx + sRz * sRx;
					float s10 = sRz * cRy;
					float s11 = sRz * sRy * sRx + cRz * cRx;
					float s12 = sRz * sRy * cRx - cRz * sRx;
					float s20 = -sRy;
					float s21 = cRy * sRx;
					float s22 = cRy * cRx;

					float lRz = (float)Math.Sin(Mathf.DegToRad(lrz)), lCz = (float)Math.Cos(Mathf.DegToRad(lrz));
					float lRy = (float)Math.Sin(Mathf.DegToRad(lry)), lCy = (float)Math.Cos(Mathf.DegToRad(lry));
					float lRx = (float)Math.Sin(Mathf.DegToRad(lrx)), lCx = (float)Math.Cos(Mathf.DegToRad(lrx));
					float l00 = lCz * lCy;
					float l01 = lCz * lRy * lRx - lRz * lCx;
					float l02 = lCz * lRy * lCx + lRz * lRx;
					float l10 = lRz * lCy;
					float l11 = lRz * lRy * lRx + lCz * lCx;
					float l12 = lRz * lRy * lCx - lCz * lRx;
					float l20 = -lRy;
					float l21 = lCy * lRx;
					float l22 = lCy * lCx;

					float r00 = s00 * l00 + s01 * l10 + s02 * l20;
					float r01 = s00 * l01 + s01 * l11 + s02 * l21;
					float r02 = s00 * l02 + s01 * l12 + s02 * l22;
					float r10 = s10 * l00 + s11 * l10 + s12 * l20;
					float r11 = s10 * l01 + s11 * l11 + s12 * l21;
					float r12 = s10 * l02 + s11 * l12 + s12 * l22;
					float r20 = s20 * l00 + s21 * l10 + s22 * l20;
					float r21 = s20 * l01 + s21 * l11 + s22 * l21;
					float r22 = s20 * l02 + s21 * l12 + s22 * l22;

					float wx = scolPx + s00 * lx + s01 * ly + s02 * lz;
					float wy = scolPy + s10 * lx + s11 * ly + s12 * lz;
					float wz = scolPz + s20 * lx + s21 * ly + s22 * lz;

					float combinedRx = Mathf.RadToDeg((float)Math.Atan2(r21, r22));
					float combinedRy = Mathf.RadToDeg((float)Math.Asin(Math.Clamp(-r20, -1f, 1f)));
					float combinedRz = Mathf.RadToDeg((float)Math.Atan2(r10, r00));

					_instantiateQueue.Enqueue(new InstanceRequest
					{
						Path = partPath,
						Position = new Vector3(
							(wx - wd.Center.X) * WorldScale,
							wz * WorldScale,
							-(wy - wd.Center.Y) * WorldScale),
						Rotation = new Vector3(combinedRx, combinedRy, combinedRz),
						FormId = statFormId,
						BaseType = "STAT",
						Scale = scale,
						WorldFormId = wd.FormId,
					});
				}
			}
			else
			{
				i++;
			}
		}
	}

	private void SwitchToWorld(string worldName)
	{
		if (worldName == _currentWorldName) return;
		if (!_worldDataByName.ContainsKey(worldName))
		{
			GD.PrintErr($"[Megaton] Unknown world: {worldName}");
			return;
		}

		GD.Print($"[Megaton] Switching to world: {worldName}");

		if (_worldContainers.TryGetValue(worldName, out var container))
		{
			ShowWorld(worldName);
		}
		else
		{
			_ = LoadWorldAsync(worldName);
		}
	}

	private void ShowWorld(string worldName)
	{
		if (_currentWorldName != null && _worldContainers.TryGetValue(_currentWorldName, out var oldContainer))
		{
			oldContainer.Visible = false;
		}

		_currentWorldName = worldName;

		if (_worldContainers.TryGetValue(worldName, out var container))
		{
			container.Visible = true;
		}

		if (_worldLabel != null)
		{
			int idx = _worldNameList.IndexOf(worldName);
			_worldLabel.Text = $"World [{idx + 1}]: {worldName}";
		}

		GD.Print($"[Megaton] Now showing world: {worldName}");
	}

	private void CreateAndAddInstance(InstanceRequest req)
	{
		Node3D container;
		if (req.WorldFormId != 0 && _worldNameById.TryGetValue(req.WorldFormId, out var wName))
		{
			container = _worldContainers.TryGetValue(wName, out var c) ? c : this;
		}
		else
		{
			container = this;
		}

		MeshInstance3D inst = null;

		if (!string.IsNullOrEmpty(req.Path))
		{
			var mesh = GetOrBuildMesh(req.Path);
			if (mesh == null && req.BaseType != "LIGH") return;

			if (mesh != null)
			{
				inst = new MeshInstance3D { Mesh = mesh };

				var basis = Basis.Identity;
				basis = basis.Rotated(Vector3.Up,      -req.Rotation.Z);
				basis = basis.Rotated(Vector3.Forward,  req.Rotation.Y);
				basis = basis.Rotated(Vector3.Right,    req.Rotation.X);
				basis = basis.Scaled(Vector3.One * req.Scale);

				inst.Transform = new Transform3D(basis, req.Position);
				container.AddChild(inst);

				if (_nifCache.TryGetValue(req.Path, out var nif))
				{
					NIFCollisionBuilder.BuildCollision(nif, inst);
				}
			}
		}

		if (req.BaseType == "LIGH")
		{
			var lightData = _lightingLoader.ParseLight(req.FormId);
			if (lightData == null) return;

			var lightNode = LightingLoader.CreateLightNode(lightData, req.Position, req.Rotation);
			if (lightNode != null)
			{
				container.AddChild(lightNode);
			}
		}
	}

	private void EnsureNifParsed(string path)
	{
		if (_nifCache.ContainsKey(path)) return;

		BSAFile match = null;
		BSAReader owner = null;
		for (int i = 0; i < _meshBsaList.Count && owner == null; i++)
		{
			var (bsa, files) = _meshBsaList[i];
			match = files.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
			if (match != null) owner = bsa;
		}

		if (owner == null) return;

		byte[] nifData = owner.ReadFileData(match);
		if (nifData == null) return;

		var nif = new NIFReader();
		nif.Parse(nifData);

		if (nif.Blocks.Count > 0)
		{
			_nifCache.TryAdd(path, nif);
		}
	}

	private ArrayMesh GetOrBuildMesh(string path)
	{
		if (_meshCache.TryGetValue(path, out var cached)) return cached;
		if (!_nifCache.TryGetValue(path, out var nif)) return null;

		var geom = NIFMeshBuilder.ExtractGeometry(nif);
		if (geom.Surfaces.Count == 0) return null;

		var mesh = NIFMeshBuilder.BuildArrayMesh(geom);
		if (mesh.GetSurfaceCount() > 0)
		{
			for (int i = 0; i < mesh.GetSurfaceCount(); i++)
			{
				string texPath = mesh.SurfaceGetName(i);

				if (geom.Surfaces.Count > i)
				{
					var surface = geom.Surfaces[i];
					var mat = NIFMaterialBuilder.BuildMaterial(surface.Shader, surface.Alpha, LoadTexture);

					if (mat.AlbedoTexture == null && !string.IsNullOrEmpty(texPath))
					{
						var tex = LoadTexture(texPath);
						if (tex != null)
						{
							mat.AlbedoTexture = tex;
						}
					}

					mat.RenderPriority = i;
					mesh.SurfaceSetMaterial(i, mat);
				}
				else if (!string.IsNullOrEmpty(texPath))
				{
					var tex = LoadTexture(texPath);
					if (tex != null)
					{
						var mat = new StandardMaterial3D { AlbedoTexture = tex };
						mat.RenderPriority = i;
						mesh.SurfaceSetMaterial(i, mat);
					}
				}
			}

			_meshCache.TryAdd(path, mesh);
			return mesh;
		}

		return null;
	}

	private Texture2D LoadTexture(string path)
	{
		if (string.IsNullOrEmpty(path)) return null;

		path = path.Replace('\\', '/');

		string searchPath = path;
		if (!searchPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
			searchPath = "textures/" + searchPath;

		if (_textureCache.TryGetValue(searchPath, out var cached)) return cached;

		if (_textureBsaList.Count == 0) return null;

		BSAFile file = null;
		BSAReader owner = null;
		foreach (var (bsa, files) in _textureBsaList)
		{
			file = files.FirstOrDefault(f => f.Path.Equals(searchPath, StringComparison.OrdinalIgnoreCase));
			if (file != null) { owner = bsa; break; }
		}

		if (file == null && searchPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
		{
			string altPath = searchPath.Substring(9);
			foreach (var (bsa, files) in _textureBsaList)
			{
				file = files.FirstOrDefault(f => f.Path.Equals(altPath, StringComparison.OrdinalIgnoreCase));
				if (file != null) { owner = bsa; break; }
			}
		}

		if (file == null || owner == null) return null;

		byte[] data = owner.ReadFileData(file);
		if (data == null) return null;

		var img = new Image();
		Error err = img.LoadDdsFromBuffer(data);
		if (err == Error.Ok)
		{
			var tex = ImageTexture.CreateFromImage(img);
			_textureCache.TryAdd(searchPath, tex);
			return tex;
		}

		return null;
	}
}
