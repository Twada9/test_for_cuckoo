# ModularMech v1 — セットアップと動作確認手順

このドキュメントは、`ModularMech/` を Unity で開いてから、設計ドキュメント
(`docs/modular-mech-design-v1.md`)§11「v1完了の定義」の6項目を実際に確認するまでの手順書です。

> **この環境(コード生成側)では Unity のコンパイル・実行検証ができていません。**
> 以下の手順は「書いたコードを読み返した結果、こう動くはずだ」という設計に基づくもので、
> 実機での動作確認は済んでいません。初回はコンパイルエラーが出る前提で読み進めてください。
> 既知の未検証点は本ドキュメント末尾にまとめてあります。

---

## 0. 前提

- Unity 6(6000.0 系。手元の `ProjectSettings/ProjectVersion.txt` は `6000.0.32f1`)
- `ModularMech/` フォルダそのものを Unity Hub の「開く」でプロジェクトとして開く
  (`test_for_cuckoo` リポジトリ全体ではなく `ModularMech/` を指定すること)

Unity を初めて開くと、`Packages/manifest.json` に書かれた URP / Input System などの
パッケージが自動でインポートされます。この時点ではまだ **URP は有効になっていません**
(パッケージが入っているだけでは Player Settings のパイプライン割り当ては行われないため)。
次の「初回に必ずやること」を先に済ませてください。

---

## 1. 初回に必ずやること

### 1.1 URP の Render Pipeline Asset を割り当てる

パッケージ (`com.unity.render-pipelines.universal`) は入っていますが、**Render Pipeline Asset
そのものはまだプロジェクトに存在しません**。これを作って Graphics 設定に割り当てるまで、
プレースホルダパーツのマテリアル(`Universal Render Pipeline/Lit` シェーダ)は
**マゼンタ(真っ赤)** に見えます。

1. Project ウィンドウで `Assets` 直下などに `Settings` フォルダを作る(任意の場所でよい)
2. 右クリック → `Create > Rendering > URP Asset (with Universal Renderer)` を選び、
   名前を付けて保存(例: `Assets/Settings/URP_Asset.asset`。Universal Renderer の
   `.asset` も自動生成される)
3. `Edit > Project Settings > Graphics` を開き、`Scriptable Render Pipeline Settings` に
   手順2で作った URP Asset をドラッグする
4. `Edit > Project Settings > Quality` を開き、各品質レベル(Low/Medium/High 等)の
   `Render Pipeline Asset` にも同じアセットを割り当てる(Unity 6 は品質レベルごとに
   上書きできるため、ここが空だと結局 Built-in にフォールバックしてマゼンタになる)

### 1.2 Active Input Handling を旧 Input Manager 前提に合わせる

`Packages/manifest.json` には `com.unity.inputsystem` が入っていますが、
本プロジェクトの入力コード(`KeyboardMechInputSource`)は **旧 Input Manager
(`ENABLE_LEGACY_INPUT_MANAGER` シンボル)を前提に書かれています**。生成した
EventSystem も `StandaloneInputModule`(旧方式)です。

1. `Edit > Project Settings > Player > Other Settings > Configuration` を開く
2. `Active Input Handling` を **`Input Manager (Old)` または `Both`** にする
   (`Input System Package (New)` のままだと、UI のクリックも機体の移動も無反応になる)
3. Unity から再起動を促されるので、指示に従って再起動する

この2つを済ませていない状態で以下のセットアップを進めても、シーン自体は作れますが
「マゼンタで何も操作できない画面」になるだけなので、**必ず最初に確認してください**。

---

## 2. セットアップウィンドウでの構築手順

メニュー `Tools > ModularMech > Setup Window` を開きます。上から3ステップが並んでおり、
各ステップの前提(前段のアセットが無い等)が満たされていない場合は日本語で理由が表示され、
ボタンが無効化されます。

