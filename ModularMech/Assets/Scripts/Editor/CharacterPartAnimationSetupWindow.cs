using System.IO;
using ModularMech.Data;
using ModularMech.Mechs;
using UnityEditor;
using UnityEngine;

namespace ModularMech.EditorTools
{
    /// <summary>
    /// 「モデルを一旦シーンに置く → Animator を設定する → プレハブ化する → 元に戻す」という
    /// 手作業の連続を、ボタン1つで自動化するウィンドウ(設計ドキュメントの対象外の補助ツール)。
    ///
    /// <para>
    /// VRM 等の装飾用モデルは <see cref="CosmeticLocomotionAnimator"/> で機体の移動速度と
    /// 連動させるが、モデル自体は生の Model アセット(または Prefab)であり、
    /// Animator の Controller や追加コンポーネントは Play モード中に直接いじっても
    /// 保存されない(実行時に複製されたインスタンスにすぎないため)。恒久的に反映するには
    /// 「Controller とコンポーネントを設定済みの新しい Prefab」を作る必要があり、これが
    /// 初めて Unity を触る人には分かりにくい。このウィンドウはその手順を1回のボタン操作に
    /// まとめる。
    /// </para>
    /// </summary>
    public sealed class CharacterPartAnimationSetupWindow : EditorWindow
    {
        private GameObject _sourceModel;
        private RuntimeAnimatorController _controller;
        private string _speedParameter = "Speed";
        private PartDefinition _partDefinitionToUpdate;
        private string _outputFolder = "Assets/Prefabs/Parts";

        [MenuItem("Tools/ModularMech/Character Part Animation Setup")]
        public static void Open()
        {
            GetWindow<CharacterPartAnimationSetupWindow>("キャラアニメ設定");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "VRM 等のモデルに Animator Controller と CosmeticLocomotionAnimator を" +
                "設定した新しいプレハブを自動生成する。手作業でシーンに出し入れする必要はない。",
                MessageType.None);

            EditorGUILayout.Space(8);

            _sourceModel = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("元のモデル", "VRM 等、動かしたいキャラクターモデル(アセット)。"),
                _sourceModel, typeof(GameObject), false);

            _controller = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                new GUIContent("Animator Controller", "モデルに割り当てる歩行/待機用の Controller。"),
                _controller, typeof(RuntimeAnimatorController), false);

            _speedParameter = EditorGUILayout.TextField(
                new GUIContent("Speed パラメータ名", "Controller 側の float パラメータ名と一致させること。"),
                _speedParameter);

            EditorGUILayout.Space(4);

            _outputFolder = EditorGUILayout.TextField(
                new GUIContent("出力先フォルダ", "新しいプレハブを保存する場所。"),
                _outputFolder);

            EditorGUILayout.Space(8);

            _partDefinitionToUpdate = (PartDefinition)EditorGUILayout.ObjectField(
                new GUIContent("(任意) 反映先の Part Definition",
                    "指定すると、生成したプレハブをこの PartDefinition の Mesh Prefab へ自動で差し替える。"),
                _partDefinitionToUpdate, typeof(PartDefinition), false);

            EditorGUILayout.Space(12);

            using (new EditorGUI.DisabledScope(_sourceModel == null || _controller == null))
            {
                if (GUILayout.Button("プレハブを生成する", GUILayout.Height(32)))
                {
                    CreatePrefab();
                }
            }
        }

        private void CreatePrefab()
        {
            if (!AssetDatabase.IsValidFolder(_outputFolder))
            {
                Directory.CreateDirectory(_outputFolder);
                AssetDatabase.Refresh();
            }

            string baseName = _sourceModel.name;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{_outputFolder}/{baseName}_Animated.prefab");

            // シーンを一切汚さないよう、非表示のフラグを付けた一時インスタンスで組み立てる。
            GameObject instance = Instantiate(_sourceModel);
            instance.hideFlags = HideFlags.DontSave;
            try
            {
                var animator = instance.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = instance.AddComponent<Animator>();
                }
                animator.runtimeAnimatorController = _controller;

                var cosmetic = instance.GetComponent<CosmeticLocomotionAnimator>();
                if (cosmetic == null)
                {
                    cosmetic = instance.AddComponent<CosmeticLocomotionAnimator>();
                }

                var serialized = new SerializedObject(cosmetic);
                SerializedProperty speedProp = serialized.FindProperty("speedParameter");
                if (speedProp != null)
                {
                    speedProp.stringValue = _speedParameter;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, path);

                if (_partDefinitionToUpdate != null)
                {
                    var partSerialized = new SerializedObject(_partDefinitionToUpdate);
                    SerializedProperty meshProp = partSerialized.FindProperty("meshPrefab");
                    if (meshProp != null)
                    {
                        meshProp.objectReferenceValue = savedPrefab;
                        partSerialized.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(_partDefinitionToUpdate);
                    }
                    else
                    {
                        Debug.LogError(
                            "[CharacterPartAnimationSetupWindow] PartDefinition.meshPrefab が見つからない。" +
                            "手動で Mesh Prefab 欄に差し替えること。");
                    }
                }

                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(savedPrefab);
                Debug.Log($"[CharacterPartAnimationSetupWindow] 作成した: {path}" +
                    (_partDefinitionToUpdate != null ? $"(PartDefinition '{_partDefinitionToUpdate.name}' の Mesh Prefab を更新済み)" : ""));
            }
            finally
            {
                DestroyImmediate(instance);
            }
        }
    }
}
