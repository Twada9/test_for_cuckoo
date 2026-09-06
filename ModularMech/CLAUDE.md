# ModularMech — 実装規約 (v1)

Unity 6 (URP) / C# のモジュラー機体ゲーム。仕様の唯一の出典は `docs/modular-mech-design-v1.md`。
本ファイルは、その仕様を実装するときの**共有コントラクト**である。複数のエージェントが並行して
書くため、ここに書かれた公開シグネチャは合意事項として扱い、**勝手に変えない**。
変えたい場合は、変更点を報告に明記すること。

## 環境の制約(重要)

このコンテナには .NET / Mono / Unity が無く、プロキシの都合で取得もできない。
**C# をコンパイル・実行して検証することはできない。**
- 「ビルドが通った」「テストがパスした」と書いてはいけない。
- 検証は「コードを読み返しての目視確認」と `Tools/check_sources.py` の静的チェックまで。
- 未検証であることは報告に必ず書く。

## ディレクトリ

設計ドキュメント §8 に従う。`Assets/Scripts/{Data,Loadout,Assembly,Runtime,UI,Serialization,Editor}`、
テストは `Assets/Tests/EditMode`。`.meta` ファイルは作らない(Unity が生成する)。

## 名前空間

| ディレクトリ | 名前空間 |
|---|---|
| Scripts/Data | `ModularMech.Data` |
| Scripts/Loadout | `ModularMech.Loadouts` (※クラス `Loadout` と衝突するため複数形) |
| Scripts/Serialization | `ModularMech.Serialization` |
| Scripts/Assembly | `ModularMech.Assembling` (※ `System.Reflection.Assembly` 連想を避ける) |
| Scripts/Runtime | `ModularMech.Mechs` (※ `ModularMech.Runtime` は asmdef 名と紛らわしいため) |
| Scripts/UI | `ModularMech.UI` |
| Scripts/Editor | `ModularMech.EditorTools` |
| Tests | `ModularMech.Tests` |

## 設計原則(逸脱禁止)

1. パーツは見た目と能力の単一データソース。装備で両方が同時に変わる。
2. コントローラは `CapabilityFlags` と `ILocomotionProfileData` を問い合わせるだけ。
   **パーツ ID / パーツ種別で分岐しない。** 新パーツ追加でコントローラが変わるなら設計ミス。
3. ステータス補正は **加算のみ**(`PartStats.operator +`)。乗算はペナルティ適用時の
   `SpeedMultiplier` / `AccelerationMultiplier` / `TurnSpeedMultiplier` だけで、集約の後段に置く。
4. `Torso` / `Legs` は必須。過積載・パワー不足は**拒否せず、警告+ペナルティで許可**。
   拒否するのは SlotMismatch と HandRequiresArm のみ。
5. 移動方式は `ILocomotionStrategy` に切り出し、`LocomotionType` から解決する。
6. リビルドは差分。全体リビルドは `Legs` 変更時のみ。
7. 欠損 ID / 欠損パーツで例外を投げない。警告を積んで安全側に倒す(§7)。
8. `UnityEngine.JsonUtility` は Dictionary を扱えないので使わない。`ModularMech.Serialization.MiniJson` を使う。
9. 純粋ロジック(集約/検証/ペナルティ/シリアライズ)は `UnityEngine` に依存させない。
   ScriptableObject ではなくインターフェース越しに扱い、EditMode テストから直接叩けるようにする。

## 共有コントラクト(確定済みシグネチャ)

### ModularMech.Data — 実装済み(変更禁止)