1. **① プレースホルダ一式を生成する**
   `PlaceholderPartGenerator`(M1)を実行し、パーツプレハブ / `PartDefinition` /
   `LocomotionProfile` / `PartCatalog` / 機体リグ(`Mech_Placeholder.prefab`)を
   `Assets/Prefabs` `Assets/ScriptableObjects` `Assets/Materials/Placeholder` に生成する。
2. **② ガレージシーンを構築する**
   `Assets/Scenes/Garage.unity` を新規作成し、スロット一覧・3Dプレビュー・パーツ一覧・
   ステータスパネルの UI 一式と、プレビュー用の機体インスタンスを組み立てる。
3. **③ テストフィールドシーンを構築する**
   `Assets/Scenes/TestField.unity` を新規作成し、地形・機体スポーン・追従カメラ・
   ガレージへ戻るボタンを組み立てる。

一番下の「①→②→③ をまとめて実行する」ボタンでも同じことが一度に行えます。
**②③ は既存の同名シーンファイルがあると上書きせず警告して中断します。** 作り直したい場合は
`Assets/Scenes/Garage.unity` または `TestField.unity`(と対応する `.meta`)を削除してから
再実行してください。

このウィンドウ以外にも、個別のメニュー項目として

- `Tools > ModularMech > Generate Placeholder Assets`
- `Tools > ModularMech > Build Garage Scene`
- `Tools > ModularMech > Build Test Field Scene`

が用意されています(ウィンドウの各ボタンはこれらを呼んでいるだけです)。

構築が終わると、両シーンは `File > Build Settings` にも自動で登録されます
(シーン内の「テスト走行へ」「ガレージへ戻る」ボタンが `SceneManager.LoadScene` で
シーン名を解決するために必要)。

---

## 3. 操作方法

### ガレージ画面(`Garage.unity`)

- 左のスロット一覧をクリック → 右にそのスロットへ装備可能なパーツ一覧が出る
- パーツをクリックすると即座に装備され、中央の3Dプレビューと下のステータスパネルに反映される
- 一覧の先頭には「(装備しない)」があり、選ぶとそのスロットが空になる
- 中央のプレビューはドラッグで回転できる(左右のみ。上下チルトは対象外)
- 上部バーの「保存」「読込」で `Application.persistentDataPath/loadouts.json` へ
  保存・復元する(M7)
- 上部バーの「テスト走行へ」で `TestField` シーンへ移動する(出撃不可な構成のときは
  何も起きない。Console に理由が出る)

### テスト走行画面(`TestField.unity`)

- `W` / `S`: 前進・後退
- `A` / `D`: その場旋回(左右移動ではなく、機体を左右に向ける)
- `Shift`: 走行(Run 能力が無い/剥奪されていると無効)
- `Space`: ジャンプ(Jump 能力が無い/剥奪されていると無効。ホバー脚は常に無効)
- `Ctrl`: しゃがみ(見た目のみ。M1〜M8 の範囲ではクールダウン等は無い)
- `G`: エモート(見た目のみ)
- 左上「ガレージへ戻る」ボタンでいつでも `Garage` シーンへ戻れる

ガレージで保存した構成は、**ガレージ画面とテスト走行画面のどちらも起動時に自動で読み込みます**
(`GarageScreen` / `TestFieldScreen` がそれぞれ同じ保存ファイルを読み、無ければ機体プレハブ既定の
構成にフォールバックする)。ガレージを開き直したときに「読込」を押す必要はありません
(押せば同じ内容を読み直します)。
**保存を挟まずに「テスト走行へ」ボタンから移動した場合は、その場でも自動保存してから
遷移するので、明示的に保存ボタンを押す必要はありません。**

> ガレージ側の自動読込が無いと、「保存 → 再起動 → 読込を押さずにテスト走行へ」の順で
> 出撃時の自動保存が保存済み構成を上書きし、保存したはずの構成が消えます(CLAUDE.md D-21)。

---

## 4. §11 完了条件6項目の確認手順

