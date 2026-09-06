# モジュラー機体ゲーム 設計ドキュメント (v1)

小型ロボットのパーツ換装をコアにしたゲームの、初期スコープ設計。
戦闘は含まず、**「パーツを組み替える」**と**「パーツ構成に応じて挙動が変わる」**の2点のみを対象とする。

---

## 0. 前提

| 項目 | 決定 |
|---|---|
| エンジン | Unity 6 (URP) |
| 言語 | C# |
| 対象プラットフォーム | PC (開発中はEditorのみで完結させる) |
| 3Dアセット | 購入アセット + プリミティブのプレースホルダで開始。自作モデリングは行わない |
| アニメーション | Mixamo / アセット付属のクリップを流用 |
| ネットワーク | なし |
| セーブ | JSON (ローカルファイル) |

### スコープ外(v1では作らない)
- 戦闘、ダメージ計算、敵
- ミッション / ステージ進行
- ショップ、経済、アンロック
- マルチプレイ

---

## 1. コアコンセプト

```
PartDefinition (データ)  →  Loadout (組み合わせ)  →  MechAssembly (実体)
                                    ↓
                        StatBlock + CapabilitySet
                                    ↓
                   LocomotionProfile / AnimationSet / ActionSet
```

**設計の中心思想:**
パーツは「見た目」と「能力」の両方を持つ単一のデータソースであり、
装備した瞬間に**見た目の変化と挙動の変化が同時に起きる**。
この2つを別々のシステムにしないことが、このゲームの実装上の一番の要。

---

## 2. パーツシステム

### 2.1 スロット定義

```csharp
public enum PartSlot {
    Head,
    Torso,      // 必須。他スロットの親となる基準
    ArmLeft,
    ArmRight,
    Legs,       // 必須。移動方式を決定する
    Backpack,   // オプション
    HandLeft,   // 手持ち装備(v1では非戦闘オブジェクトのみ)
    HandRight,
}
```

- `Torso` と `Legs` は **必須スロット**。欠けた状態は不正なLoadoutとして扱う
- それ以外は `null` を許容し、その場合は該当メッシュを非表示にする

### 2.2 PartDefinition (ScriptableObject)

```csharp
[CreateAssetMenu(menuName = "Mech/Part Definition")]
public sealed class PartDefinition : ScriptableObject {
    [Header("Identity")]
    public string partId;           // 一意。セーブデータのキーになる
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;
    public PartSlot slot;

    [Header("Visual")]
    public GameObject meshPrefab;   // SkinnedMeshRenderer または MeshRenderer を持つ
    public AttachmentMode attachmentMode;

    [Header("Stats")]
    public PartStats stats;

    [Header("Behavior")]
    public CapabilityFlags grantedCapabilities;
    public LocomotionProfile locomotionProfile;   // Legs スロットのみ有効
    public AnimatorOverrideController animatorOverride; // 任意
}
```

```csharp
public enum AttachmentMode {
    SkinnedToSharedRig,  // 共通スケルトンにウェイト付き(胴・脚など変形するもの)
    RigidToBone,         // ボーンに剛体として親子付け(頭・バックパック・武器など)
}
```

### 2.3 PartStats

```csharp
[Serializable]
public struct PartStats {
    public float weight;          // 総重量に加算
    public float powerOutput;     // Torso が供給、他パーツが消費
    public float powerDraw;
    public float moveSpeedMod;    // 加算補正
    public float turnSpeedMod;
    public float jumpPowerMod;
    public float stability;       // 高いほど加減速が緩やか

    public static PartStats operator +(PartStats a, PartStats b) { /* 各フィールド加算 */ }
}
```

### 2.4 CapabilityFlags

パーツが「何をできるようにするか」を表すビットフラグ。
挙動の分岐は基本的にこのフラグを見る。

```csharp
[Flags]
public enum CapabilityFlags {
    None      = 0,
    Walk      = 1 << 0,
    Run       = 1 << 1,
    Jump      = 1 << 2,
    Hover     = 1 << 3,   // Backpack / Legs が付与
    Dash      = 1 << 4,
    Crouch    = 1 << 5,
    GrabLeft  = 1 << 6,   // ArmLeft がないと手持ちできない
    GrabRight = 1 << 7,
    Emote     = 1 << 8,
}
```

### 2.5 LocomotionProfile

