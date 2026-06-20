# Task List - Redot Engine Mono で Fallout 3 を再現するためのタスク

## 凡例

- [x] 完了
- [ ] 未完了
- [-] 一部実装済み/保留中

---

## 1. シェーダー/マテリアルシステム

FO3 の BSShaderPPLightingProperty に相当するシェーダーシステム。現状は拡散テクスチャのみ StandardMaterial3D に適用。

- [x] 拡散テクスチャの抽出と適用
- [x] ノーマルマップ（Environment Mask 赤チャンネルを Roughness に使用）
- [x] スペキュラー/グロスマップ（Environment Mask Green→Metallic, Red→Roughness のデュアルチャンネル対応）
- [x] 環境/反射マップ（Slot 4→MetallicTexture, Slot 5→Roughness+Metallic 全シェーダータイプ対応）
- [x] ディテールマップ（Slot 6 DetailAlbedo: DetailBlendMode=Mix, Uv1Scale=4x）
- [x] パララックス/ハイトマップ（抽出・適用済み、ParallaxScale/ParallaxMaxPasses 対応）
- [x] アルファテスト/ブレンドモードの完全対応（NiAlphaProperty + ShaderFlags ビット8）
- [x] シェーダータイプの区別（全18タイプ中15タイプ実装、Wing/ SnowShader/ ZBufferWrite 追加）
- [x] 頂点カラーの適用（NiGeometryData からの抽出と SurfaceTool.SetColor 対応）
- [ ] スキニング/アニメーション（NiSkinInstance, NiSkinData, KF ファイル）
- [x] パーティクルシステム（BSStripParticleSystem→GPUParticles3D 変換、NiParticleSystem パース）

## 2. ライティングシステム

オリジナルゲームのライティングデータ（LIGH レコード、セルごとのライティング、シャドウマップ）の処理。

- [x] セルライティング（XCLL サブレコードの読み込み）
- [x] テンプレートライティング継承マージ（LGTM + LNAM）
- [x] DirectionalLight3D + Fog (WorldEnvironment) の設定
- [x] オブジェクトごとのライト（LIGH→OmniLight3D/SpotLight3D 変換）
- [x] **ライトの向きが正しくない問題**（#11）— 二重変換除去, FalloffExponent 対応
- [x] シャドウマップの再現（PSSM, ShadowBias 調整, atlas_size=4096）
- [x] オリジナルゲームの LightingTemplate（LGTM）の完全再現

## 3. 地形/地表レンダリング

LAND レコードからの地形メッシュ生成。

- [x] LAND レコードの解析（VHGT, VNML, VCLR, BTXT）
- [x] 33x33 グリッドメッシュ生成
- [x] ギャップフィリング（平坦地形での補完）
- [x] 地形衝突形状の生成
- [x] **メガトンの地面がない問題**（#10）— 内装ワールド用フォールバック平坦地形生成
- [x] \_masterFormIDIndex での LAND フィルタ（MasterFormIDIndex 共有により重複インデックス排除）
- [x] テクスチャタイリング/ブレンド（BTXT の完全対応）
  - [x] UV タイル 256 単位リピート（CellSize/256 = 16）
- [x] 地形 LOD（9x9 グリッド簡略化メッシュ生成、距離判定による自動切替）

## 4. 衝突/物理

NIF 内の bhk\* 物理ブロックからの衝突形状生成。

- [x] bhkCollisionObject / bhkRigidBody の解析
- [x] 各形状タイプの変換（Box, Sphere, Capsule, ConvexVertices, MoppBvTree, PackedNiTriStrips, List, NiTriStrips, Transform）
- [x] Half-float デコンプレッション
- [x] StaticBody3D + CollisionShape3D の構築（MeshInstance3D の親として StaticBody3D を正しく配置）
- [x] 動的物理（bhkRigidBody MotionType → RigidBody3D/CharacterBody3D/StaticBody3D 自動選択）
- [ ] レイキャスト/ピッキング
- [x] Havok 由来の物理パラメータ（摩擦、反発）の対応（HavokMaterial→PhysicsMaterial 変換、21種類対応）

## 5. ちらつき/描画問題

