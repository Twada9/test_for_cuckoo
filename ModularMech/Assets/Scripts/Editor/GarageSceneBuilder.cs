using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Mechs;
using ModularMech.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ModularMech.EditorTools
{
    /// <summary>
    /// ガレージ画面のシーン(<c>Assets/Scenes/Garage.unity</c>)をスクリプトから組み立てる
    /// (設計ドキュメント §6.1 / §9 M8)。
    ///
    /// レイアウトは「左:スロット一覧 / 中央:3Dプレビュー / 右:パーツ一覧 / 下:ステータスパネル」。
    /// 3Dプレビューは専用カメラが機体だけを RenderTexture へ描き、それを中央の RawImage に映す方式
    /// (<see cref="MechPreviewRotator"/> はその RawImage 上でドラッグを受けて、プレビュー用の
    /// ピボット Transform を回転させる)。
    ///
    /// 前提: <c>Tools/ModularMech/Generate Placeholder Assets</c>(<see cref="PlaceholderPartGenerator"/>)
    /// を先に実行し、<see cref="PartCatalog"/> と機体プレハブが存在していること。
    /// 既存の Garage.unity がある場合は上書きしない(警告して中断)。
    /// </summary>
    public static class GarageSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Garage.unity";
        const string CatalogPath = "Assets/ScriptableObjects/PartCatalog.asset";
        const string MechPrefabPath = "Assets/Prefabs/Mech/Mech_Placeholder.prefab";
        const string UIPrefabFolder = "Assets/Prefabs/UI";
        const string CapabilityIconSetPath = "Assets/ScriptableObjects/UI/CapabilityIconSet.asset";
        const string TextureFolder = "Assets/Textures";
        const string RenderTexturePath = TextureFolder + "/GaragePreview.renderTexture";
        const string PreviewMaterialPath = "Assets/Materials/Placeholder/Placeholder_PreviewPlatform.mat";

        const float TopBarHeight = 64f;
        const float BottomPanelHeight = 260f;
        const float LeftWidth = 380f;
        const float RightWidth = 420f;

        [MenuItem("Tools/ModularMech/Build Garage Scene")]
        public static void BuildScene()
        {
            if (!TryLoadPrerequisites(out PartCatalog catalog, out GameObject mechPrefab))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ScenePath) != null)
            {
                Debug.LogWarning(
                    $"[GarageSceneBuilder] 既に {ScenePath} が存在するため何もしない。" +
                    "作り直す場合は該当ファイル(と .meta)を削除してから再実行すること。");
                return;
            }

            UGUIBuilderUtility.EnsureFolder("Assets/Scenes");
            UGUIBuilderUtility.EnsureFolder(UIPrefabFolder);
            UGUIBuilderUtility.EnsureFolder("Assets/ScriptableObjects/UI");
            UGUIBuilderUtility.EnsureFolder(TextureFolder);
            // PreviewMaterialPath の親フォルダ。通常は PlaceholderPartGenerator が先に作るが、
            // 単独でここだけ削除された場合に AssetDatabase.CreateAsset が失敗しないよう明示的に確保する。
            UGUIBuilderUtility.EnsureFolder("Assets/Materials/Placeholder");

            // NewScene は開いているシーンの未保存変更を確認なしに破棄する。
            // Unity 自身のシーンテンプレート機能もこの確認を先に挟んでいる。
            //
            // 重要: この呼び出しは、プレハブ/ScriptableObject の生成・読込より**前**に置くこと。
            // シーンの切り替え(NewScene)は、どこにも根を持たない(まだ SerializeField 等に
            // 代入されていない)ロード済みアセット参照を Unity が暗黙に解放する契機になり得る。
            // 実際に、ここより後で CreateXxxPrefab() を呼ぶ順序だったときは、
            // 「既存のため再利用した」プレハブ/アセットの参照が NewScene の後で
            // すべて破棄済み扱いになり、SlotListView 等の SerializeField に
            // null が代入される実行時エラーとして発覚した。
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[GarageSceneBuilder] ユーザーがキャンセルしたため中断する。");
                return;
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SlotEntryView slotEntryPrefab = CreateSlotEntryPrefab();
            PartEntryView partEntryPrefab = CreatePartEntryPrefab();
            CapabilityIconView capabilityIconPrefab = CreateCapabilityIconPrefab();
            Text issueTextPrefab = CreateIssueTextPrefab();
            CapabilityIconSet capabilityIconSet = CreateCapabilityIconSet();

            UGUIBuilderUtility.CreateLegacyEventSystem();

            (Transform previewPivot, MechRuntime mechRuntime) = BuildPreviewStage(mechPrefab);

            Canvas canvas = UGUIBuilderUtility.CreateScreenCanvas("Canvas");
            RectTransform canvasRoot = canvas.GetComponent<RectTransform>();

            BuildBackground(canvasRoot);
            BuildPreviewArea(canvasRoot, previewPivot);

            SlotListView slotListView = BuildLeftPanel(canvasRoot, slotEntryPrefab);
            PartListView partListView = BuildRightPanel(canvasRoot, partEntryPrefab);
            StatPanelView statPanelView = BuildBottomPanel(canvasRoot, capabilityIconSet, capabilityIconPrefab, issueTextPrefab);

            GarageScreen garageScreen = canvasRoot.gameObject.AddComponent<GarageScreen>();
            (Button saveButton, Button loadButton, Button deployButton) = BuildTopBar(canvasRoot);

            UGUIBuilderUtility.SetRef(garageScreen, "mechRuntime", mechRuntime);
            UGUIBuilderUtility.SetRef(garageScreen, "partCatalog", catalog);
            UGUIBuilderUtility.SetRef(garageScreen, "slotListView", slotListView);
            UGUIBuilderUtility.SetRef(garageScreen, "partListView", partListView);
            UGUIBuilderUtility.SetRef(garageScreen, "statPanelView", statPanelView);
            UGUIBuilderUtility.SetRef(garageScreen, "saveButton", saveButton);
            UGUIBuilderUtility.SetRef(garageScreen, "loadButton", loadButton);

            // GarageScreen 自身も Start でプレビュー機体の MechLocomotionController を無効化する
            // (同じ GameObject から GetComponent するフォールバックを持つ)が、参照の挿し忘れを
            // 残さないためここでも明示的に結線しておく(二重に無効化しても冪等なので害はない)。
            var previewLocomotion = mechRuntime != null ? mechRuntime.GetComponent<MechLocomotionController>() : null;
            UGUIBuilderUtility.SetRef(garageScreen, "previewLocomotion", previewLocomotion);

            var deployTransition = deployButton.gameObject.AddComponent<SceneTransitionButton>();
            SetSceneName(deployTransition, "TestField");
            UGUIBuilderUtility.SetRef(deployTransition, "requireDeployableFrom", garageScreen);
            UGUIBuilderUtility.SetRef(deployTransition, "saveBeforeLoad", garageScreen);

            // Button.onClick.AddListener は非永続リスナーで、シーン保存には残らない。
            // シーンファイルへ焼き込むには UnityEventTools 経由の永続リスナー登録が必須。
            UnityEventTools.AddPersistentListener(deployButton.onClick, deployTransition.LoadScene);
            EditorUtility.SetDirty(deployButton);

            EnsureInBuildSettings(ScenePath);
            EnsureInBuildSettings("Assets/Scenes/TestField.unity", onlyIfExists: true);

            bool saved = EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (saved)
            {
                Debug.Log($"[GarageSceneBuilder] {ScenePath} を作成した。");
            }
            else
            {
                Debug.LogError($"[GarageSceneBuilder] {ScenePath} の保存に失敗した。");
            }
        }

        static bool TryLoadPrerequisites(out PartCatalog catalog, out GameObject mechPrefab)
        {
            catalog = AssetDatabase.LoadAssetAtPath<PartCatalog>(CatalogPath);
            mechPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MechPrefabPath);

            if (catalog == null)
            {
                Debug.LogError(
                    $"[GarageSceneBuilder] {CatalogPath} が見つからない。" +
                    "先に Tools/ModularMech/Generate Placeholder Assets を実行すること。");
                return false;
            }

            if (mechPrefab == null)
            {
                Debug.LogError(
                    $"[GarageSceneBuilder] {MechPrefabPath} が見つからない。" +
                    "先に Tools/ModularMech/Generate Placeholder Assets を実行すること。");
                catalog = null;
                return false;
            }

            if (mechPrefab.GetComponent<MechRuntime>() == null)
            {
                Debug.LogError(
                    $"[GarageSceneBuilder] {MechPrefabPath} に MechRuntime が付いていない。" +
                    "プレースホルダ生成をやり直すこと。");
                catalog = null;
                mechPrefab = null;
                return false;
            }

            return true;
        }

        // --- プレビュー用の3D舞台 ---------------------------------------------------------

        static (Transform pivot, MechRuntime runtime) BuildPreviewStage(GameObject mechPrefab)
        {
            var lightGo = new GameObject("PreviewLight", typeof(Light));
            Light light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            var pivotGo = new GameObject("MechPreviewPivot");
            pivotGo.transform.position = Vector3.zero;

            var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = "PreviewPlatform";
            Object.DestroyImmediate(platform.GetComponent<Collider>());
            platform.transform.position = new Vector3(0f, -0.05f, 0f);
            platform.transform.localScale = new Vector3(3.2f, 0.05f, 3.2f);
            Material platformMaterial = UGUIBuilderUtility.GetOrCreateMaterial(
                PreviewMaterialPath, new Color(0.20f, 0.22f, 0.26f));
            if (platformMaterial != null)
            {
                platform.GetComponent<MeshRenderer>().sharedMaterial = platformMaterial;
            }

            var mechInstance = (GameObject)PrefabUtility.InstantiatePrefab(mechPrefab);
            mechInstance.name = "Mech_Preview";
            mechInstance.transform.SetParent(pivotGo.transform, false);
            mechInstance.transform.localPosition = Vector3.zero;
            mechInstance.transform.localRotation = Quaternion.identity;

            // ガレージのプレビューは回転させて眺めるだけの置物。移動コントローラを生かしたままだと
            // キーボード入力が(旧 Input Manager 経由で)そのまま拾われ、CharacterController が
            // ドラッグ回転と無関係に動いてしまう。MechRuntime / MechAssembly / MechAnimationDriver は
            // 装備変更のライブ反映に必要なので有効のままにする。
            var locomotion = mechInstance.GetComponent<MechLocomotionController>();
            if (locomotion != null)
            {
                locomotion.enabled = false;
            }

            MechRuntime runtime = mechInstance.GetComponent<MechRuntime>();

            RenderTexture previewTexture = GetOrCreatePreviewTexture();

            var cameraGo = new GameObject("PreviewCamera", typeof(Camera));
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
            camera.targetTexture = previewTexture;
            camera.fieldOfView = 32f;
            camera.nearClipPlane = 0.05f;
            cameraGo.transform.position = new Vector3(0f, 1.5f, -4.4f);
            cameraGo.transform.LookAt(new Vector3(0f, 1.0f, 0f), Vector3.up);

            return (pivotGo.transform, runtime);
        }

        static RenderTexture GetOrCreatePreviewTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
            if (existing != null)
            {
                return existing;
            }

            var rt = new RenderTexture(1024, 1024, 24) { name = "GaragePreview" };
            AssetDatabase.CreateAsset(rt, RenderTexturePath);
            return rt;
        }

        // --- Canvas 内レイアウト -----------------------------------------------------------

        static void BuildBackground(RectTransform canvasRoot)
        {
            Image bg = UGUIBuilderUtility.CreateImage("Background", canvasRoot, new Color(0.09f, 0.09f, 0.11f), raycastTarget: false);
            UGUIBuilderUtility.Stretch(bg.rectTransform);
        }

        static void BuildPreviewArea(RectTransform canvasRoot, Transform previewPivot)
        {
            RectTransform area = UGUIBuilderUtility.CreateUIObject("PreviewArea", canvasRoot);
            UGUIBuilderUtility.AnchorInset(area, LeftWidth, RightWidth, TopBarHeight, BottomPanelHeight);

            var rawImage = area.gameObject.AddComponent<RawImage>();
            rawImage.texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
            rawImage.raycastTarget = true;

            var rotator = area.gameObject.AddComponent<MechPreviewRotator>();
            UGUIBuilderUtility.SetRef(rotator, "target", previewPivot);

            Text hint = UGUIBuilderUtility.CreateText(
                "DragHint", area, "ドラッグで回転", 14, TextAnchor.LowerCenter, new Color(1f, 1f, 1f, 0.5f));
            hint.rectTransform.anchorMin = new Vector2(0f, 0f);
            hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.sizeDelta = new Vector2(0f, 24f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 4f);
        }

        static SlotListView BuildLeftPanel(RectTransform canvasRoot, SlotEntryView entryPrefab)
        {
            RectTransform panel = UGUIBuilderUtility.CreateUIObject("LeftPanel_Slots", canvasRoot);
            UGUIBuilderUtility.AnchorLeftColumn(panel, LeftWidth, TopBarHeight, BottomPanelHeight);
            panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);

            Text title = UGUIBuilderUtility.CreateText("Title", panel, "スロット", 22, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(0f, 36f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -6f);

            RectTransform content = UGUIBuilderUtility.CreateVerticalScrollList("Scroll", panel);
            RectTransform scrollRoot = content.parent.parent.GetComponent<RectTransform>();
            UGUIBuilderUtility.AnchorInset(scrollRoot, 0f, 0f, 42f, 0f);

            var view = panel.gameObject.AddComponent<SlotListView>();
            UGUIBuilderUtility.SetRef(view, "entryPrefab", entryPrefab);
            UGUIBuilderUtility.SetRef(view, "entryContainer", content);

            return view;
        }

        static PartListView BuildRightPanel(RectTransform canvasRoot, PartEntryView entryPrefab)
        {
            RectTransform panel = UGUIBuilderUtility.CreateUIObject("RightPanel_Parts", canvasRoot);
            UGUIBuilderUtility.AnchorRightColumn(panel, RightWidth, TopBarHeight, BottomPanelHeight);
            panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);

            Text title = UGUIBuilderUtility.CreateText("Title", panel, "装備可能パーツ", 22, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(0f, 36f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -6f);

            RectTransform content = UGUIBuilderUtility.CreateVerticalScrollList("Scroll", panel);
            RectTransform scrollRoot = content.parent.parent.GetComponent<RectTransform>();
            UGUIBuilderUtility.AnchorInset(scrollRoot, 0f, 0f, 42f, 0f);

            var view = panel.gameObject.AddComponent<PartListView>();
            UGUIBuilderUtility.SetRef(view, "entryPrefab", entryPrefab);
            UGUIBuilderUtility.SetRef(view, "entryContainer", content);

            return view;
        }

        static StatPanelView BuildBottomPanel(
            RectTransform canvasRoot, CapabilityIconSet iconSet, CapabilityIconView iconPrefab, Text issueTextPrefab)
        {
            RectTransform panel = UGUIBuilderUtility.CreateUIObject("BottomPanel_Stats", canvasRoot);
            UGUIBuilderUtility.AnchorBottomBar(panel, BottomPanelHeight);
            panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            RectTransform content = UGUIBuilderUtility.CreateUIObject("Content", panel);
            UGUIBuilderUtility.AnchorInset(content, 16f, 16f, 10f, 10f);
            UGUIBuilderUtility.AddVerticalLayout(content.gameObject, 6f);

            (Text weightText, Image weightFill, Image weightOverflowFill) = BuildBarRow(content, "WeightRow", "重量 — / —");
            (Text powerText, Image powerFill, _) = BuildBarRow(content, "PowerRow", "電力 — / —");

            RectTransform speedRow = UGUIBuilderUtility.CreateUIObject("SpeedRow", content);
            UGUIBuilderUtility.AddFixedHeight(speedRow.gameObject, 28f);
            UGUIBuilderUtility.AddHorizontalLayout(speedRow.gameObject, 10f);
            Text speedText = UGUIBuilderUtility.CreateText("Speed", speedRow, "速度 —", 18, TextAnchor.MiddleLeft);
            UGUIBuilderUtility.AddFixedWidth(speedText.gameObject, 160f);
            Text speedBaseText = UGUIBuilderUtility.CreateText("SpeedBase", speedRow, string.Empty, 14, TextAnchor.MiddleLeft, new Color(0.75f, 0.75f, 0.8f));
            UGUIBuilderUtility.AddFixedWidth(speedBaseText.gameObject, 120f);

            // スプリント倍率の副表示(D-23)。主表示の実効速度はスプリントを含まないため、
            // ここが無いと「Shift で主表示より速く走る」ことが画面から読み取れない。
            Text speedRunText = UGUIBuilderUtility.CreateText("SpeedRun", speedRow, string.Empty, 14, TextAnchor.MiddleLeft, new Color(0.75f, 0.8f, 0.75f));
            UGUIBuilderUtility.AddFlexibleWidth(speedRunText.gameObject);

            RectTransform capsRow = UGUIBuilderUtility.CreateUIObject("CapabilitiesRow", content);
            UGUIBuilderUtility.AddFixedHeight(capsRow.gameObject, 44f);
            UGUIBuilderUtility.AddHorizontalLayout(capsRow.gameObject, 8f);

            RectTransform issuesRow = UGUIBuilderUtility.CreateUIObject("IssuesList", content);
            UGUIBuilderUtility.AddFixedHeight(issuesRow.gameObject, 74f);
            UGUIBuilderUtility.AddVerticalLayout(issuesRow.gameObject, 2f);

            var view = panel.gameObject.AddComponent<StatPanelView>();
            UGUIBuilderUtility.SetRef(view, "weightText", weightText);
            UGUIBuilderUtility.SetRef(view, "weightBarFill", weightFill);
            UGUIBuilderUtility.SetRef(view, "weightOverflowBarFill", weightOverflowFill);
            UGUIBuilderUtility.SetRef(view, "powerText", powerText);
            UGUIBuilderUtility.SetRef(view, "powerBarFill", powerFill);
            UGUIBuilderUtility.SetRef(view, "speedText", speedText);
            UGUIBuilderUtility.SetRef(view, "speedBaseText", speedBaseText);
            UGUIBuilderUtility.SetRef(view, "speedRunText", speedRunText);
            UGUIBuilderUtility.SetRef(view, "capabilityIconSet", iconSet);
            UGUIBuilderUtility.SetRef(view, "capabilityIconContainer", capsRow);
            UGUIBuilderUtility.SetRef(view, "capabilityIconPrefab", iconPrefab);
            UGUIBuilderUtility.SetRef(view, "issueListContainer", issuesRow);
            UGUIBuilderUtility.SetRef(view, "issueTextPrefab", issueTextPrefab);

            return view;
        }

        /// <summary>「ラベル + 横に伸びるゲージ(+任意でオーバーフロー用の2本目)」の1行を作る。</summary>
        static (Text label, Image fill, Image overflowFill) BuildBarRow(RectTransform parent, string name, string initialText)
        {
            RectTransform row = UGUIBuilderUtility.CreateUIObject(name, parent);
            UGUIBuilderUtility.AddFixedHeight(row.gameObject, 30f);
            UGUIBuilderUtility.AddHorizontalLayout(row.gameObject, 10f);

            Text label = UGUIBuilderUtility.CreateText($"{name}_Label", row, initialText, 18, TextAnchor.MiddleLeft);
            UGUIBuilderUtility.AddFixedWidth(label.gameObject, 260f);

            RectTransform barBg = UGUIBuilderUtility.CreateUIObject($"{name}_BarBg", row);
            barBg.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            UGUIBuilderUtility.AddFlexibleWidth(barBg.gameObject);

            Image fill = UGUIBuilderUtility.CreateFillBar($"{name}_Fill", barBg, Color.white);
            UGUIBuilderUtility.Stretch(fill.rectTransform);

            // 100%まで埋まった fill の上に重ねて描く「超過分」の帯。容器いっぱいをもう一枚重ねて、
            // fillAmount = clamp01(ratio-1) だけ埋めることで「どれだけ超過しているか」を示す簡易表現。
            // 100%地点の右に継ぎ足す表現ではないが、色と量の両方で超過の程度を読み取れる(D-9の趣旨)。
            Image overflowFill = UGUIBuilderUtility.CreateFillBar($"{name}_OverflowFill", barBg, Color.red);
            UGUIBuilderUtility.Stretch(overflowFill.rectTransform);
            overflowFill.gameObject.SetActive(false);

            return (label, fill, overflowFill);
        }

        static (Button save, Button load, Button deploy) BuildTopBar(RectTransform canvasRoot)
        {
            RectTransform bar = UGUIBuilderUtility.CreateUIObject("TopBar", canvasRoot);
            UGUIBuilderUtility.AnchorTopBar(bar, TopBarHeight);
            bar.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
            UGUIBuilderUtility.AddHorizontalLayout(bar.gameObject, 12f);
            var padded = bar.GetComponent<HorizontalLayoutGroup>();
            padded.padding = new RectOffset(16, 16, 10, 10);
            padded.childAlignment = TextAnchor.MiddleLeft;
            padded.childForceExpandWidth = false;

            Text titleText = UGUIBuilderUtility.CreateText("Title", bar, "ガレージ", 26, TextAnchor.MiddleLeft);
            UGUIBuilderUtility.AddFixedWidth(titleText.gameObject, 220f);

            // 余白を右詰めにするための伸縮スペーサー。
            RectTransform spacer = UGUIBuilderUtility.CreateUIObject("Spacer", bar);
            var spacerElement = spacer.gameObject.AddComponent<LayoutElement>();
            spacerElement.flexibleWidth = 1f;

            Button loadButton = UGUIBuilderUtility.CreateButton("LoadButton", bar, "読込", out _);
            UGUIBuilderUtility.AddFixedWidth(loadButton.gameObject, 120f);

            Button saveButton = UGUIBuilderUtility.CreateButton("SaveButton", bar, "保存", out _);
            UGUIBuilderUtility.AddFixedWidth(saveButton.gameObject, 120f);

            Button deployButton = UGUIBuilderUtility.CreateButton("DeployButton", bar, "テスト走行へ", out _);
            UGUIBuilderUtility.AddFixedWidth(deployButton.gameObject, 160f);

            return (saveButton, loadButton, deployButton);
        }

        // --- UI プレハブ生成(初回のみ。既存があれば流用) -------------------------------------

        static SlotEntryView CreateSlotEntryPrefab()
        {
            string path = $"{UIPrefabFolder}/SlotEntry.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<SlotEntryView>(path);
            if (existing != null)
            {
                Debug.LogWarning($"[GarageSceneBuilder] 既存のため上書きしない: {path}");
                return existing;
            }

            var root = new GameObject("SlotEntry", typeof(RectTransform));
            try
            {
                RectTransform rt = root.GetComponent<RectTransform>();
                UGUIBuilderUtility.AddFixedHeight(root, 64f);

                var bg = root.AddComponent<Image>();
                bg.color = new Color(0.16f, 0.16f, 0.20f);
                var button = root.AddComponent<Button>();
                button.targetGraphic = bg;
                var view = root.AddComponent<SlotEntryView>();

                RectTransform highlight = UGUIBuilderUtility.CreateUIObject("SelectedHighlight", rt);
                UGUIBuilderUtility.Stretch(highlight);
                var highlightImage = highlight.gameObject.AddComponent<Image>();
                highlightImage.color = new Color(1f, 0.85f, 0.2f, 0.35f);
                highlightImage.raycastTarget = false;
                highlight.gameObject.SetActive(false);

                Image icon = UGUIBuilderUtility.CreateImage("Icon", rt, Color.white, raycastTarget: false);
                icon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                icon.rectTransform.anchoredPosition = new Vector2(8f, 0f);
                icon.rectTransform.sizeDelta = new Vector2(48f, 48f);
                icon.enabled = false;

                RectTransform texts = UGUIBuilderUtility.CreateUIObject("Texts", rt);
                texts.anchorMin = new Vector2(0f, 0f);
                texts.anchorMax = new Vector2(1f, 1f);
                texts.pivot = new Vector2(0f, 0.5f);
                texts.offsetMin = new Vector2(64f, 4f);
                texts.offsetMax = new Vector2(-8f, -4f);
                UGUIBuilderUtility.AddVerticalLayout(texts.gameObject, 2f);

                Text slotName = UGUIBuilderUtility.CreateText("SlotName", texts, "スロット", 14, TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.75f));
                Text partName = UGUIBuilderUtility.CreateText("PartName", texts, "(未装備)", 18, TextAnchor.MiddleLeft);

                UGUIBuilderUtility.SetRef(view, "slotNameText", slotName);
                UGUIBuilderUtility.SetRef(view, "partNameText", partName);
                UGUIBuilderUtility.SetRef(view, "iconImage", icon);
                UGUIBuilderUtility.SetRef(view, "selectedHighlight", highlight.gameObject);
                UGUIBuilderUtility.SetRef(view, "button", button);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                return prefab.GetComponent<SlotEntryView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static PartEntryView CreatePartEntryPrefab()
        {
            string path = $"{UIPrefabFolder}/PartEntry.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<PartEntryView>(path);
            if (existing != null)
            {
                Debug.LogWarning($"[GarageSceneBuilder] 既存のため上書きしない: {path}");
                return existing;
            }

            var root = new GameObject("PartEntry", typeof(RectTransform));
            try
            {
                RectTransform rt = root.GetComponent<RectTransform>();
                UGUIBuilderUtility.AddFixedHeight(root, 72f);

                var bg = root.AddComponent<Image>();
                bg.color = new Color(0.16f, 0.16f, 0.20f);
                var button = root.AddComponent<Button>();
                button.targetGraphic = bg;
                var view = root.AddComponent<PartEntryView>();

                RectTransform highlight = UGUIBuilderUtility.CreateUIObject("EquippedHighlight", rt);
                UGUIBuilderUtility.Stretch(highlight);
                var highlightImage = highlight.gameObject.AddComponent<Image>();
                highlightImage.color = new Color(0.3f, 0.9f, 0.4f, 0.25f);
                highlightImage.raycastTarget = false;
                highlight.gameObject.SetActive(false);

                Image icon = UGUIBuilderUtility.CreateImage("Icon", rt, Color.white, raycastTarget: false);
                icon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                icon.rectTransform.anchoredPosition = new Vector2(8f, 0f);
                icon.rectTransform.sizeDelta = new Vector2(48f, 48f);
                icon.enabled = false;

                RectTransform texts = UGUIBuilderUtility.CreateUIObject("Texts", rt);
                texts.anchorMin = new Vector2(0f, 0f);
                texts.anchorMax = new Vector2(1f, 1f);
                texts.pivot = new Vector2(0f, 0.5f);
                texts.offsetMin = new Vector2(64f, 4f);
                texts.offsetMax = new Vector2(-8f, -4f);
                UGUIBuilderUtility.AddVerticalLayout(texts.gameObject, 2f);

                Text nameText = UGUIBuilderUtility.CreateText("Name", texts, "(装備しない)", 18, TextAnchor.MiddleLeft);
                Text reasonText = UGUIBuilderUtility.CreateText("Reason", texts, string.Empty, 13, TextAnchor.MiddleLeft, new Color(1f, 0.65f, 0f));

                UGUIBuilderUtility.SetRef(view, "iconImage", icon);
                UGUIBuilderUtility.SetRef(view, "nameText", nameText);
                UGUIBuilderUtility.SetRef(view, "reasonText", reasonText);
                UGUIBuilderUtility.SetRef(view, "equippedHighlight", highlight.gameObject);
                UGUIBuilderUtility.SetRef(view, "button", button);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                return prefab.GetComponent<PartEntryView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static CapabilityIconView CreateCapabilityIconPrefab()
        {
            string path = $"{UIPrefabFolder}/CapabilityIcon.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<CapabilityIconView>(path);
            if (existing != null)
            {
                Debug.LogWarning($"[GarageSceneBuilder] 既存のため上書きしない: {path}");
                return existing;
            }

            var root = new GameObject("CapabilityIcon", typeof(RectTransform));
            try
            {
                // アイコン(絵)+ 表示名(文字)の横並び。CapabilityIconSet の icon は v1 では
                // 全件 null なので、文字が無いと同じ白い四角が最大9個並ぶだけになり、
                // どの能力が剥奪されたのか判別できない(§11-3 / D-9)。
                UGUIBuilderUtility.AddFixedWidth(root, 96f);
                UGUIBuilderUtility.AddFixedHeight(root, 40f);
                UGUIBuilderUtility.AddHorizontalLayout(root, 4f);

                var view = root.AddComponent<CapabilityIconView>();

                Image iconImage = UGUIBuilderUtility.CreateImage("Icon", root.transform, Color.white, raycastTarget: false);
                UGUIBuilderUtility.AddFixedWidth(iconImage.gameObject, 32f);
                UGUIBuilderUtility.AddFixedHeight(iconImage.gameObject, 32f);

                Text labelText = UGUIBuilderUtility.CreateText("Label", root.transform, string.Empty, 13, TextAnchor.MiddleLeft);
                UGUIBuilderUtility.AddFlexibleWidth(labelText.gameObject);

                // 「剥奪」を示す取り消し線の代わりに、斜めの細い赤線を重ねる(任意演出。CLAUDE.md D-9)。
                // アイコンの子に置く。ルート直下だとレイアウトグループに1要素として並べられてしまう。
                RectTransform strike = UGUIBuilderUtility.CreateUIObject("StrikeLine", iconImage.transform);
                strike.sizeDelta = new Vector2(44f, 4f);
                strike.anchorMin = strike.anchorMax = new Vector2(0.5f, 0.5f);
                strike.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var strikeImage = strike.gameObject.AddComponent<Image>();
                strikeImage.color = new Color(0.85f, 0.15f, 0.15f, 0.9f);
                strikeImage.raycastTarget = false;
                strike.gameObject.SetActive(false);

                UGUIBuilderUtility.SetRef(view, "iconImage", iconImage);
                UGUIBuilderUtility.SetRef(view, "labelText", labelText);
                UGUIBuilderUtility.SetRef(view, "strippedOverlay", strike.gameObject);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                return prefab.GetComponent<CapabilityIconView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static Text CreateIssueTextPrefab()
        {
            string path = $"{UIPrefabFolder}/IssueText.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<Text>(path);
            if (existing != null)
            {
                Debug.LogWarning($"[GarageSceneBuilder] 既存のため上書きしない: {path}");
                return existing;
            }

            var root = new GameObject("IssueText", typeof(RectTransform));
            try
            {
                var text = root.AddComponent<Text>();
                text.font = UGUIBuilderUtility.DefaultFont;
                text.fontSize = 15;
                text.alignment = TextAnchor.MiddleLeft;
                text.color = Color.white;
                text.raycastTarget = false;
                UGUIBuilderUtility.AddFixedHeight(root, 20f);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                return prefab.GetComponent<Text>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static CapabilityIconSet CreateCapabilityIconSet()
        {
            var existing = AssetDatabase.LoadAssetAtPath<CapabilityIconSet>(CapabilityIconSetPath);
            if (existing != null)
            {
                Debug.LogWarning($"[GarageSceneBuilder] 既存のため上書きしない: {CapabilityIconSetPath}");
                return existing;
            }

            var asset = ScriptableObject.CreateInstance<CapabilityIconSet>();
            AssetDatabase.CreateAsset(asset, CapabilityIconSetPath);

            var entries = new (CapabilityFlags flag, string label)[]
            {
                (CapabilityFlags.Walk, "歩行"),
                (CapabilityFlags.Run, "走行"),
                (CapabilityFlags.Jump, "ジャンプ"),
                (CapabilityFlags.Hover, "ホバー"),
                (CapabilityFlags.Dash, "ダッシュ"),
                (CapabilityFlags.Crouch, "しゃがみ"),
                (CapabilityFlags.GrabLeft, "左手持ち"),
                (CapabilityFlags.GrabRight, "右手持ち"),
                (CapabilityFlags.Emote, "エモート"),
            };

            var serialized = new SerializedObject(asset);
            SerializedProperty list = serialized.FindProperty("mappings");
            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("flag").intValue = (int)entries[i].flag;
                element.FindPropertyRelative("displayName").stringValue = entries[i].label;
                element.FindPropertyRelative("icon").objectReferenceValue = null;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            return asset;
        }

        // --- 雑多なユーティリティ ---------------------------------------------------------

        static void SetSceneName(SceneTransitionButton button, string sceneName)
        {
            var serialized = new SerializedObject(button);
            serialized.FindProperty("sceneName").stringValue = sceneName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void EnsureInBuildSettings(string scenePath, bool onlyIfExists = false)
        {
            if (onlyIfExists && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath) == null)
            {
                return;
            }

            var scenes = EditorBuildSettings.scenes;
            foreach (EditorBuildSettingsScene s in scenes)
            {
                if (s.path == scenePath)
                {
                    return;
                }
            }

            var list = new List<EditorBuildSettingsScene>(scenes) { new EditorBuildSettingsScene(scenePath, true) };
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