| # | 条件 | 確認する画面と操作 |
|---|---|---|
| 1 | 全スロットのパーツを任意に入れ替えられる | ガレージ画面で8スロット(頭・胴・左右腕・脚・バックパック・左右手持ち)すべてを一度は選び、右のパーツ一覧から別のものへ切り替える。手持ちスロットは対応する腕を装備するまでボタンが非活性(理由テキスト表示)になることも確認する。 |
| 2 | 入れ替えが3Dプレビューに即座に反映される | 上記の切り替え中、中央のプレビューの見た目(パーツの形・色)が装備直後に変わることを確認する。ドラッグで一周回して裏側も見る。 |
| 3 | 総重量・電力・能力がリアルタイムに再計算・表示される | 下部ステータスパネルの「重量 X / Y」「電力 出力/消費」「速度」「能力アイコン」が、装備を変えるたびに数値ごと更新されることを確認する。 |
| 4 | 過積載時に警告が出て、実際に走行不可になる | ガレージで `装甲ヘッド` + `重装甲胴体` + `重装腕(左)` + `重装腕(右)` + `軽量二脚` を装備する(14+95+38+38+20 = 総重量205 / 軽量二脚の積載上限120、比1.71。`PlaceholderPartGenerator` のコメントに検証用として明記されている組み合わせそのもの)。ステータスパネルに赤字の警告行と、能力アイコン列で「走行」が取り消し線付き(剥奪)表示になることを確認する。「テスト走行へ」で移動し、`Shift` を押しても走行(スプリント)にならないこと、`Space` でのジャンプも不可になっていること(比1.5超のため)を確認する。**歩行そのものはできる**(D-8: 走行不可 = Run 剥奪であって停止ではない)。 |
| 5 | 二脚 / ホバーの脚を入れ替えると移動挙動が明確に変わる | ガレージで脚を `軽量二脚(legs_biped_light_01)` にして「テスト走行へ」。接地して走る・跳べることを確認したらガレージへ戻り、脚を `ホバー脚(legs_hover_01)` に差し替えて再度「テスト走行へ」。今度は機体が地面から浮いた高さを保って滑るように移動し、`Space` を押してもジャンプしないことを確認する。 |
| 6 | 構成を保存し、再起動後に復元できる | ガレージで好きな構成を組み、上部バーの「保存」を押す(Console に失敗時のみ警告が出る)。Unity エディタを一度終了する、または Play モードを止めて再度 Play し直す。**ガレージ画面は起動時に保存ファイルを自動で読み込む(D-21)ので、何も押さずに保存時と同じ構成が復元されていること**を確認する(「読込」ボタンを押しても同じ結果になる)。保存したパーツがカタログから消えている場合は、ステータスパネルの警告行にその旨が出る(D-17)。保存先は `Application.persistentDataPath/loadouts.json`(Windows: `%USERPROFILE%\AppData\LocalLow\<CompanyName>\ModularMech\loadouts.json`、macOS: `~/Library/Application Support/<CompanyName>/ModularMech/loadouts.json`、Linux: `~/.config/unity3d/<CompanyName>/ModularMech/loadouts.json`)。 |

---

## 5. このセットアップが自動生成するアセット / シーンの構成

### ガレージシーン(`Garage.unity`)の階層(概略)

```
EventSystem (EventSystem, StandaloneInputModule)
PreviewLight (Light: Directional)
MechPreviewPivot
├─ PreviewPlatform (Cylinder, コライダ無し)
└─ Mech_Preview (Mech_Placeholder のインスタンス。MechLocomotionController は無効化)
PreviewCamera (Camera: targetTexture = GaragePreview RenderTexture)
Canvas (Screen Space - Overlay)
├─ Background
├─ PreviewArea (RawImage + MechPreviewRotator。target = MechPreviewPivot)
├─ LeftPanel_Slots (SlotListView)
│  └─ Scroll/Viewport/Content (SlotEntry プレハブがここに実行時プールされる)
├─ RightPanel_Parts (PartListView)
│  └─ Scroll/Viewport/Content (PartEntry プレハブが実行時プールされる)
├─ BottomPanel_Stats (StatPanelView)
│  └─ Content (重量行 / 電力行 / 速度行 / 能力アイコン行 / 警告一覧行)
└─ TopBar (読込 / 保存 / テスト走行へ ボタン)
   └─ GarageScreen コンポーネントは Canvas ルートに付与
```

