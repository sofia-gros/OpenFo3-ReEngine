# 複数ワールドスペース対応

## 概要

Fallout 3 の ESM に含まれる全 WRLD レコードを自動検出し、キーボード操作でワールドスペースを切り替えられるようにする。

## 変更内容

### Megaton.cs 改修

**改修前の課題:**

- `_Ready()` で `GamePaths.GetTargetWorld()` の単一ワールド名のみ検索
- すべてのメッシュインスタンスをルートノード直下に追加
- ワールド切り替え手段なし

**改修後の設計:**

```
Megaton (Node3D)
├── WorldLabel (Label3D) - 現在のワールド名表示
├── World_MegatonWorld (Node3D) - 非表示時 Visible=false
│   ├── Terrain_* (MeshInstance3D)
│   ├── CellDirectionalLight (DirectionalLight3D)
│   ├── WorldEnvironment (WorldEnvironment)
│   ├── Mesh_* (MeshInstance3D)
│   └── Light_* (OmniLight3D/SpotLight3D)
├── World_WastelandWorld (Node3D)
│   └── ...
└── ...
```

### 主要な新機能

1. **WRLD 自動検出** (`DiscoverWorlds()`)
   - ESM の全 WRLD レコードを走査
   - EDID（ワールド名）、DNAM（デフォルト地形高さ）、MNAM（セル範囲）を抽出
   - MNAM から Center 座標を自動計算 `((nw+se)*CellSize/2)`
   - ソート順: Megaton > Wasteland > DC > Interior > その他

2. **ワールドコンテナ分離**
   - 各ワールドに `Node3D` コンテナを割り当て
   - 地形・ライティング・メッシュをコンテナの子として追加
   - コンテナの `Visible` プロパティで表示/非表示を切り替え

3. **遅延ロード** (`LoadWorldAsync()`)
   - 初回アクセス時のみロード（`_worldContainers` 辞書で管理）
   - 2 回目以降はコンテナの可視性のみ変更
   - 高速切り替えが可能

4. **キーボード切り替え** (`_Input()`)
   - F1-F9: `_worldNameList` のインデックスに対応
   - 現在のワールド名を Label3D で表示（Billboard 有効）

5. **非同期 REFR ロード**
   - `Task.Run(() => LoadWorldRefrs(wd))` でバックグラウンド処理
   - InstanceRequest に `WorldFormId` フィールドを追加
   - `_Process` で正しいコンテナにルーティング

6. **共有キャッシュ**
   - メッシュ (`_meshCache`)、NIF (`_nifCache`)、テクスチャ (`_textureCache`) は全ワールドで共有

### WorldData 構造体

```csharp
private struct WorldData
{
    public uint FormId;             // WRLD レコードの FormID
    public string Name;             // EDID（例: "MegatonWorld"）
    public float DefaultLandHeight; // DNAM のデフォルト地形高さ
    public int NwCellX, NwCellY;    // MNAM の北西セル座標（展開後）
    public int SeCellX, SeCellY;    // MNAM の南東セル座標（展開後）
    public Vector2 Center;          // Center 座標（MNAM から計算）
    public bool HasMnam;            // MNAM の有無
}
```

### キーコードマッピング

| キー | 機能                                  |
| ---- | ------------------------------------- |
| F1   | 1 番目のワールド（通常 MegatonWorld） |
| F2   | 2 番目のワールド                      |
| F3   | 3 番目のワールド                      |
| F4   | 4 番目のワールド                      |
| ...  | ...                                   |
| F9   | 9 番目のワールド                      |

## 設定

`config.json` の `World` セクションで TargetWorld と Center を指定可能。
MNAM を持つワールドは自動計算される。

```json
"World": {
    "TargetWorld": "MegatonWorld",
    "CenterX": -14200.0,
    "CenterY": -3800.0
}
```

## 制限事項

- カメラ位置はワールド切り替え時にリセットされない（手動移動が必要）
- WastelandWorld 等の大規模ワールドはロードに時間がかかる
- 内装ワールドで MNAM がない場合、config.json の Center 値が使用される
- ワールドごとの個別ライティングは各コンテナで独立

## 今後の改善案

- ワールド切り替え時のカメラ位置自動調整
- ワールド名一覧表示（メニューUI）
- config.json でのワールド別 Center 座標設定
- ワールドのアンロード（メモリ解放）
- ワールド間ポータル/ドア遷移（プレイヤー位置に応じた自動切替）
