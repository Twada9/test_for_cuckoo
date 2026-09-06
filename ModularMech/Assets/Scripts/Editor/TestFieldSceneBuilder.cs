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
    /// テスト走行画面のシーン(<c>Assets/Scenes/TestField.unity</c>)をスクリプトから組み立てる
    /// (設計ドキュメント §6.2 / §9 M8)。
    ///
    /// 平坦な地形 + 段差数個 + スロープ数個、機体のスポーン、追従カメラ、ガレージへ戻る導線を用意する。
    /// 地形は「Ground」レイヤーへ統一し、機体の <c>MechLocomotionController.groundMask</c>
    /// (ホバー脚の高度維持が使う)をそのレイヤーだけに絞る――既定の Everything のままでも動作は
    /// するが、地形以外(将来 UI 用コライダ等)を誤って拾わないよう明示的に揃えておく。
    ///
    /// 前提・上書きしない方針は <see cref="GarageSceneBuilder"/> と同じ。
    /// </summary>
    public static class TestFieldSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/TestField.unity";
        const string CatalogPath = "Assets/ScriptableObjects/PartCatalog.asset";
        const string MechPrefabPath = "Assets/Prefabs/Mech/Mech_Placeholder.prefab";
        const string MaterialFolder = "Assets/Materials/Placeholder";
        const string GroundLayerName = "Ground";

        [MenuItem("Tools/ModularMech/Build Test Field Scene")]
        public static void BuildScene()
        {
            if (!TryLoadPrerequisites(out PartCatalog catalog, out GameObject mechPrefab))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ScenePath) != null)
            {
                Debug.LogWarning(
                    $"[TestFieldSceneBuilder] 既に {ScenePath} が存在するため何もしない。" +
                    "作り直す場合は該当ファイル(と .meta)を削除してから再実行すること。");
                return;
            }

            UGUIBuilderUtility.EnsureFolder("Assets/Scenes");
            UGUIBuilderUtility.EnsureFolder(MaterialFolder);

            int groundLayer = UGUIBuilderUtility.EnsureLayer(GroundLayerName);
            LayerMask groundMask = groundLayer >= 0 ? (LayerMask)(1 << groundLayer) : (LayerMask)~0;
            if (groundLayer < 0)
            {
                Debug.LogWarning(
                    "[TestFieldSceneBuilder] 'Ground' レイヤーを確保できなかった(空きユーザーレイヤーが無い)。" +
                    "groundMask は Everything のままにする(動作はするが地形限定にはならない)。");
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            UGUIBuilderUtility.CreateLegacyEventSystem();

            BuildLighting();
            BuildTerrain(groundLayer);

            Transform mechTransform = BuildMech(mechPrefab, groundMask, out MechRuntime mechRuntime);
            BuildFollowCamera(mechTransform);
            BuildHud(mechRuntime, catalog);

            GarageSceneBuilder.EnsureInBuildSettings(ScenePath);
            GarageSceneBuilder.EnsureInBuildSettings("Assets/Scenes/Garage.unity", onlyIfExists: true);

            bool saved = EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (saved)
            {
                Debug.Log($"[TestFieldSceneBuilder] {ScenePath} を作成した。");
            }
            else
            {
                Debug.LogError($"[TestFieldSceneBuilder] {ScenePath} の保存に失敗した。");
            }
        }

        static bool TryLoadPrerequisites(out PartCatalog catalog, out GameObject mechPrefab)
        {
            catalog = AssetDatabase.LoadAssetAtPath<PartCatalog>(CatalogPath);
            mechPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MechPrefabPath);

            if (catalog == null)
            {
                Debug.LogError(
                    $"[TestFieldSceneBuilder] {CatalogPath} が見つからない。" +
                    "先に Tools/ModularMech/Generate Placeholder Assets を実行すること。");
                return false;
            }

            if (mechPrefab == null || mechPrefab.GetComponent<MechRuntime>() == null)
            {
                Debug.LogError(
                    $"[TestFieldSceneBuilder] {MechPrefabPath} が無い、または MechRuntime が付いていない。" +
                    "先に Tools/ModularMech/Generate Placeholder Assets を実行すること。");
                catalog = null;
                mechPrefab = null;
                return false;
            }

            return true;
        }

        // --- 地形 ------------------------------------------------------------------------

        static void BuildLighting()
        {
            var lightGo = new GameObject("Sun", typeof(Light));
            Light light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.26f, 0.28f);
        }

        /// <summary>平坦な地形 + 段差数個 + スロープ数個(設計ドキュメント §6.2)。</summary>
        static void BuildTerrain(int groundLayer)
        {
            var terrainRoot = new GameObject("Terrain");

            Material groundMat = UGUIBuilderUtility.GetOrCreateMaterial(
                $"{MaterialFolder}/Placeholder_Ground.mat", new Color(0.30f, 0.42f, 0.30f));
            Material stepMat = UGUIBuilderUtility.GetOrCreateMaterial(
                $"{MaterialFolder}/Placeholder_Step.mat", new Color(0.45f, 0.45f, 0.48f));
            Material rampMat = UGUIBuilderUtility.GetOrCreateMaterial(
                $"{MaterialFolder}/Placeholder_Ramp.mat", new Color(0.35f, 0.40f, 0.50f));

            CreateBlock(
                "Ground", terrainRoot.transform,
                position: new Vector3(0f, -0.5f, 0f), eulerAngles: Vector3.zero,
                scale: new Vector3(60f, 1f, 100f), groundLayer, groundMat);

            // 段差: 高さが少しずつ増える5段の階段。1段あたりの増分は CharacterController の既定
            // stepOffset(0.3)以下に収め、ジャンプ無しでも登れる高さに揃えてある。
            const float stepHeight = 0.28f;
            const float stepDepth = 2.2f;
            const int stepCount = 5;
            for (int i = 0; i < stepCount; i++)
            {
                float height = stepHeight * (i + 1);
                CreateBlock(
                    $"Step_{i}", terrainRoot.transform,
                    position: new Vector3(-16f, height / 2f, i * stepDepth),
                    eulerAngles: Vector3.zero,
                    scale: new Vector3(4f, height, stepDepth),
                    groundLayer, stepMat);
            }

            // スロープ: 傾斜の違う2本。
            CreateRamp(terrainRoot.transform, "Ramp_Gentle", new Vector3(16f, 0f, 2f), 10f, 8f, groundLayer, rampMat);
            CreateRamp(terrainRoot.transform, "Ramp_Steep", new Vector3(24f, 0f, 2f), 22f, 6f, groundLayer, rampMat);
        }

        static void CreateRamp(Transform parent, string name, Vector3 basePosition, float angleDegrees, float length, int layer, Material material)
        {
            const float thickness = 0.4f;
            const float width = 4f;

            float halfLength = length / 2f;
            float rad = angleDegrees * Mathf.Deg2Rad;
            // 底辺(地面と接する側)の高さを 0 に揃え、そこから角度分だけ傾けて持ち上げる。
            Vector3 position = basePosition + new Vector3(0f, halfLength * Mathf.Sin(rad), halfLength * Mathf.Cos(rad));

            CreateBlock(
                name, parent,
                position, new Vector3(-angleDegrees, 0f, 0f),
                new Vector3(width, thickness, length), layer, material);
        }

        static GameObject CreateBlock(
            string name, Transform parent, Vector3 position, Vector3 eulerAngles, Vector3 scale, int layer, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.position = position;
            block.transform.eulerAngles = eulerAngles;
            block.transform.localScale = scale;

            if (layer >= 0)
            {
                block.layer = layer;
            }

            if (material != null)
            {
                block.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            return block;
        }

        // --- 機体 ------------------------------------------------------------------------

        static Transform BuildMech(GameObject mechPrefab, LayerMask groundMask, out MechRuntime mechRuntime)
        {
            var mechInstance = (GameObject)PrefabUtility.InstantiatePrefab(mechPrefab);
            mechInstance.name = "Mech";
            mechInstance.transform.position = new Vector3(0f, 1.05f, -20f);
            mechInstance.transform.rotation = Quaternion.identity;

            mechRuntime = mechInstance.GetComponent<MechRuntime>();

            var locomotion = mechInstance.GetComponent<MechLocomotionController>();
            if (locomotion != null)
            {
                SetGroundMask(locomotion, groundMask);
            }

            return mechInstance.transform;
        }

        static void SetGroundMask(MechLocomotionController controller, LayerMask mask)
        {
            var serialized = new SerializedObject(controller);
            SerializedProperty property = serialized.FindProperty("groundMask");
            if (property == null)
            {
                Debug.LogWarning("[TestFieldSceneBuilder] MechLocomotionController.groundMask が見つからない。既定のままにする。");
                return;
            }

            property.intValue = mask.value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildFollowCamera(Transform mechTransform)
        {
            var cameraGo = new GameObject("FollowCamera", typeof(Camera));
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;

            var follow = cameraGo.AddComponent<MechFollowCamera>();
            UGUIBuilderUtility.SetRef(follow, "target", mechTransform);

            // 初期フレームから機体が視界に入るよう、スポーン地点に合わせて先に配置しておく。
            cameraGo.transform.position = mechTransform.position + new Vector3(0f, 3.5f, -6f);
            cameraGo.transform.LookAt(mechTransform.position + Vector3.up * 1.2f, Vector3.up);
        }

        // --- HUD -------------------------------------------------------------------------

        static void BuildHud(MechRuntime mechRuntime, PartCatalog catalog)
        {
            Canvas canvas = UGUIBuilderUtility.CreateScreenCanvas("Canvas");
            RectTransform canvasRoot = canvas.GetComponent<RectTransform>();

            var screen = canvasRoot.gameObject.AddComponent<TestFieldScreen>();
            UGUIBuilderUtility.SetRef(screen, "mechRuntime", mechRuntime);
            UGUIBuilderUtility.SetRef(screen, "partCatalog", catalog);

            RectTransform topLeft = UGUIBuilderUtility.CreateUIObject("TopLeft", canvasRoot);
            topLeft.anchorMin = new Vector2(0f, 1f);
            topLeft.anchorMax = new Vector2(0f, 1f);
            topLeft.pivot = new Vector2(0f, 1f);
            topLeft.anchoredPosition = new Vector2(16f, -16f);
            topLeft.sizeDelta = new Vector2(200f, 48f);

            Button backButton = UGUIBuilderUtility.CreateButton("BackButton", topLeft, "ガレージへ戻る", out _);
            UGUIBuilderUtility.Stretch(backButton.GetComponent<RectTransform>());

            var transition = backButton.gameObject.AddComponent<SceneTransitionButton>();
            SetSceneName(transition, "Garage");
            UnityEventTools.AddPersistentListener(backButton.onClick, transition.LoadScene);
            EditorUtility.SetDirty(backButton);

            Text controlsHint = UGUIBuilderUtility.CreateText(
                "ControlsHint", canvasRoot,
                "W/S: 前進・後退  A/D: 旋回  Shift: 走行  Space: ジャンプ  Ctrl: しゃがみ  G: エモート",
                16, TextAnchor.LowerLeft, new Color(1f, 1f, 1f, 0.75f));
            controlsHint.rectTransform.anchorMin = new Vector2(0f, 0f);
            controlsHint.rectTransform.anchorMax = new Vector2(1f, 0f);
            controlsHint.rectTransform.pivot = new Vector2(0f, 0f);
            controlsHint.rectTransform.sizeDelta = new Vector2(-32f, 28f);
            controlsHint.rectTransform.anchoredPosition = new Vector2(16f, 12f);
        }

        static void SetSceneName(SceneTransitionButton button, string sceneName)
        {
            var serialized = new SerializedObject(button);
            serialized.FindProperty("sceneName").stringValue = sceneName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
