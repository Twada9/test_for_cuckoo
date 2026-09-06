using ModularMech.Data;
using ModularMech.Mechs;
using UnityEditor;
using UnityEngine;

namespace ModularMech.EditorTools
{
    /// <summary>
    /// 「プレースホルダ生成 → ガレージ構築 → テストフィールド構築」を順番に実行するための
    /// 単一のウィンドウ(設計ドキュメント §9 M8)。
    ///
    /// 初見の人がこのウィンドウだけで v1 を一通り動かせることを目標にしている。各ステップの前提
    /// (前段のアセットが無い等)を検査し、足りないものを日本語で具体的に表示する。
    /// 実際の生成処理そのものは <see cref="PlaceholderPartGenerator"/> /
    /// <see cref="GarageSceneBuilder"/> / <see cref="TestFieldSceneBuilder"/> にすべて委譲する
    /// (このウィンドウはそれらの呼び出しと状態表示だけを担当する)。
    /// </summary>
    public sealed class ModularMechSetupWindow : EditorWindow
    {
        const string CatalogPath = "Assets/ScriptableObjects/PartCatalog.asset";
        const string MechPrefabPath = "Assets/Prefabs/Mech/Mech_Placeholder.prefab";
        const string GarageScenePath = "Assets/Scenes/Garage.unity";
        const string TestFieldScenePath = "Assets/Scenes/TestField.unity";

        Vector2 _scroll;

        [MenuItem("Tools/ModularMech/Setup Window")]
        public static void Open()
        {
            var window = GetWindow<ModularMechSetupWindow>(utility: false, title: "ModularMech Setup");
            window.minSize = new Vector2(480f, 420f);
            window.Show();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("ModularMech v1 セットアップ", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "上から順に実行すること。各ステップは既存アセットを上書きしない" +
                "(既にある場合は Console に警告が出るだけで、そのまま次へ進んで問題ない)。\n" +
                "この環境では Unity 上での動作確認ができていない。各ステップの結果は必ず Console と" +
                "実際のシーン内容を自分の目で確認すること。",
                MessageType.Info);

            EditorGUILayout.Space(8f);
            DrawPlayerSettingsReminder();

            EditorGUILayout.Space(12f);
            DrawStep1();

            EditorGUILayout.Space(12f);
            DrawStep2();

            EditorGUILayout.Space(12f);
            DrawStep3();

            EditorGUILayout.Space(16f);
            DrawRunAll();

            EditorGUILayout.EndScrollView();
        }

        // --- 事前注意 ----------------------------------------------------------------------

        static void DrawPlayerSettingsReminder()
        {
            EditorGUILayout.LabelField("初回のみ: Player Settings の確認", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "・URP の Render Pipeline Asset が Graphics 設定に割り当たっていること" +
                "(未設定だと URP 前提のマテリアルがマゼンタになる)。\n" +
                "・Active Input Handling が『Input Manager (Old)』または『Both』であること" +
                "(移動入力とボタン操作は旧 Input Manager 前提で書かれている)。\n" +
                "詳細は ModularMech/README.md を参照。",
                MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Player Settings を開く"))
                {
                    SettingsService.OpenProjectSettings("Project/Player");
                }

                if (GUILayout.Button("Graphics 設定を開く"))
                {
                    SettingsService.OpenProjectSettings("Project/Graphics");
                }
            }
        }

        // --- ステップ1: プレースホルダ生成 --------------------------------------------------

        void DrawStep1()
        {
            bool catalogExists = AssetExists<PartCatalog>(CatalogPath);
            bool mechExists = AssetExists<GameObject>(MechPrefabPath);
            bool done = catalogExists && mechExists;

            EditorGUILayout.LabelField(StepLabel(1, "プレースホルダ生成", done), EditorStyles.boldLabel);
            DrawChecklistItem("PartCatalog.asset", catalogExists, CatalogPath);
            DrawChecklistItem("Mech_Placeholder.prefab", mechExists, MechPrefabPath);

            if (GUILayout.Button("① プレースホルダ一式を生成する"))
            {
                PlaceholderPartGenerator.GenerateAll();
            }
        }

        // --- ステップ2: ガレージ構築 ---------------------------------------------------------

        void DrawStep2()
        {
            bool prereqDone = AssetExists<PartCatalog>(CatalogPath) && AssetExists<GameObject>(MechPrefabPath);
            bool sceneExists = AssetExists<SceneAsset>(GarageScenePath);

            EditorGUILayout.LabelField(StepLabel(2, "ガレージシーン構築", sceneExists), EditorStyles.boldLabel);
            DrawChecklistItem("Garage.unity", sceneExists, GarageScenePath);

            if (!prereqDone)
            {
                EditorGUILayout.HelpBox(
                    "① が未完了(PartCatalog.asset または Mech_Placeholder.prefab が無い)。" +
                    "先に①を実行すること。",
                    MessageType.Error);
            }
            else if (sceneExists)
            {
                EditorGUILayout.HelpBox(
                    "既に Garage.unity が存在する。作り直したい場合は先にファイル(と .meta)を削除すること。",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!prereqDone))
            {
                if (GUILayout.Button("② ガレージシーンを構築する"))
                {
                    GarageSceneBuilder.BuildScene();
                }
            }
        }

        // --- ステップ3: テストフィールド構築 -------------------------------------------------

        void DrawStep3()
        {
            bool prereqDone = AssetExists<PartCatalog>(CatalogPath) && AssetExists<GameObject>(MechPrefabPath);
            bool sceneExists = AssetExists<SceneAsset>(TestFieldScenePath);

            EditorGUILayout.LabelField(StepLabel(3, "テストフィールドシーン構築", sceneExists), EditorStyles.boldLabel);
            DrawChecklistItem("TestField.unity", sceneExists, TestFieldScenePath);

            if (!prereqDone)
            {
                EditorGUILayout.HelpBox(
                    "① が未完了(PartCatalog.asset または Mech_Placeholder.prefab が無い)。" +
                    "先に①を実行すること。",
                    MessageType.Error);
            }
            else if (sceneExists)
            {
                EditorGUILayout.HelpBox(
                    "既に TestField.unity が存在する。作り直したい場合は先にファイル(と .meta)を削除すること。",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!prereqDone))
            {
                if (GUILayout.Button("③ テストフィールドシーンを構築する"))
                {
                    TestFieldSceneBuilder.BuildScene();
                }
            }
        }

        // --- まとめて実行 -------------------------------------------------------------------

        void DrawRunAll()
        {
            EditorGUILayout.LabelField("まとめて実行", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "①→②→③ を順番に呼ぶだけのショートカット。既に存在するものは各ステップが" +
                "自分で警告を出してスキップする(壊れない)。",
                MessageType.None);

            if (GUILayout.Button("① → ② → ③ をまとめて実行する", GUILayout.Height(28f)))
            {
                PlaceholderPartGenerator.GenerateAll();
                GarageSceneBuilder.BuildScene();
                TestFieldSceneBuilder.BuildScene();
            }
        }

        // --- 表示ヘルパー -------------------------------------------------------------------

        static string StepLabel(int index, string title, bool done)
        {
            return $"{(done ? "✓" : "・")} {index}. {title}";
        }

        static void DrawChecklistItem(string label, bool exists, string path)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(exists ? "  [済]" : "  [未]", GUILayout.Width(40f));
                EditorGUILayout.LabelField($"{label}  ({path})");
            }
        }

        static bool AssetExists<T>(string path) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path) != null;
        }
    }
}
