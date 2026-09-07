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
    // 冪等。Start の実行順が保証されないため、MechRuntime.Start と GarageScreen の両方から呼ぶ(D-16)
    public void EnsureDefaultLoadoutApplied();
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

### D-13. `UnknownPartId` は Error(`IsDeployable = false`)

設計ドキュメント §3.2 の表に行が無く、原則4「拒否するのは SlotMismatch と HandRequiresArm のみ」だけを
読むと警告寄りに見えるため、ここで確定させる。

- `LoadoutResolver` は §7 どおり、欠損 ID のスロットを空にして**例外を投げずに**進む(状態の保持)。
- `LoadoutValidator` は同じ状態を **Error** として報告する(気づかせる役割)。

理由: 欠損 ID を含む構成は「壊れてはいない」が、プレイヤーが保存した構成とは別物になっている。
Warning にすると気づかないまま出撃でき、アセット差し替えによるデータ欠損の発覚が遅れる。
「落ちない」は Resolver の責務、「気づかせる」は Validator の責務、という分担にする。

---

## 統合レビューを受けた裁定(D-14 以降)

### D-14. 移動戦略は速度・旋回・ジャンプの乗算スケールを持たない

設計ドキュメント §10 の「v1 は加算のみに統一。乗算補正は導入しない」と設計原則3 の趣旨は、
**プレイヤーに提示される数値の出典を一本化する**ことにある。

- `ILocomotionStrategy` の実装は `SpeedScale` / `TurnScale` / `JumpScale` のような
  **ステータス由来の値に掛かる乗算を持ってはならない**。
  移動方式ごとの速度差・旋回差は `LocomotionProfile` の数値(脚アセットごとに別)で表現する。
- 戦略が持ってよいのは「どう積分するか」の違いだけ:
  ホバーは目標速度ベクトルへ寄せる / 装軌は前後入力が小さいときその場旋回する /
  接地系は重力と接地押し付けを行う、など。
  加速・減速・空中制御の係数は積分の挙動であってステータス補正ではないため、戦略側に置いてよい。
  ただし**ステータス表示に現れる量(最高速・旋回速度・ジャンプ力)には掛けない**こと。
- 理由: `StatPanelView` は `(BaseMoveSpeed + moveSpeedMod) × SpeedMultiplier` を実効速度として
  主表示する(D-9)。戦略側に速度倍率があると、表示と実挙動が恒久的に食い違う。

### D-15. ペナルティ倍率の消費側に対称な下限を置く

`powerOutput = 0` かつ `powerDraw > 0` のとき `PowerRatio = 0` になり、
加速度と旋回速度が 0 倍になる。§3.3 の式は正しいので**式には手を入れない**。
消費側(`MechLocomotionController`)で下限を掛ける。

- `MinPenaltyFactor = 0.1f` を速度・加速・旋回に**対称に**適用する。
- 加速度にだけ下限があって旋回に無い、のような非対称を作らない
  (「這うように前進はするが永久に旋回できない」という切り分け不能な壊れ方をするため)。

### D-16. 初期化は `Start`。`OnEnable` は購読だけ

Unity は同一シーンで `Awake` → `OnEnable` → `Start` の順に回すため、
`OnEnable` で他コンポーネントの初期化済み状態に依存すると順序に負ける。

- 状態の初期化(既定 Loadout の構築・適用)は `Start` で行う。`OnEnable` はイベント購読のみ。
- ビュー(`SlotListView` / `PartListView` 等)のエントリ生成は `Awake` に置かず、
  **`EnsureEntries()` の遅延初期化**にして、公開メソッドの先頭から必ず呼ぶ。
  他オブジェクトの `Awake` より先に自分の公開メソッドが呼ばれても壊れないこと。

### D-17. ロード時の警告は UI に出す

`LoadoutSerializer` / `LoadoutSaveFile` が積む警告(「パーツがカタログに見つかりません」等)を
`Debug.LogWarning` で終わらせない。ステータスパネルの警告行に出し、プレイヤーが
「保存したはずのパーツが消えた」ことに気づけるようにする(§7 の意図、§11-6 の復元品質)。