**Play モードに入る前(Scene ビュー)では、機体は見た目のパーツが何も付いていない
「素のリグ」だけに見えます。** これは仕様どおりです — `MechAssembly` / `MechRuntime` の
どちらも `ExecuteInEditMode` を持たないため、パーツの組み立ては Play 中にしか走りません。
Play すると `MechRuntime.Start` → `MechRuntime.Apply` が走り、初めて見た目が組み上がります。

### テストフィールドシーン(`TestField.unity`)の階層(概略)

```
EventSystem
Sun (Light: Directional)
Terrain
├─ Ground (Cube, Ground レイヤー)
├─ Step_0 .. Step_4 (5段の階段, Ground レイヤー)
└─ Ramp_Gentle / Ramp_Steep (傾斜2本, Ground レイヤー)
Mech (Mech_Placeholder のインスタンス。MechLocomotionController.groundMask = Ground レイヤー)
FollowCamera (Camera + MechFollowCamera。target = Mech)
Canvas
├─ TestFieldScreen コンポーネント(保存済み Loadout の読み込み)
├─ TopLeft/BackButton (ガレージへ戻る)
└─ ControlsHint (操作説明テキスト)
```

### SerializeField 結線対応表(このセットアップが埋めるもの)

いずれも `SerializedObject` 経由で **フィールド名の文字列** を指定して埋めています。
C# のコンパイラが検出できない唯一の箇所なので、**対象クラスの `[SerializeField]` を
増減・改名したら、生成側(`PlaceholderPartGenerator` / `GarageSceneBuilder` /
`TestFieldSceneBuilder`)とこの表の両方を必ず同時に直してください。**
名前が食い違うと生成時に Console へエラー(または警告)が出て、その参照だけが空のままになります。

#### ステップ①(`PlaceholderPartGenerator`)が機体プレハブ `Mech_Placeholder.prefab` に埋めるもの

| コンポーネント | フィールド | 結線先 |
|---|---|---|
| `MechAssembly` | `skeletonRoot` | プレハブ内のボーン階層のルート `Root` |
| `MechAnimationDriver` | `animator` | プレハブルートの `Animator` |
| `MechRuntime` | `assembly` | 同じ GameObject の `MechAssembly` |
| | `animationDriver` | 同じ GameObject の `MechAnimationDriver` |
| | `catalog` | `Assets/ScriptableObjects/PartCatalog.asset` |
| | `defaultPartIds` | `PartSlots.All` の宣言順で8要素(`head_sensor_01` / `torso_standard_01` / `arm_standard_l_01` / `arm_standard_r_01` / `legs_biped_light_01` / 以降3件は空文字)。総重量107 / 上限120、消費32 / 出力95 でペナルティのかからない基準構成 |
| `MechLocomotionController` | `runtime` | 同じ GameObject の `MechRuntime` |
| | `characterController` | 同じ GameObject の `CharacterController` |
| | `animationDriver` | 同じ GameObject の `MechAnimationDriver` |

`MechLocomotionController` は `Animator` を直接持ちません(アニメータへの書き込みは
`MechAnimationDriver` に一本化してあるため)。

#### ステップ②③(シーンビルダ)が各シーンに埋めるもの