- [x] カメラ回転時のメッシュの見え/見えのちらつき（原因: メッシュの向きが不正）
- [x] **メッシュ向きバグの根本原因を特定**（Flags フィールド読み取り誤り）
  - 原因: `NIFBlockResolver.cs` で NiAVObject の `Flags` を uint16(2byte) で読んでいた（本来は uint32/4byte）
  - 2byte のシフトが発生し Translation/Rotation/Scale がすべて破損していた
  - PyFFI (pyffi.formats.nif.NifFormat) 基準 + 全ブロック余り=0 で uint32 を確定
  - 修正: `NIFBlockResolver.cs:374` を `br.ReadUInt32()` に復元
- [-] **Redot 上での向き検証**（#12）
  - 外壁の向きは完璧だが街中の建築物の回転が合わない問題が残存
  - REFR 回転コード（`Megaton.cs` CreateAndAddInstance）を改訂済み: Up(-rz) → Forward(ry) → Right(rx) の順
  - NIF→Godot 座標変換は numpy + PyFFI で検証済み（相似変換 R_conv @ R_fo3 @ R_conv^-1）
  - 三角形の向き（フロント/バックフェイス）の確認は未実施

## 6. ワールド/セル読み込み

- [x] ESM パース（GRUP 階層、圧縮レコード、FormId インデックス）
- [x] BSA アーカイブ読み込み（v104/v105, ZLib 展開）
- [x] NIF モデル解析（バージョン 20.2.0.7, userVer=11, bsHeader=0x22）
- [x] NIF ブロック階層解決（NiNode/NiGeometry/BSFadeNode/BSProperty 系を完全対応）
- [x] **NiAVObject Flags = uint32 を確定**（PyFFI 基準で検証）
- [x] ジオメトリ抽出（NiTriStripsData, NiTriShapeData）
- [x] 座標変換（FO3: XYZ → Godot: XZ-Y, WorldScale=0.015）
- [x] NIF 頂点変換の数学的検証（`NIFMeshBuilder.cs` R_conv @ R_fo3, numpy で証明）
- [x] 非同期ワールドローディング（Parallel.ForEach + \_Process queue）
- [x] メッシュ/テクスチャ/NIF キャッシング
- [ ] STAT/DOOR 以外のレコードタイプ対応
  - [x] FURN（家具）
  - [x] ACTI（アクティベータ）
  - [x] LIGH（光源）
  - [x] MSTT（可動静的オブジェクト）
  - [x] CONT（コンテナ）
  - [x] MISC（雑貨）
  - [x] WEAP（武器）
  - [x] ARMO（防具）
  - [x] TREE（木）
  - [x] SCOL（静的コレクション）— ONAM+DATA パーツのパースと個別 STAT メッシュ配置
  - [x] KEYM（鍵）
  - [x] ARMA（防具追加パーツ）
  - [x] NOTE（手紙）
  - [x] PWAT（設置水）
  - [x] TACT（会話型アクティベータ）
  - [x] AMMO（弾薬）
- [x] その他の BSA 読み込み（Misc, Voices 等、config.json の全 BSA を動的読み込み）
- [x] ワールドスペースの切り替え（Wasteland, DC ruins 等）

## 7. NAVMESH/経路探索

- [x] NAVM レコードの解析（NVVX/NVTR/NVDP サブレコードのパース）
- [x] ナビゲーションメッシュの生成（NavigationMesh → NavigationRegion3D）
- [ ] エージェント経路探索（NavigationAgent3D による NPC 経路探索）

## 8. サウンド/オーディオ

- [ ] オーディオシステムの実装
- [ ] BSA からのサウンドファイル展開
- [ ] 環境音/効果音の配置
- [ ] ボイス/会話システム

## 9. UI/HUD

- [ ] UI システムの実装
- [ ] PIP-Boy インターフェース
- [ ] HUD（体力, AP, コンパス, ターゲット情報）
- [ ] メニュー/インベントリ
- [ ] 会話UI

## 10. キャラクター/アニメーション