D-13(`UnknownPartId` は Error)との関係を明確にしておく:
- **セーブ経路**では `LoadoutSerializer` が §7 どおりスロットを空にして警告する。
  この経路では `UnknownPartId` の Error は発火しない。
- D-13 の Error は**実行中にカタログを差し替えた場合の防御網**であり、通常経路では出ない。
  この二層構造を前提に、UI 側は Serializer の警告を必ず表示すること。

### D-18. 入力層も無反応を作らない(D-5 の入力版)

旧 Input Manager が無効(`ENABLE_LEGACY_INPUT_MANAGER` 未定義)のとき、
入力ソースが黙ってゼロを返し続けてはならない。**起動時に1回だけエラーを出す**。
Input System パッケージ導入時に「新しい入力バックエンドを有効にする」を選ぶと、
コンパイルは通ったまま機体が一切動かなくなり、ログも出ないため原因究明が極めて難しい。

### D-19. ガレージのプレビュー機体は操作させない

プレビュー用の機体では `MechLocomotionController` を無効化する。
有効なままだと、重力で落下する / `MechPreviewRotator` のドラッグ回転と
戦略のヨー回転が同じ Transform を奪い合う。

### D-20. `CharacterController` が無いときは接地扱い

`MechLocomotionContext.RefreshGrounded` は `Controller == null` のとき `IsGrounded = true` を返す。
物理無しでプレビューする用途を明示的に想定しているのに、接地判定だけが false を返すと
重力が積算されて無限落下する。D-7(ホバーは接地を申告する)と同じ考え方。

### D-21. ガレージ起動時に保存データを自動読込する

出撃時の自動保存(`SceneTransitionButton`)と、ガレージ起動時に既定構成を組む挙動を組み合わせると、
**保存済み構成が無言で破壊される**:

1. 構成 A を保存 → 2. 再起動 → 3. ガレージは既定構成で始まる(A ではない) →
4. プレイヤーが「読込」を押さずに「テスト走行へ」を押す → 自動保存で **A が上書き消滅**

§11-6「構成を保存し、再起動後に復元できる」が、最も自然な操作順で破れる。

- `GarageScreen` は起動時に `LoadoutSaveFile.Load` を試み、成功時はそれを作業中 Loadout にする。
  失敗・ファイル無しのときだけ既定構成へフォールバックする。
- `TestFieldScreen` が既に保存を自動読込しているので、**両画面で規則を対称にする**。

### D-22. 物理無しプレビューでもホバーは浮く

`Controller == null`(物理無しプレビュー)のとき鉛直速度を捨てるだけだと、
ホバー脚が `GroundOffset` まで永久に浮き上がらない。D-20 が明示的に想定した用途が満たせない。

- 非物理経路では、速度を積分するのではなく `Transform.position.y` を目標高度へ直接寄せる。
- 接地系は従来どおり鉛直成分を捨てる(無限落下を防ぐ D-20 の趣旨)。

### D-23. スプリント倍率は戦略に散らさない

`RunSpeedRatio` は設計ドキュメント §5.2 が明示した機体共通の設定であり、D-14 の対象外だが、
現状は同じ乗算が3つの戦略にコピーされている。新しい移動方式の実装者が書き忘れると
**スプリントだけが静かに効かなくなる**(D-5 / D-18 が嫌った「無反応」と同じ種類の失敗)。

- スプリント適用は `MechLocomotionController` 側で1回だけ行い、戦略には確定済みの速度を渡す。
- スプリント中の実速度はステータスパネルの主表示を超えるため、副表示に「走行時 ×N」を出す
  (D-9 の「表示と実効値が一致する」を維持するため)。

### D-24. 保存ファイル未存在の通知は、起動時の自動読込では出さない

D-21(起動時の自動読込)により、保存ファイルが一度も無い初回起動で必ず
「保存ファイルが見つかりません」の警告が1件出る。これは D-17 が本当に見せたい
「パーツが消えた」警告と同じ見た目・同じ場所に出るため、正常な初回起動のたびに
本物の警告が埋もれる形になる。