```csharp
public enum PartSlot { Head, Torso, ArmLeft, ArmRight, Legs, Backpack, HandLeft, HandRight }

public static class PartSlots {
    public static readonly PartSlot[] All;             // 宣言順
    public static bool IsRequired(PartSlot slot);      // Torso / Legs
    public static bool TryGetRequiredArm(PartSlot handSlot, out PartSlot armSlot);
}

[Flags] public enum CapabilityFlags { None=0, Walk=1<<0, Run=1<<1, Jump=1<<2, Hover=1<<3,
                                      Dash=1<<4, Crouch=1<<5, GrabLeft=1<<6, GrabRight=1<<7, Emote=1<<8 }
public static class CapabilityFlagsExtensions {
    public static bool Has(this CapabilityFlags value, CapabilityFlags flag);  // HasFlag のボックス化回避。必ずこちらを使う
}

[Serializable] public struct PartStats {
    public float weight, powerOutput, powerDraw, moveSpeedMod, turnSpeedMod, jumpPowerMod, stability;
    public static readonly PartStats Zero;
    public static PartStats operator +(PartStats a, PartStats b);   // 全フィールド加算
}

public enum LocomotionType { Biped, Quadruped, Hover, Tracked }

public interface ILocomotionProfileData {
    LocomotionType Type { get; }
    float BaseMoveSpeed { get; } float BaseTurnSpeed { get; } float BaseJumpPower { get; }
    float Acceleration { get; } float GroundOffset { get; } float WeightCapacity { get; }
}

public interface IPartData {
    string PartId { get; } PartSlot Slot { get; } PartStats Stats { get; }
    CapabilityFlags GrantedCapabilities { get; }
    ILocomotionProfileData LocomotionProfileData { get; }   // Legs 以外は null
}

public interface IPartCatalog { bool TryGet(string partId, out IPartData part); }
```

### ModularMech.Data — 未実装(M1 担当)

```csharp
[CreateAssetMenu(menuName = "Mech/Part Definition")]
public sealed class PartDefinition : ScriptableObject, IPartData { /* 設計ドキュメント §2.2 のフィールドをそのまま public に持つ */ }

[CreateAssetMenu(menuName = "Mech/Locomotion Profile")]
public sealed class LocomotionProfile : ScriptableObject, ILocomotionProfileData { /* §2.5 のフィールド */ }

public enum AttachmentMode { SkinnedToSharedRig, RigidToBone }

[CreateAssetMenu(menuName = "Mech/Part Catalog")]
public sealed class PartCatalog : ScriptableObject, IPartCatalog {
    bool TryGet(string partId, out IPartData part);
    IReadOnlyList<PartDefinition> PartsForSlot(PartSlot slot);
    IReadOnlyList<PartDefinition> AllParts { get; }
}
```

### ModularMech.Loadouts

```csharp
public enum EquipError { None, SlotMismatch, MissingRequiredArm, NullPart }

[Serializable] public sealed class Loadout {
    public string Name { get; set; }
    public IReadOnlyDictionary<PartSlot, string> Equipped { get; }
    public event Action<PartSlot> SlotChanged;                 // UI の差分更新用
    public string GetPartId(PartSlot slot);                    // 未装備は null
    public bool TryEquip(PartSlot slot, IPartData part, out EquipError error);
    public void Unequip(PartSlot slot);                        // 腕を外すと対応する手も外れる
    public Loadout Clone();
    public LoadoutValidation Validate(IPartCatalog catalog);   // LoadoutValidator への委譲
}

public sealed class ResolvedLoadout {
    public IReadOnlyDictionary<PartSlot, IPartData> Parts { get; }
    public IReadOnlyList<string> MissingPartIds { get; }       // カタログに無かった ID
    public IPartData Torso { get; }  public IPartData Legs { get; }   // 無ければ null
    public bool TryGet(PartSlot slot, out IPartData part);
    public IEnumerable<IPartData> All { get; }
}
public static class LoadoutResolver { public static ResolvedLoadout Resolve(Loadout loadout, IPartCatalog catalog); }

public enum ValidationSeverity { Warning, Error }
public enum ValidationCode { MissingTorso, MissingLegs, UnknownPartId, SlotMismatch,
                             HandWithoutArm, PowerBudgetExceeded, WeightCapacityExceeded }
public readonly struct ValidationIssue {
    public ValidationCode Code { get; } public ValidationSeverity Severity { get; }
    public PartSlot Slot { get; } public bool HasSlot { get; } public string Message { get; }
}
public sealed class LoadoutValidation {
    public IReadOnlyList<ValidationIssue> Issues { get; }
    public bool IsDeployable { get; }   // Error が 0 件
    public bool HasWarnings { get; }
}
public static class LoadoutValidator {
    public static LoadoutValidation Validate(Loadout loadout, IPartCatalog catalog);
    public static LoadoutValidation Validate(ResolvedLoadout resolved);
}
```