`Legs` パーツが持つ移動方式の定義。ScriptableObject。

```csharp
public enum LocomotionType { Biped, Quadruped, Hover, Tracked }

[CreateAssetMenu(menuName = "Mech/Locomotion Profile")]
public sealed class LocomotionProfile : ScriptableObject {
    public LocomotionType type;
    public float baseMoveSpeed;
    public float baseTurnSpeed;
    public float baseJumpPower;
    public float acceleration;
    public float groundOffset;              // 接地高さ(ホバー脚は浮く)
    public AnimatorOverrideController animatorSet;
    public float weightCapacity;            // これを超えると過積載
}
```

**これが「パーツに合わせた行動」の主役。**
二脚 → 歩行アニメ+ジャンプ可、ホバー脚 → 滑るような移動+ジャンプ不可+常時浮遊、
のように**脚パーツを変えるだけで移動そのものが別物になる**。

---

## 3. Loadout(構成)

### 3.1 データ

```csharp
[Serializable]
public sealed class Loadout {
    public Dictionary<PartSlot, string> equipped; // slot -> partId

    public bool TryEquip(PartSlot slot, PartDefinition part, out EquipError error);
    public void Unequip(PartSlot slot);
    public LoadoutValidation Validate(PartCatalog catalog);
}
```

### 3.2 バリデーション

装備時に以下を検証する。エラーはUIに理由付きで表示する。

| チェック | 内容 | 失敗時の扱い |
|---|---|---|
| SlotMatch | `part.slot == slot` か | 装備不可(拒否) |
| RequiredSlots | Torso / Legs が存在するか | 不正Loadout、出撃不可 |
| PowerBudget | `Σ powerDraw <= Σ powerOutput` | **警告**。装備は可能だが性能低下 |
| WeightCapacity | `Σ weight <= legs.weightCapacity` | **警告**。過積載ペナルティ |
| HandRequiresArm | HandLeft装備時に ArmLeft が存在するか | 装備不可(拒否) |

**設計判断:** パワー超過と過積載は「拒否」ではなく「ペナルティ付きで許可」にする。
プレイヤーが極端な構成を試せる余地を残すため。

### 3.3 ペナルティ計算

```csharp
// 過積載: 超過率に応じて速度低下
float overweightRatio = totalWeight / legs.weightCapacity;
if (overweightRatio > 1f) {
    speedMultiplier *= Mathf.Lerp(1f, 0.4f, (overweightRatio - 1f) / 0.5f);
    capabilities &= ~CapabilityFlags.Run;   // 走行不可
    if (overweightRatio > 1.5f) capabilities &= ~CapabilityFlags.Jump;
}

// パワー不足: 全体的な反応速度低下
float powerRatio = totalOutput / totalDraw;
if (powerRatio < 1f) {
    accelerationMultiplier *= powerRatio;
    turnSpeedMultiplier *= powerRatio;
}
```

---

## 4. 組み立て(MechAssembly)

### 4.1 骨格の共有

すべてのパーツは**同一のスケルトン階層**を前提とする。
Torsoプレハブがルートスケルトンを持ち、他パーツはそこにアタッチされる。

```csharp
public sealed class MechAssembly : MonoBehaviour {
    [SerializeField] Transform skeletonRoot;
    Dictionary<string, Transform> boneMap;   // ボーン名 -> Transform (起動時に構築)
    Dictionary<PartSlot, GameObject> spawnedParts;

    public void Rebuild(Loadout loadout, PartCatalog catalog);
    void AttachSkinned(GameObject instance);  // bones配列をboneMapで張り替え
    void AttachRigid(GameObject instance, string boneName);
}
```

**SkinnedMeshRenderer の張り替え(換装の核心):**

```csharp
void AttachSkinned(GameObject instance) {
    var smr = instance.GetComponentInChildren<SkinnedMeshRenderer>();
    var newBones = new Transform[smr.bones.Length];
    for (int i = 0; i < smr.bones.Length; i++) {
        newBones[i] = boneMap[smr.bones[i].name];  // 名前で共通スケルトンに解決
    }
    smr.bones = newBones;
    smr.rootBone = boneMap[smr.rootBone.name];
    instance.transform.SetParent(skeletonRoot.parent, false);
}
```

> **重要な制約:** 全パーツのボーン名・バインドポーズが一致していること。
> アセット購入時はこの点を必ず確認する。ここが崩れると換装システム全体が成立しない。