- `LoadoutLoadResult.FileNotFound` を区別できるようにする(セーブファイルが無かっただけで、
  壊れていたわけではないことを示す)。
- 起動時の自動読込(D-21)経路でこの状態のときは、コンソールには残すが
  ステータスパネルの警告行には出さない。
- 「読込」ボタンを明示的に押した結果としてのファイル無しは、押した本人へのフィードバックとして
  意味があるので、従来どおりパネルにも出す。

### D-25. `EditorSceneManager.NewScene` は、アセット参照の生成・読込より前に呼ぶ

`GarageSceneBuilder.BuildScene()` で、UI プレハブ/ScriptableObject を
`AssetDatabase.LoadAssetAtPath` 等で読み込んでから `EditorSceneManager.NewScene(...)` を
呼んでいたところ、**シーン切り替えの後でその参照が破棄済み扱いになる**実行時エラーが
実機で再現した(`SetRef` の null 代入警告が、シーン切り替え前に読み込んだ5つの
アセット全部から一斉に出た)。

原因はシーンの切り替えが、どこにも根を持たない(まだ SerializeField 等に代入されていない)
ロード済みアセット参照を Unity が暗黙に解放する契機になり得るため。

- **`NewScene` は必ず、そのシーン構築で使うアセットの生成・読込より前に呼ぶ。**
  読込んだ直後に使うのではなく、「まずシーンを確定させてから、そのシーンの中で
  必要なものを生成・読込む」の順序を守る。
- `TestFieldSceneBuilder` はこの順序を最初から守っていた(NewScene 後に BuildTerrain 等で
  マテリアルを作る)ため影響なし。

**追記(1回目の修正が不完全だった件)**: 上記の修正では UI プレハブ5点の生成・読込だけを
`NewScene` の後へ移したが、`GarageSceneBuilder.BuildScene()` / `TestFieldSceneBuilder.BuildScene()`
冒頭の `TryLoadPrerequisites(out PartCatalog catalog, out GameObject mechPrefab)` を見落としており、
**`catalog` と `mechPrefab` は依然として `NewScene` より前に読み込まれたままだった**。

実機で「エラーは無いが装備可能パーツ一覧が完全に空(『装備しない』すら出ない)」という形で再現した
(`PartListView.Show` は `catalog == null` だと何もログを出さず全消去するため、無言の空欄になる)。
`GarageScreen.partCatalog` に代入されていた参照が、他の5点と同じ理由で破棄済み扱いになっていた。

**教訓: 「既存のため再利用するプレハブ」だけが対象ではない。`AssetDatabase.LoadAssetAtPath` で
読んだアセットは、たとえルートレベルの ScriptableObject / プレハブであっても、
`NewScene` をまたいで保持してはならない。** 前提条件の検証(存在するか)は `NewScene` より前に
行ってよいが、その戻り値の参照は使い捨てにし、実際に使う参照は `NewScene` の直後に
`AssetDatabase.LoadAssetAtPath` で読み直すこと。両ビルダーとも、この読み直しを追加して解消した。

### D-26. カタログ等の「集約アセット」は、既存でも空なら埋め直す

`PlaceholderPartGenerator.CreateCatalog` は「既に PartCatalog.asset があれば何もしない」
という判断だったが、実機で「エラーは無いのに装備可能パーツが1つも無い」症状として再現した。
原因は D-25 と同種: 以前の中断・失敗した実行で**空のまま作られたカタログ**が既存扱いされ、
二度と中身が埋まらなくなっていた。`PartCatalog.PartsForSlot` は parts が空でもエラーを
出さない設計(§7 の安全側フォールバック)のため、この状態は静かに「0件」として表面化する。

- 個々の自己完結したアセット(パーツ1つ分の `PartDefinition` 等)は、既存なら据え置いてよい。
- しかし**他のアセットを集約するアセット**(`PartCatalog.parts` のような一覧)は、
  「既に存在する」だけでは「中身が正しい」ことを意味しない。
  既存でも中身が空(または全欠落)なら、生成した定義で埋め直す。
  中身が既にあるなら(手動追加分を保護するため)従来どおり据え置く。