### ModularMech.Mechs

```csharp
public readonly struct StatBlock {
    public PartStats Raw { get; }
    public float TotalWeight { get; } public float TotalPowerOutput { get; } public float TotalPowerDraw { get; }
    public float WeightCapacity { get; } public float OverweightRatio { get; } public float PowerRatio { get; }
    public float SpeedMultiplier { get; } public float AccelerationMultiplier { get; } public float TurnSpeedMultiplier { get; }
    public CapabilityFlags Capabilities { get; }       // ペナルティ適用後
    public bool IsOverweight { get; } public bool IsUnderpowered { get; }
}

public static class StatAggregator { public static StatBlock Aggregate(ResolvedLoadout resolved); }
```

**ペナルティ計算(設計ドキュメント §3.3 を厳密に踏襲。式を変えない)**

```
overweightRatio = totalWeight / legs.WeightCapacity
  if (ratio > 1) { speedMul *= Lerp(1, 0.4, (ratio-1)/0.5); caps &= ~Run; if (ratio > 1.5) caps &= ~Jump; }
powerRatio = totalOutput / totalDraw
  if (ratio < 1) { accelMul *= ratio; turnMul *= ratio; }
```
ゼロ除算の扱い(実装が決めた安全側の既定。テストもこれに従う):
- `WeightCapacity <= 0`(脚が無い / 容量未設定)→ `OverweightRatio = 0`、ペナルティ無し。
- `TotalPowerDraw <= 0` → `PowerRatio = 1`、ペナルティ無し。
- `Lerp` は t をクランプするので `ratio >= 1.5` では速度倍率は 0.4 で下げ止まる。

### ModularMech.Assembling / ModularMech.Mechs (MonoBehaviour 層)

```csharp
public sealed class MechAssembly : MonoBehaviour {
    public void Rebuild(Loadout loadout, PartCatalog catalog);   // 内部で前回状態と差分比較
    public void RebuildAll(Loadout loadout, PartCatalog catalog);// Legs 変更時
    public IReadOnlyDictionary<PartSlot, GameObject> SpawnedParts { get; }
}

public sealed class MechRuntime : MonoBehaviour {
    public StatBlock CurrentStats { get; }
    public CapabilityFlags Capabilities { get; }
    public ILocomotionProfileData Locomotion { get; }
    public LoadoutValidation Validation { get; }
    public Loadout ActiveLoadout { get; }
    public event Action LoadoutApplied;
    public void Apply(Loadout loadout, PartCatalog catalog);
}

public struct MechInputState { public Vector2 move; public bool sprint, jump, crouch, emote; }

public interface ILocomotionStrategy {
    LocomotionType Type { get; }
    void Enter(MechLocomotionContext ctx);
    void Tick(MechLocomotionContext ctx, in MechInputState input, float deltaTime);
    void Exit(MechLocomotionContext ctx);
}
```

### ModularMech.Serialization

```csharp
public static class MiniJson {
    public static object Deserialize(string json);   // Dictionary<string,object> / List<object> / string / double / bool / null
    public static string Serialize(object value, bool pretty = false);
}
```

```csharp
// ModularMech.Loadouts
public sealed class LoadoutLoadResult {
    public bool Success { get; } public List<Loadout> Loadouts { get; }
    public int ActiveIndex { get; } public IReadOnlyList<string> Warnings { get; }
}
public static class LoadoutSerializer {
    public const int CurrentVersion = 1;
    public static string Serialize(IReadOnlyList<Loadout> loadouts, int activeIndex, bool pretty = true);
    public static LoadoutLoadResult Deserialize(string json, IPartCatalog catalog);  // 未知 ID はスロットを空にして警告
}
```
JSON の形は設計ドキュメント §7 に一致させる(`version` / `loadouts[].name` / `loadouts[].parts` / `activeLoadoutIndex`)。
未装備スロットは `null` を書き出す。