- [x] NiSkinInstance / NiSkinData の解析（バイナリレイアウト検証済み、SkinDataStore に保存）
- [x] スキンインスタンスのジオメトリへの関連付け（Node.SkinInstanceIndex → SkinDataStore）
- [x] スケルトン/アーマチュアの構築（NiBone階層→Skeleton3D + ボーンRestTransform）
- [x] 頂点ウェイトの抽出（BoneVertWeight → 頂点ごとの{boneId, weight}配列）
- [ ] 頂点ウェイトのSurfaceTool適用（Redot API 非対応のため保留）
- [ ] KF アニメーションファイルの読み込み
- [ ] アニメーション再生システム
- [ ] 第三人称カメラ

## 11. NPC/AI/ゲームプレイ

- [ ] NPC 配置（ACHR, ACRE レコード）
- [ ] AI パッケージ（AIPackages）
- [ ] 会話/ダイアログ（QUST, DIAL, INFO）
- [ ] 戦闘システム
- [ ] インベントリ/アイテム管理
- [ ] レベル/スキル/特典システム
- [ ] SAVE/ロード

## 12. クエスト/ストーリー

- [ ] クエストシステム（QUST レコード）
- [ ] ストーリーダイアログ
- [ ] スクリプト（フォールアウトスクリプト言語？）

## 13. ツール/インフラ

- [x] Python 解析ツールキット（AI/ ディレクトリ）
- [x] パス設定の設定ファイル化（GamePaths.cs のハードコード除去）
- [x] config.json による Fallout3Root パス設定（GamePaths.cs を改修）
- [x] BSA ファイルリストの設定ファイル化（複数アーカイブ対応）
- [x] ワールド設定の設定ファイル化（TargetWorld・CenterX/Y）
- [x] デバッグ表示/統計情報（FPS, カメラ位置, キャッシュ数, ワールド名, キュー状態）
- [ ] パフォーマンス最適化
- [ ] エラーハンドリングの強化

---

## 実装ログ

### 2026-06-19: #11 ライトの向きが正しくない問題 - 修正 (fix/light-direction)

**問題分析:**
1. **位置の二重変換**: `CreateLightNode` が受け取る `position` は `Megaton.cs.ProcessRecord` で既に Godot 座標に変換済み（`req.Position`、メッシュと同じ座標）。しかし `CreateLightNode` 内で再度 `(X, Z, -Y)` の座標変換を適用していたため位置がズレていた。
2. **LightEnergy 固定値**: すべてのライトで `LightEnergy = 1.0f`。FO3 の `FalloffExponent` が無視。
3. **範囲下限**: `Mathf.Max(radius, 1f)` により小さな光源の範囲が不自然に拡大。

**修正内容:**
- `LightingLoader.cs`:
  - 位置の二重変換を削除、`position` を直接 `Transform3D` に使用
  - `FalloffExponent` → `Attenuation` + `LightEnergy` の変換を追加
  - 範囲下限を 1.0 → 0.5 Godot 単位に緩和

---

### 2026-06-19: #10 メガトンの地面がない問題 - 修正

**問題分析:**
1. MegatonWorld は内装ワールドスペースであり、LAND レコードを持たない。または CELL に XCLC 座標がない。
2. LAND レコードがない場合 gap fill が実行されず、地形が一切生成されない。
3. MNAM セル範囲が未設定の場合、地形生成範囲が 0 セルになる。

**修正内容:**
- `TerrainBuilder.cs`: LAND レコード不在時の最終フォールバック平坦地形生成
- `Megaton.cs`: MNAM 範囲の自動拡張（最低15x15セル）

---

### 2026-06-20: マテリアルシステム再構築と機能拡充

1. **NIFMaterialBuilder.cs 構造破損修正**
   - BuildDefault のブレース不一致、浮遊コード、ApplyRefraction 欠落を修正
   - Redot 26.1 の SubsurfScatter API に対応
   - 23 errors → 0 errors に改善

2. **頂点カラー対応**
   - NiGeometryData から RGBA 頂点カラー（float×4）を抽出
   - SurfaceTool.SetColor() でメッシュに適用
   - SkinTint/HairTint のティント効果が正しく発現

3. **シェーダータイプ拡張（3タイプ追加）**
   - Wing (type 29): 半透明 + 両面レンダリング
   - SnowShader (type 14): 明るい白色ティント
   - ZBufferWrite (type 5): 標準不透明マテリアル
   - 実装済み: 15/18 タイプ

