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