### 4.2 リビルドのタイミング

- Loadout変更時に**該当スロットのみ**破棄・再生成する(全体リビルドはしない)
- ただし `Legs` 変更時のみ、LocomotionProfileが変わるため全体を再初期化する

---

## 5. 挙動への反映

### 5.1 集約フロー

```csharp
public sealed class MechRuntime : MonoBehaviour {
    public StatBlock CurrentStats { get; private set; }
    public CapabilityFlags Capabilities { get; private set; }
    public LocomotionProfile Locomotion { get; private set; }

    public event Action LoadoutApplied;

    public void Apply(Loadout loadout, PartCatalog catalog) {
        var parts = catalog.Resolve(loadout);
        CurrentStats  = parts.Aggregate(PartStats.Zero, (acc, p) => acc + p.stats);
        Capabilities  = parts.Aggregate(CapabilityFlags.None, (acc, p) => acc | p.grantedCapabilities);
        Locomotion    = parts[PartSlot.Legs].locomotionProfile;
        ApplyPenalties();
        assembly.Rebuild(loadout, catalog);
        animator.runtimeAnimatorController = Locomotion.animatorSet;
        LoadoutApplied?.Invoke();
    }
}
```

### 5.2 移動コントローラ

```csharp
public sealed class MechLocomotionController : MonoBehaviour {
    // MechRuntime の値を毎フレーム参照して動く。
    // 自身は「どう動けるか」を一切ハードコードしない。

    void Update() {
        var caps = runtime.Capabilities;
        var input = ReadInput();

        if (!caps.HasFlag(CapabilityFlags.Walk) && !caps.HasFlag(CapabilityFlags.Hover)) return;

        float speed = runtime.Locomotion.baseMoveSpeed + runtime.CurrentStats.moveSpeedMod;
        speed *= runtime.SpeedMultiplier;
        if (input.sprint && caps.HasFlag(CapabilityFlags.Run)) speed *= runSpeedRatio;

        switch (runtime.Locomotion.type) {
            case LocomotionType.Hover:   MoveHover(input, speed);  break;
            case LocomotionType.Tracked: MoveTracked(input, speed); break;
            default:                     MoveGrounded(input, speed); break;
        }

        if (input.jump && caps.HasFlag(CapabilityFlags.Jump)) Jump();
    }
}
```

**原則:** コントローラは能力を問い合わせるだけで、パーツの種類を直接知らない。
新しいパーツを追加してもコントローラのコードは変更不要。

### 5.3 アニメーション

- ベースとなる `AnimatorController` は1つだけ作る(Idle / Locomotion / Jump / Emote のステート)
- `Legs` の `LocomotionProfile.animatorSet` で **AnimatorOverrideController** を差し替え、クリップだけ入れ替える
- 腕パーツの `animatorOverride` は上半身レイヤーのクリップを上書きする(任意)

これにより、ステートマシンを1つ保守するだけで移動方式ごとの見た目を切り替えられる。

---

## 6. UI(組み立て画面)

v1で必要な画面は2つのみ。

### 6.1 ガレージ画面
- 左: スロット一覧(8スロット、装備中パーツ名とアイコン)
- 中央: 機体の3Dプレビュー(回転可能、装備変更が即座に反映)
- 右: 選択中スロットに装備可能なパーツ一覧
- 下: ステータスパネル(総重量 / 重量上限、消費電力 / 出力、速度、能力アイコン)
- 警告表示: 過積載・パワー不足を色付きで明示

### 6.2 テスト走行画面
- 平坦な地形のみ。障害物として段差・スロープを数個置く
- 組んだ機体を実際に動かして挙動差を体感する
- ガレージへ即座に戻れる

---

## 7. 保存

```json
{
  "version": 1,
  "loadouts": [
    {
      "name": "デフォルト機",
      "parts": {
        "Torso": "torso_light_01",
        "Head": "head_sensor_01",
        "ArmLeft": "arm_standard_01",
        "ArmRight": "arm_standard_01",
        "Legs": "legs_biped_01",
        "Backpack": null
      }
    }
  ],
  "activeLoadoutIndex": 0
}
```

- パーツは**IDのみ**保存する。定義本体は `PartCatalog` から解決する
- ロード時に存在しないIDがあれば、そのスロットを空にして警告を出す(アセット差し替えへの耐性)