4. **ライティング改善**
   - Ambient ライトの二重適用を修正（DirectionalLight3D 削除）
   - DirectionalShadowMaxDistance を 200→300 に延長
   - PSSM スプリット調整 (0.05/0.15/0.4)
   - project.godot に shadow_atlas_size=4096 等を追加

5. **設定ファイル化**
   - config.json 新規作成（Fallout3Root 等を外部設定可能に）
   - GamePaths.cs を JSON 設定読み込み対応に改修

6. **レコードタイプ対応拡充**
   - マスターインデックスに KEYM, ARMA, NOTE, PWAT, TACT, AMMO 追加
   - 全 26 タイプ対応

 7. **ドキュメント**: 7件のドキュメントを作成
 8. **地形テクスチャタイル UV 修正**: FO3 基準の 256 単位タイルを正しく反映（CellSize/256=16 リピート）
 9. **ビルド警告ゼロ**: 3件の不要な catch 変数を削除
 10. **複数 BSA アーカイブ対応**: config.json の BSA セクションから全アーカイブを動的読み込み（Meshes, Textures, Misc 等）
     - meshBsaList / textureBsaList に振り分け（Misc.bsa は両方に参加）
     - EnsureNifParsed / LoadTexture を全 BSA 走査対応に改修
 11. **SCOL パーツ配置**: ONAM+DATA サブレコードの解析と個別 STAT インスタンスの生成
     - SCOL のワールド変換と Part ローカル変換の合成
     - InstanceRequest に Scale フィールド追加
     - MODL フォールバックから完全なパーツ配置へ改善
 12. **NiSkinInstance/NiSkinData パース基盤**: FO3 バージョン (20.2.0.7) のバイナリレイアウトを nifxml から検証
     - NiTransform / NiBound / BoneVertData / BoneData / SkinData の全構造体定義
     - NiSkinInstance: Data Ref, SkinPartition Ref, SkeletonRoot Ptr, Bones Ptr[]
     - NiSkinData: SkinTransform, NumBones, HasVertexWeights, BoneList[]
     - SkinDataStore に保存、NIFReader.SkinData からアクセス可能
     - Node.SkinInstanceIndex でジオメトリとスキンデータを紐付け
 13. **config.json World 設定**: TargetWorld / CenterX / CenterY を外部設定化
     - GamePaths に GetTargetWorld() / GetWorldCenter() 追加
     - Megaton.cs のハードコード "MegatonWorld" を config から読み取りに変更
 14. **NiNode 派生型拡充**: NiBillboardNode / NiSortAdjustNode / NiSwitchNode /
     BSValueNode / BSOrderedNode / BSRangeNode / BSMultiBoundNode /
     BSTreeNode / NiBone / RoomMarker / BSMasterParticleSystem / BSStripParticleSystem /
     BSRefractionFireGlow / NiAmbientLight / NiDirectionalLight / NiSpotLight / NiPointLight
     を NIFBlockResolver のノード種別に追加
     - 未対応ノードの子階層がトラバースされない問題を修正
 15. **NiSkinPartition パース**: GPU スキニング最適化データのパース追加
     - SkinPartitionEntry: ボーンマップ、頂点ウェイト、インデックス、ストリップス
     - HasVertexMap / HasVertexWeights / HasFaces / HasBoneIndices の条件付きフィールド対応
     - SkinDataStore.SkinPartitions に保存

---

## 優先度（当面の目標）

1. **高優先度** - レンダリングの完全性
   - [x] メガトンの地面がない問題（#10）
   - [x] ライトの向きが正しくない問題（#11）
   - [-] **街中の建築物の向きが合わない問題**（#12）— Redot 上での視覚検証待ち
   - [x] ノーマルマップ/スペキュラーマップの対応（Environment Mask デュアルチャンネル）
   - [x] 頂点カラー対応
   - [x] シェーダータイプ区別（15/18 タイプ実装済み）
   - [x] 環境/反射マップ対応（Slot 4→Metallic, Slot 5→Roughness+Metallic）
   - [x] ディテールマップ対応（DetailBlendMode + UV 設定）