### D-27. 装飾用キャラクターモデル(VRM 等)を Torso パーツにする経路の規約

VRoid 等で作った独立スケルトンのキャラクターモデルを、機体の見た目として
1スロット(通常は `Torso`)に差し込む補助経路を確定させる。設計ドキュメント §0 の
スコープ外だが、実機検証で「モデルが一切表示されない」事故が起きたため、
以下を仕様と同じ強さで扱う。

**背景の事故**: `CharacterPartAnimationSetupWindow` が生成プレハブを組み立てる際、
一時インスタンスのルートに `HideFlags.DontSave` を付けてから
`PrefabUtility.SaveAsPrefabAsset` を呼んでいた。Unity は DontSave の付いた
オブジェクトをプレハブ保存の対象から除外するため、保存は
`No objects were found for saving into prefab` の**エラーだけ出して null を返し、
ファイルは1つも書かれなかった**。ウィンドウはその null をそのまま
`PartDefinition.meshPrefab` に代入し、`AssetDatabase.SaveAssets()` で永続化し、
さらに「作成した」と Debug.Log した。結果、プレイヤーが指定した Torso パーツの
`meshPrefab` が **null に破壊され**、`MechAssembly.SpawnSlot` の
「`meshPrefab == null` は見た目を持たない正常なパーツ」分岐(D-13 とは別の正常系)に
吸い込まれて、**無言で不可視**になった。

1. **エディタツールがアセットへ参照を書き戻す前に、その参照が実在することを確認する。**
   `SaveAsPrefabAsset` の戻り値が null(または `out bool success` が false)のときは
   **`PartDefinition` に一切触れない**(既存の `meshPrefab` を保持する)。失敗時は
   `Debug.LogError` で止め、成功ログ・`AssetDatabase.SaveAssets()` を実行しない。
   「生成物をアセットの必須フィールドへ代入する」系のツールは全てこの順序を守る。
   (対応済み: commit eb99c84 / 8c07f75)

2. **オフスクリーンでプレハブを組むときは `HideFlags.DontSave` を使わない。**
   `HideFlags.DontSave` は「シーンに保存しない」だけでなく `SaveAsPrefabAsset` 自体を
   失敗させる(`No objects were found for saving into prefab`)。開いているシーンを
   汚さない目的なら、`GarageSceneBuilder` の `Create*Prefab` 系と同じく
   フラグ無しで組み立てて `finally` で `DestroyImmediate` する(保存直後に消えるので
   シーンには実質何も残らない)。`EditorSceneManager.NewPreviewScene()` に移してもよい
   (D-25 が禁じた「アクティブシーンを差し替える `NewScene`」とは別物)。
   (対応済み: commit 8c07f75)

3. **`MechAssembly` は `CosmeticLocomotionAnimator` が載ったモデルの Animator を剥がさない。**
   `stripNestedAnimators` は共有リグに追従する / 剛体で貼るだけの静的メッシュが
   誤って持ち込んだ Animator を消すための機能。独立スケルトンを自前の Animator で
   動かす装飾モデル(D-1)はその対象外。剥がすと
   `[RequireComponent(typeof(Animator))]` 違反でリビルドのたびにエラーが出て、
   かつ手足が完全に止まる。`StripAnimators` は
   `GetComponentInParent<CosmeticLocomotionAnimator>(true)` が非 null の Animator を
   スキップする。

4. **全身モデルを剛体アタッチするなら `PartAttachment` を必ず付ける。**
   `PartAttachment` 無しの `Torso` パーツはスロット既定ボーン `"Chest"`(高さ ~1.65m)に
   オフセット 0 で刺さり、モデルが宙に浮く。ウィンドウは生成プレハブに
   `PartAttachment` を追加し、既定で骨格ルートボーン `"Root"`(高さ 0)に
   オフセット 0 で付ける(VRM は足が原点にあるため接地する)。ボーン名・オフセットは
   ウィンドウの入力欄で調整でき、コード定数では変えない(§10 / D-1)。