| コンポーネント | フィールド | 結線先 |
|---|---|---|
| `GarageScreen` | `mechRuntime` | Garage の `Mech_Preview` 上の `MechRuntime` |
| | `partCatalog` | `Assets/ScriptableObjects/PartCatalog.asset` |
| | `slotListView` / `partListView` / `statPanelView` | 同シーン内の各ビュー |
| | `saveButton` / `loadButton` | TopBar の「保存」「読込」ボタン |
| | `previewLocomotion` | `Mech_Preview` 上の `MechLocomotionController`(ガレージでは起動時に無効化される。D-19。ステータスパネルの「走行時 ×N」の倍率もここから読む) |
| `SlotListView` | `entryPrefab` | `Assets/Prefabs/UI/SlotEntry.prefab` |
| | `entryContainer` | LeftPanel のスクロール `Content` |
| `PartListView` | `entryPrefab` | `Assets/Prefabs/UI/PartEntry.prefab` |
| | `entryContainer` | RightPanel のスクロール `Content` |
| `StatPanelView` | `weightText` / `weightBarFill` / `weightOverflowBarFill` | BottomPanel の重量行 |
| | `powerText` / `powerBarFill` | BottomPanel の電力行 |
| | `speedText` / `speedBaseText` / `speedRunText` | BottomPanel の速度行(主表示 / 基礎値 / 走行時倍率) |
| | `capabilityIconSet` | `Assets/ScriptableObjects/UI/CapabilityIconSet.asset` |
| | `capabilityIconContainer` / `capabilityIconPrefab` | 能力アイコン行 / `Assets/Prefabs/UI/CapabilityIcon.prefab` |
| | `issueListContainer` / `issueTextPrefab` | 警告一覧行 / `Assets/Prefabs/UI/IssueText.prefab` |
| `SlotEntryView`(`SlotEntry.prefab`) | `slotNameText` / `partNameText` / `iconImage` / `selectedHighlight` / `button` | プレハブ内の各要素 |
| `PartEntryView`(`PartEntry.prefab`) | `iconImage` / `nameText` / `reasonText` / `equippedHighlight` / `button` | プレハブ内の各要素 |
| `CapabilityIconView`(`CapabilityIcon.prefab`) | `iconImage` / `labelText` / `strippedOverlay` | アイコン(白い四角)/ 能力名テキスト / 剥奪時の斜線 |
| `MechPreviewRotator` | `target` | `MechPreviewPivot` |
| `SceneTransitionButton`(出撃ボタン) | `sceneName` / `requireDeployableFrom` / `saveBeforeLoad` | `"TestField"` / `GarageScreen` / `GarageScreen` |
| `SceneTransitionButton`(戻るボタン) | `sceneName` | `"Garage"` |
| `TestFieldScreen` | `mechRuntime` / `partCatalog` | TestField の `Mech` 上の `MechRuntime` / カタログ |
| `MechFollowCamera` | `target` | TestField の `Mech` の Transform |
| `MechLocomotionController`(TestField の Mech) | `groundMask` | `Ground` レイヤーのみ |

---

## 6. 設計ドキュメント・CLAUDE.md からの意図的な逸脱

- **新規ランタイムスクリプトを3本追加した**(当初依頼の Editor 配下3ファイルには
  含まれない): `Assets/Scripts/UI/SceneTransitionButton.cs`、
  `Assets/Scripts/UI/TestFieldScreen.cs`、`Assets/Scripts/Runtime/MechFollowCamera.cs`。
  理由: シーンが2つに分かれた以上、(a) 両シーン間を行き来する導線、(b) ガレージで組んだ
  構成をテストフィールドへ引き継ぐ橋渡し、(c) 追従カメラ、のどれも既存コードには
  存在せず、シーン構築だけでは満たせなかったため。いずれも `MechRuntime.Apply` /
  `GarageScreen.SaveToDisk` など既存の公開 API を呼ぶだけの薄いラッパーで、
  集約・検証・ペナルティ計算などのロジックには一切手を入れていない。