## コーディング規約

- 1ファイル1公開型。ファイル名 = 型名。
- `CapabilityFlags.HasFlag` は使わず `Has()` 拡張メソッド。
- `Update()` 内で `GetComponent` / LINQ / 新規アロケーションをしない。参照は `Awake` でキャッシュ。
- public フィールドは ScriptableObject / Inspector 露出のみ。ロジック型はプロパティ。
- float の等値比較をしない。閾値比較は `>` / `<` を設計ドキュメントの式どおりに。
- コメントは「なぜ」を書く。「何を」はコードで示す。日本語コメント可。

---

## 追加確定事項(アドバイザリ反映 / v1 の裁定)

設計ドキュメントに書かれていない、または解釈が割れる点をここで確定させる。
**以下は仕様と同じ強さで扱う。**

### D-1. アタッチ情報の出典は `PartAttachment`(プレハブ側)

設計ドキュメント §4.1 の `AttachRigid(instance, boneName)` に渡す `boneName` の供給源を確定する。

- **唯一の出典は、パーツプレハブのルートに載る `ModularMech.Assembling.PartAttachment`。**
  取り付け先ボーン名と、位置/回転/スケールのオフセットを持つ(§10「めり込み/浮き」対策)。
- `PartAttachment` が無い、またはボーン名が空のときは、**スロット既定のソケット名**にフォールバックする。
  既定名の表は `MechAssembly` 側に1箇所だけ置く。これはスロットの取り付け規約であって
  パーツ種別による分岐ではない(設計原則2に違反しない)。
- `PartDefinition` にはアタッチ情報を持たせない。データを二重化すると、どちらが勝つかの規則が必要になり、
  アセット差し替え時に必ず食い違うため。

### D-2. ボーン解決の失敗で例外を投げない

設計ドキュメント §4.1 のコード片は `boneMap[name]` の直接インデックスで書かれているが、
**そのまま実装しない**。ボーン名の不一致は §10 が筆頭に挙げた最も起きやすい失敗であり、
設計原則7(例外を投げず安全側に倒す)が優先される。

- 必ず `TryGetValue`。解決できないボーンは元の Transform を残す。
- `rootBone` が解決できない場合はそのパーツの生成を諦め、警告1件を出して**他のパーツは通常どおり組む**。
- 途中で失敗しても `SpawnedParts` の辞書と実際の階層が食い違わないこと。

### D-3. AnimatorOverrideController は「脚由来の1枚だけ」

`AnimatorOverrideController` はベースコントローラのクリップを鍵にした差し替え表であり、
脚の AOC を適用した後に腕の AOC を重ねても、鍵が一致せず**黙って無効になる**。

- v1 で適用する AOC は `LocomotionProfile.animatorSet` の1枚のみ。
- `PartDefinition.animatorOverride` は **v1 未使用フィールド**。フィールドは仕様どおり残すが、
  どのコードからも読まない。読む実装を足さないこと。
- 腕パーツの見た目差はプレハブ形状で表現する。§11 の完了条件に腕モーションは含まれない。

### D-4. `PartStats.stability` は v1 の予約フィールド

集約(加算)はするが、**どのコードも読まない**。

理由: 消費者を決めないまま各所で式を発明すると、設計原則3(補正は加算のみ、乗算はペナルティのみ)を
悪意なく破る。`stability` は加算合計なので、素朴に加速度の除数などに使うと
「重装ほど機敏になる」逆転が起きる。v1 では消費者を作らない。

### D-5. 未実装の `LocomotionType` は Biped にフォールバック

`LocomotionType` は4値あるが、v1 で挙動を作り込むのは `Biped` と `Hover`(§11 完了条件5)。
`LocomotionStrategyRegistry` が実装を見つけられない場合は、
**接地系(Biped)戦略にフォールバックし、警告を1件出す。** null を返して無反応にしない。

