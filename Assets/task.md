# Task List - Redot Engine Mono で Fallout 3 を再現するためのタスク

## 凡例

- [x] 完了
- [ ] 未完了
- [-] 一部実装済み/保留中

---

## 1. シェーダー/マテリアルシステム

FO3 の BSShaderPPLightingProperty に相当するシェーダーシステム。現状は拡散テクスチャのみ StandardMaterial3D に適用。

- [x] 拡散テクスチャの抽出と適用
- [-] ノーマルマップ（NIFMaterialBuilder に抽出ロジックあり、マッピング未完成）
- [-] スペキュラー/グロスマップ（一部実装、正しい動作未確認）
- [-] 環境/反射マップ（Slot 4 抽出→Metallic テクスチャとして設定のみ）
- [-] ディテールマップ（Slot 6 DetailAlbedo 設定のみ、動作未確認）
- [ ] パララックス/ハイトマップ（抽出ロジック未検証）
- [ ] アルファテスト/ブレンドモードの完全対応
- [ ] シェーダータイプの区別（FO3 の Shader Type 1, 17 等の振る舞いの再現）
- [ ] 頂点カラーの適用（地形以外のメッシュに未対応）
- [ ] スキニング/アニメーション（NiSkinInstance, NiSkinData, KF ファイル）
- [ ] パーティクルシステム（FO3 パーティクルデータの解析と描画）

## 2. ライティングシステム

オリジナルゲームのライティングデータ（LIGH レコード、セルごとのライティング、シャドウマップ）の処理。

- [x] セルライティング（XCLL サブレコードの読み込み）
- [x] テンプレートライティング継承マージ（LGTM + LNAM）
- [x] DirectionalLight3D + Fog (WorldEnvironment) の設定
- [-] オブジェクトごとのライト（LIGH→OmniLight3D/SpotLight3D 変換）
- [ ] **ライトの向きが正しくない問題**（#11）
  - 回転変換が誤っている可能性
  - カメラがライトに埋まると非表示になる問題
  - エンジン標準ライトへの変換が適切か検討
- [ ] シャドウマップの再現
- [ ] オリジナルゲームの LightingTemplate（LGTM）の完全再現

## 3. 地形/地表レンダリング

LAND レコードからの地形メッシュ生成。

- [x] LAND レコードの解析（VHGT, VNML, VCLR, BTXT）
- [x] 33x33 グリッドメッシュ生成
- [x] ギャップフィリング（平坦地形での補完）
- [x] 地形衝突形状の生成
- [ ] **メガトンの地面がない問題**（#10）
  - 地面が別データとして格納されている可能性
  - 該当する LAND レコードの特定と抽出
- [-] \_masterFormIDIndex での LAND フィルタ（コメントアウト中）
- [ ] テクスチャタイリング/ブレンド（BTXT の完全対応）
- [ ] 地形 LOD

## 4. 衝突/物理

NIF 内の bhk\* 物理ブロックからの衝突形状生成。

- [x] bhkCollisionObject / bhkRigidBody の解析
- [x] 各形状タイプの変換（Box, Sphere, Capsule, ConvexVertices, MoppBvTree, PackedNiTriStrips, List, NiTriStrips, Transform）
- [x] Half-float デコンプレッション
- [-] StaticBody3D + CollisionShape3D の構築
- [ ] 動的物理（RigidBody, CharacterBody）
- [ ] レイキャスト/ピッキング
- [ ] Havok 由来の物理パラメータ（摩擦、反発）の対応

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
  - [ ] FURN（家具）
  - [ ] ACTI（アクティベータ）
  - [ ] LIGH（光源）
  - [ ] MSTT（可動静的オブジェクト）
  - [ ] CONT（コンテナ）
  - [ ] MISC（雑貨）
  - [ ] WEAP（武器）
  - [ ] ARMO（防具）
  - [ ] TREE（木）
  - [ ] SCOL（静的コレクション）
- [ ] その他の BSA 読み込み（Voices, Misc 等）
- [ ] ワールドスペースの切り替え（Wasteland, DC ruins 等）

## 7. NAVMESH/経路探索

- [ ] NAVM レコードの解析
- [ ] ナビゲーションメッシュの生成
- [ ] エージェント経路探索

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

- [ ] NiSkinInstance / NiSkinData の解析
- [ ] スケルトン/アーマチュアの構築
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
- [ ] パス設定の設定ファイル化（GamePaths.cs のハードコード除去）
- [ ] デバッグ表示/統計情報
- [ ] パフォーマンス最適化
- [ ] エラーハンドリングの強化

---

## 優先度（当面の目標）

1. **高優先度** - レンダリングの完全性
   - メガトンの地面がない問題（#10）
   - ライトの向きが正しくない問題（#11）
   - **街中の建築物の向きが合わない問題**（#12）— Flags uint32 修正後に Redot で再検証が必要
   - ノーマルマップ/スペキュラーマップの完全対応

2. **中優先度** - 拡張
   - 全レコードタイプ対応
   - 複数ワールドスペース
   - シャドウマップ

3. **低優先度** - ゲームプレイ
   - NPC/AI
   - UI/HUD
   - サウンド
   - アニメーション
   - クエスト

---

## 最近の変更履歴

### メッシュ向きバグ調査（直近セッション）
- **根本原因を特定**: `NIFBlockResolver.cs:374` の `Flags` が uint16 で読まれていた（正しくは uint32）
- AI アシスタント（Claude）が uint32 → uint16 に誤って変更し、2byte シフトで全 NiAVObject の Translation/Rotation/Scale が破損
- PyFFI で 3 つの NIF ファイル（`generichangingwire01.nif` 含む）を基準に検証 → 常に uint32 で余り=0 になることを確認
- `AI/verify_flags.py`, `verify_refr_math.py`, `verify_refr_math2.py`, `verify_full_pipeline.py` で数式を証明
- 修正後ビルド成功（0 エラー / 0 警告）
- **残課題**: Redot 上でメガトン街中の建築物の回転が合っているか視覚検証する（外壁は完璧）
- 補足: REFR 回転コード（`Megaton.cs` CreateAndAddInstance）は相似変換として数学的に正しいことを確認済み