- **`Assets/Scripts/Editor/ModularMech.EditorTools.asmdef` に `"UnityEngine.UI"` 参照を追加した。**
  Editor 側スクリプトで `Text` / `Image` / `Button` / `ScrollRect` / `EventSystem` 等の
  uGUI 型を直接組み立てる必要があり、この参照が無いとコンパイルできないため。
  既存の公開 API・名前空間規約には影響しない。
- **`Assets/Scripts/Editor/UGUIBuilderUtility.cs` を追加した。** Garage / TestField
  両方のシーン構築で共通する uGUI 組み立て処理(Canvas 生成・レイアウト・
  SerializedObject 経由の参照結線など)をまとめた、公開 API を持たない内部ヘルパー。

---

## 7. 未検証・既知の注意点

- **この環境では Unity のコンパイル・実行検証ができていません。** 初回オープン時に
  コンパイルエラーが出る可能性があります。特に uGUI 周りの型名・プロパティ名は
  目視で確認したのみです。
- ベース `AnimatorController` はまだ作られていません(M5 の対象で本タスクの範囲外)。
  `MechAnimationDriver` は割り当てるコントローラが無い場合 Console に警告を出して
  何もしない設計なので、**アニメーションが再生されないだけで、移動そのものは
  スクリプト駆動のまま正常に動く**はずです(移動に root motion は使っていない)。
  この警告は装備を変えるたびではなく**最初の1回だけ**出ます(毎回出すと、ボーン名の
  不一致など1回きりの警告がコンソールから流れてしまうため)。
- 全プレースホルダパーツは `AttachmentMode.RigidToBone` で、`MechAssembly.AttachSkinned`
  (Skinned メッシュ経路)は一度も経由していません。購入アセット等で Skinned メッシュを
  導入する際は、この経路の動作を別途確認してください。
- ガレージシーンは Play モードに入る前は機体の見た目が空(素のリグのみ)に見えます。
  `MechAssembly` / `MechRuntime` が `ExecuteInEditMode` を持たないための仕様です。
- テストフィールドの段差・スロープの正確な位置合わせ(地面との継ぎ目のめり込み/隙間)は
  座標計算のみで組んでおり、実機でのビジュアル確認はできていません。段差の1段あたりの
  高さは `CharacterController` の既定 `stepOffset`(0.3)以下に収めてありますが、
  プレハブ側の実際の設定値までは確認していません。
- `Ground` レイヤーの確保は `ProjectSettings/TagManager.asset` を直接編集する方式で、
  ユーザーレイヤー(8〜31)に空きが無いプロジェクトでは失敗し、`groundMask` が
  既定の Everything のままフォールバックします(動作はしますが地形限定にはなりません)。
- ステータスパネルの「重量超過」バーは、100%まで埋めた帯の上に赤い帯を重ねて
  `fillAmount = clamp01(超過率-1)` だけ表示する簡易表現です。100%地点の右側に
  継ぎ足す表現(設計ドキュメント §6.1 が想定するであろう見た目)にはなっていません。
- Active Input Handling を「Input System Package (New)」のままにした場合、
  UI クリックも機体移動も反応しません(§1.2 を参照)。これは事故ではなく、
  実装(`KeyboardMechInputSource` / `StandaloneInputModule`)が旧方式前提のためです。
- 保存ファイルがまだ無い状態でガレージを開くと、起動時の自動読込(D-21)の結果として
  ステータスパネルの警告行に「保存ファイルが見つかりません。新規状態として扱います。」が
  1行出ます。異常ではなく、「既定構成から始めた」ことの明示です(一度保存すれば消えます)。
- `CapabilityIconSet` のアイコンはすべて未設定(スプライト無し)です。そのため能力アイコンは
  **白い四角 + 能力名テキスト**(「歩行」「走行」…)で表示され、剥奪されたものは暗い色と
  取り消し線で示されます。どの能力が剥奪されたかはテキストで判別できますが、実際のアイコン画像は
  デザイナーが差し替える前提です(`CapabilityIconSet` の `icon` にスプライトを入れるだけで、
  コード変更なしに置き換わります)。