理由: ペナルティで `SpeedMultiplier` が 0.4 まで落ちる仕様があるため、「動かない」の原因候補が多い。
無反応を作ると切り分けが不能になる。

### D-6. リビルドはフレーム末に1回へ集約する

`Loadout.SlotChanged` は**UI の差分更新専用**。腕を外すと連動する手も外れるため、
1回のユーザー操作で複数回発火することが確定している。

- `SlotChanged` を購読して直接 `MechRuntime.Apply()` を呼ばない。
- リビルド要求はダーティフラグに立て、`LateUpdate` で1回だけ実行する。
- 理由: `Apply` は `animator.runtimeAnimatorController` の再代入(= ステートマシンのリセット)を含むため、
  多重実行すると T ポーズが一瞬見える。「即時反映」の体感品質はここで決まる。

### D-7. root motion は常に無効。接地は戦略が申告する

- 移動は完全にスクリプト駆動。`Animator.applyRootMotion` は常に false。
  アニメーションは見た目だけで、位置を動かさない。
- ベースステートマシンの接地パラメータは、レイキャストではなく **`ILocomotionStrategy` が書き込む**。
  Hover 戦略は常に「接地している」と申告して、落下/着地ループに落ちるのを防ぐ。
  ステートマシンに嘘をつくのは、v1 では正しい先送り。

### D-8. §11 完了条件4「走行不可」の定義

> 4. 過積載時に警告が出て、実際に走行不可になる

**「走行不可」= `CapabilityFlags.Run` の剥奪であり、移動そのものは可能。**
§3.3 の式が実際に行うのは `caps &= ~Run`、速度倍率の低下、1.5倍超で `caps &= ~Jump` の3つ。
過積載でも歩行はできる。テストはこの解釈で固定すること(「過積載なら速度0」は誤り)。

### D-9. UI の表示規約(過積載・パワー不足の見せ方)

ペナルティが不可視だと、プレイヤーには不具合に見える。最低限これだけは満たす。

- **速度**: ペナルティ適用後の実効値を主表示、基礎値を副表示。減っていることが一目で分かること。
- **重量**: `総重量 / 上限`。100% を超えて伸びるバーで超過分の色を変える。
  `WeightCapacity <= 0`(脚未装備等)は「—」と表示する。**0% と表示してはいけない**(上限無制限に見える)。
- **電力**: `出力 / 消費`。比率 1.0 未満で色を変える。
- **能力アイコン**: 「そもそも付与されていない」と「付与されたがペナルティで剥奪された」を
  別の見た目にする(後者は取り消し線など)。剥奪を無表示にすると、脚が壊れているように見える。
- **警告行**: `ValidationIssue.Message` を1件1行、`ValidationSeverity` で色分け。
  `IsDeployable == false` のときだけ出撃を無効化する。

### D-10. `Validate` の結果はキャッシュする

`LoadoutValidator.Validate` は `List<ValidationIssue>` と各 `Message` 文字列を新規生成する。
`Update()` から毎フレーム呼ぶと GC を生む(コーディング規約の「Update 内で割り当てない」に正面から違反)。
検証結果は `MechRuntime.Validation` に保持し、`LoadoutApplied` のときだけ作り直す。

### D-11. `Crouch` / `Dash` は v1 ではアニメーションのみ

クールダウン・コスト・コライダーのリサイズを実装しない。
リソース管理と状態機械は戦闘システムの半分であり、v1 スコープ外(設計ドキュメント §0)。

### D-12. `GrabLeft` / `GrabRight` に v1 で意味を与えない

手持ちスロットのバリデーション(`HandRequiresArm`)は腕の有無だけを見る。
`GrabLeft` / `GrabRight` フラグを読む実装を作らない。
読ませた瞬間に「手に持ったものを使う」= アクション実行層が必要になり、スコープが壊れる。
手持ち物は手ボーンにオフセット付きで剛体固定。IK は使わない(Animation Rigging を導入しない)。