---

## 8. ディレクトリ構成

```
Assets/
├── Scripts/
│   ├── Data/
│   │   ├── PartDefinition.cs
│   │   ├── PartStats.cs
│   │   ├── CapabilityFlags.cs
│   │   ├── LocomotionProfile.cs
│   │   └── PartCatalog.cs
│   ├── Loadout/
│   │   ├── Loadout.cs
│   │   ├── LoadoutValidator.cs
│   │   └── LoadoutSerializer.cs
│   ├── Assembly/
│   │   ├── MechAssembly.cs
│   │   └── BoneMapper.cs
│   ├── Runtime/
│   │   ├── MechRuntime.cs
│   │   ├── MechLocomotionController.cs
│   │   └── StatAggregator.cs
│   └── UI/
│       ├── GarageScreen.cs
│       ├── SlotListView.cs
│       ├── PartListView.cs
│       └── StatPanelView.cs
├── ScriptableObjects/
│   ├── Parts/
│   └── Locomotion/
├── Prefabs/
│   ├── Mech/
│   └── Parts/
└── Scenes/
    ├── Garage.unity
    └── TestField.unity
```

---

## 9. 実装マイルストーン

Claude Codeに渡す際は、この順で1つずつ進める。

**M1: データ基盤**
`PartDefinition` / `PartStats` / `CapabilityFlags` / `LocomotionProfile` / `PartCatalog` を実装。
プリミティブ(Cube/Capsule)でプレースホルダパーツを各スロット2〜3種作る。

**M2: 組み立て**
`MechAssembly` によるパーツの生成・破棄・ボーン張り替え。
まずは `RigidToBone` のみ実装し、Skinnedは後回しでよい。
Editorスクリプトから直接Loadoutを差し替えて見た目が変わることを確認する。

**M3: ステータス集約**
`StatAggregator` と `LoadoutValidator`。過積載・パワー不足の計算とペナルティ適用。
UIなしでConsole出力で検証する。

**M4: 移動**
`MechLocomotionController`。まず `Biped` のみ。次に `Hover` を追加し、
**脚を変えると移動が変わる**ことを実証する。ここがv1の成立点。

**M5: アニメーション**
ベースAnimatorController + AnimatorOverrideControllerの差し替え。
Mixamoのクリップを使う。

**M6: ガレージUI**
スロット選択 → パーツ一覧 → 装備 → プレビュー即時反映 → ステータス表示。

**M7: 保存 / 復元**
JSONへの保存とロード。存在しないIDへのフォールバック処理。

**M8: 仕上げ**
テストフィールドの整備、購入アセットへの差し替え。

---

## 10. 想定される難所

| 難所 | 対策 |
|---|---|
| パーツ間でボーン名が一致しない | アセット購入前に必ず検証。自作分はBlenderでリグをコピーして使う |
| 装備の見た目がめり込む / 浮く | パーツ側にアタッチ用オフセットTransformを持たせ、微調整可能にする |
| 移動方式追加のたびにコントローラが肥大化 | `ILocomotionStrategy` に切り出し、`LocomotionType` から実装を解決する |
| ステータス補正が乗算・加算で混在して破綻 | v1は**加算のみ**に統一。乗算補正は導入しない |
| プレビューのリビルドが重い | スロット単位の差分更新。全体リビルドは `Legs` 変更時のみ |

---

## 11. v1完了の定義

以下がすべて満たされた時点でv1完了とする。

1. ガレージ画面で全スロットのパーツを任意に入れ替えられる
2. 入れ替えが3Dプレビューに即座に反映される
3. 総重量・電力・能力がリアルタイムに再計算され表示される
4. 過積載時に警告が出て、実際に走行不可になる
5. 二脚 / ホバーの脚を入れ替えると、テストフィールドでの移動挙動が明確に変わる
6. 構成を保存し、再起動後に復元できる

---

## 補記: アセットについて

購入アセットやMixamoのモーションを使う場合、それぞれのライセンス範囲(個人利用 / 商用 / 再配布可否)を導入時点で確認しておくこと。
また既存作品をモチーフにする場合、機体デザインやパーツ名をそのまま流用すると公開時に問題になり得るため、
仕組みは参考にしつつビジュアルと固有名詞はオリジナルで作るのが安全。