2. **中優先度** - 拡張
   - [x] 全レコードタイプ対応（26タイプ）
   - [x] 複数ワールドスペース
   - [x] シャドウマップ
   - [x] NiSkinInstance/NiSkinData パース基盤
   - [x] 地形 LOD（9x9 簡略メッシュ + 距離切替）
   - [x] LAND インデックス最適化（共有インデックス使用）
   - [x] StaticBody3D の親子関係修正（MeshInstance3D の親として StaticBody3D を正しく配置）
   - [x] Havok 物理パラメータ対応（摩擦・反発のマテリアルマッピング）
   - [x] デバッグ表示（FPS, カメラ位置, キャッシュ統計, キュー状態）

3. **低優先度** - ゲームプレイ
   - NPC/AI
   - UI/HUD
   - サウンド
   - アニメーション
   - クエスト

---

### 2026-06-20: 複数ワールドスペース対応 - 実装 (feat/multi-world)

**問題:**
1. Megaton.cs が単一ワールド（MegatonWorld）にハードコードされており、WastelandWorld や DC ruins 等の他のワールドスペースに切り替えられない。
2. ワールドごとに異なる Center 座標、地形、ライティング、REFR 配置が必要。
3. ワールド切り替えの UI 手段がない。

**修正内容:**
- `Megaton.cs` 全面改修:
  - 全 WRLD レコードを `_Ready()` で自動検出（`DiscoverWorlds()`）
  - WRLD の MNAM セル範囲から Center 座標を自動計算
  - ワールドごとに `Node3D` コンテナを生成し、地形・ライティング・メッシュインスタンスを分離
  - キーボード F1-F9 でワールド切り替え（`_Input()` オーバーライド）
  - `LoadWorldAsync()` / `SwitchToWorld()` で遅延ロード + 表示切替
  - ワールド名表示 Label3D を追加
- 共有キャッシュ（メッシュ, NIF, テクスチャ）は全ワールドで再利用
- REFR 非同期ロードは worldFormId でタグ付けし、正しいコンテナにルーティング

**既知の制限:**
- カメラ位置はワールド切り替え時に維持（手動移動が必要）
- 大規模ワールド（WastelandWorld）のロードは時間がかかる
- 内装ワールドの Center 座標は MNAM がない場合 config.json のデフォルト値を使用

### 2026-06-20: マテリアル改善 + 地形 LOD + LAND インデックス最適化

1. **スペキュラー/グロスマップ改善**: Environment Mask (Slot 5) の Green チャンネルを Metallic に使用。Red→Roughness、Green→Metallic のデュアルチャンネルマッピングを実装。specular flag (bit 0) がオンの場合のみ適用。

2. **環境/反射マップ対応拡充**: Environment Map (Slot 4) を全シェーダータイプで MetallicTexture として適用。従来は EnvironmentMap シェーダーのみ対応だったものを BuildDefault 含む全ビルダーに展開。

3. **ディテールマップ修正**: DetailBlendMode を Mix に設定、Uv1Scale を 4x に設定してディテールテクスチャが正しく重なるよう修正。

4. **LAND インデックス共有最適化**: TerrainBuilder が独自に LAND インデックスを構築するのをやめ、Megaton.cs の _masterFormIDIndex（既に LAND を含む）を SetLandIndex() で共有。メモリ重複排除。

5. **地形 LOD 実装**: 33x33 グリッドから 4 スキップで 9x9 の簡略化メッシュを生成。_Process 内の UpdateTerrainLod() でカメラ距離 60 Godot 単位を閾値に自動切替。

6. **リファクタリング**: ApplyDiffuse/ApplyNormalMap/ApplyEmission/ApplyEnvironmentMask/ApplyEnvironmentMap のヘルパー分割。コード重複削減と責務の明確化。

### 2026-06-20: パフォーマンス最適化（プーリング＋フラスタムカリング＋LOD基盤）

1. **インスタンスプーリング**: MeshInstance3D を NIFパス単位でプールし、`new MeshInstance3D()` の代わりに `RentMeshInstance()` で再利用
2. **フラスタムカリング**: `UpdateFrustumCulling()` で `Camera3D.IsPositionInFrustum()` による視野外プロップの非表示化。カメラFar=300に設定
3. **Prop LOD基盤**: `UpdatePropLod()` で距離に応じたメッシュ差し替え機構。`TrackProp()` ですべての生成プロップを追跡
4. **ワールド切り替え時のプール解放**: `ReturnWorldPropsToPool()` で非アクティブワールドのプールリサイクル

今後の拡張: LODメッシュ自動生成（マージ/デシメーション）、SpatialGridによる空間分割

### 2026-06-20: 残り3シェーダータイプ実装（FO3 Sky / Water / Unlit）

1. **BSVersion検出**: NIFReader に BsVersion フィールド追加、BSVer=34 以下を FO3 と判定
2. **シェーダータイプ変換**: TranslateFO3ShaderType で FO3 BSShaderType (0,1,10,14,15,17,29,32,33) を内部正規型に変換
   - FO3 TallGrass(0) → internal TallGrass(34)
   - FO3 Sky(10) → internal FO3Sky(40)
   - FO3 Water(17) → internal FO3Water(41)
   - FO3 NoLighting(33) → internal FO3Unlit(42)
3. **3新ハンドラ実装**: BuildFO3Sky（無光・発光）、BuildFO3Water（透明・屈折）、BuildFO3Unlit（無光・強制発光）
4. **Skyrim互換維持**: BSVer>=35 の NIF は従来通り BSLightingShaderType ID をそのまま使用

### 2026-06-20: パーティクルシステム + 動的物理 + スキニング基盤実装

1. **パーティクルシステム**: BSStripParticleSystem の NiParticleSystem フィールドパース（WorldSpace, ModifierRefs）、TraverseExtract で検出し GPUParticles3D + ParticleProcessMaterial を生成
2. **動的物理**: bhkRigidBody CInfo550_660 から MotionType を読み取り、StaticBody3D / RigidBody3D / CharacterBody3D を自動選択。質量・減衰パラメータ対応
3. **スキニング基盤**: NiBone 階層から Skeleton3D 構築（ボーン名・RestTransform・親子関係）、BoneVertWeight の頂点ごとのボーンインデックス/ウェイト配列変換（最大4ボーン、正規化）
4. **未対応**: SurfaceTool.SetBoneIndices/SetBoneWeights が Redot 26.1 に存在しないため、ウェイト適用は保留。スケルトンとメッシュの生成のみ完了

### 2026-06-20: 衝突形状の親子関係修正 + Havok 物理パラメータ + デバッグ表示

1. **StaticBody3D 親子関係修正**: MeshInstance3D を StaticBody3D の子として正しく配置するよう CreateAndAddInstance を改修。
   - BuildCollision に skipAdd パラメータを追加し、呼び出し元で配置を制御
   - 従来: Container → MeshInstance3D → StaticBody3D → CollisionShape3D
   - 修正後: Container → StaticBody3D + CollisionShape3D → MeshInstance3D

2. **Havok 物理パラメータ対応**: HavokMaterial → Godot PhysicsMaterial の変換マッピングを実装。
   - 21 種類の Havok マテリアル（Stone, Cloth, Dirt, Glass, Grass, Metal, Organic, Skin, Water, Wood, Heavy Stone, Heavy Metal, Heavy Wood, Chain, Snow, Elevator, Hollow Metal, Sheet Metal, Sand, Broken Concrete, Iron）に対応
   - Box/Sphere/Capsule/ConvexVertices/NiTriStrips/TransformShape からマテリアル抽出
   - Friction（摩擦）と Bounce（反発）を Godot PhysicsMaterial に設定

## 最近の変更履歴

### メッシュ向きバグ調査（直近セッション）
- **根本原因を特定**: `NIFBlockResolver.cs:374` の `Flags` が uint16 で読まれていた（正しくは uint32）
- AI アシスタント（Claude）が uint32 → uint16 に誤って変更し、2byte シフトで全 NiAVObject の Translation/Rotation/Scale が破損
- PyFFI で 3 つの NIF ファイル（`generichangingwire01.nif` 含む）を基準に検証 → 常に uint32 で余り=0 になることを確認
- `AI/verify_flags.py`, `verify_refr_math.py`, `verify_refr_math2.py`, `verify_full_pipeline.py` で数式を証明
- 修正後ビルド成功（0 エラー / 0 警告）
- **残課題**: Redot 上でメガトン街中の建築物の回転が合っているか視覚検証する（外壁は完璧）
- 補足: REFR 回転コード（`Megaton.cs` CreateAndAddInstance）は相似変換として数学的に正しいことを確認済み
