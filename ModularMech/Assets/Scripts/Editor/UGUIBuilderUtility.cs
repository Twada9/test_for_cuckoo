using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModularMech.EditorTools
{
    /// <summary>
    /// <see cref="GarageSceneBuilder"/> / <see cref="TestFieldSceneBuilder"/> が共通で使う、
    /// uGUI 階層の組み立てと SerializedObject 経由の参照結線をまとめたヘルパー。
    ///
    /// このファイル自体は公開 API ではない(EditorTools 内部の実装共有のためだけに存在する)。
    /// PlaceholderPartGenerator と同じ流儀(既存アセットは上書きしない / SerializedObject で
    /// private フィールドへ直接値を入れる)を踏襲する。
    /// </summary>
    internal static class UGUIBuilderUtility
    {
        // --- RectTransform 基本操作 ---------------------------------------------------

        public static RectTransform CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        /// <summary>親いっぱいに広げる(オフセット無し)。</summary>
        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>四辺からの余白を指定して広げる。</summary>
        public static void AnchorInset(RectTransform rt, float left, float right, float top, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>左端に張り付く縦ストレッチの帯(スロット一覧などに使う)。</summary>
        public static void AnchorLeftColumn(RectTransform rt, float width, float topInset, float bottomInset)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(0f, bottomInset);
            rt.offsetMax = new Vector2(width, -topInset);
        }

        /// <summary>右端に張り付く縦ストレッチの帯(パーツ一覧などに使う)。</summary>
        public static void AnchorRightColumn(RectTransform rt, float width, float topInset, float bottomInset)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.offsetMin = new Vector2(-width, bottomInset);
            rt.offsetMax = new Vector2(0f, -topInset);
        }

        /// <summary>上端に張り付く横ストレッチの帯。</summary>
        public static void AnchorTopBar(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -height);
            rt.offsetMax = new Vector2(0f, 0f);
        }

        /// <summary>下端に張り付く横ストレッチの帯(ステータスパネルに使う)。</summary>
        public static void AnchorBottomBar(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, height);
        }

        // --- 基本ウィジェット -----------------------------------------------------------

        public static Font DefaultFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Text CreateText(
            string name, Transform parent, string content, int fontSize = 18,
            TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            RectTransform rt = CreateUIObject(name, parent);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = anchor;
            text.color = color ?? Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        public static Image CreateImage(string name, Transform parent, Color color, bool raycastTarget = true)
        {
            RectTransform rt = CreateUIObject(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        /// <summary>装備率バーなどに使う、水平方向に Fill する Image。</summary>
        public static Image CreateFillBar(string name, Transform parent, Color color)
        {
            Image image = CreateImage(name, parent, color, raycastTarget: false);
            // sprite が null のままだと Image.OnPopulateMesh が Filled 系の設定を素通りして
            // 常に矩形フルクアッドを描く(= 常に満杯に見える)。組み込みの白スプライトを明示的に積む。
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 0f;
            return image;
        }

        public static Button CreateButton(string name, Transform parent, string label, out Text labelText)
        {
            RectTransform rt = CreateUIObject(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = new Color(0.22f, 0.24f, 0.30f);

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            labelText = CreateText($"{name}_Label", rt, label, 20, TextAnchor.MiddleCenter);
            Stretch(labelText.rectTransform);

            return button;
        }

        public static LayoutElement AddFixedHeight(GameObject go, float height)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            return element;
        }

        public static LayoutElement AddFixedWidth(GameObject go, float width)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            return element;
        }

        /// <summary>
        /// 残り幅を埋める伸縮要素。素の Image は ILayoutElement を実装しない(サイズの希望を
        /// 持たない)ため、childForceExpandWidth=false な HorizontalLayoutGroup の下では
        /// 明示しないと幅0に潰れる(ゲージのバー背景などで必要)。
        /// </summary>
        public static LayoutElement AddFlexibleWidth(GameObject go, float flex = 1f)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.flexibleWidth = flex;
            return element;
        }

        // --- 複合コンポーネント ----------------------------------------------------------

        public static Canvas CreateScreenCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        /// <summary>
        /// 旧 Input Manager 前提の StandaloneInputModule を使う EventSystem。
        /// KeyboardMechInputSource が ENABLE_LEGACY_INPUT_MANAGER 前提で書かれているのに合わせてある。
        /// Player Settings の Active Input Handling を「Input Manager (Old)」または「Both」にしないと
        /// UI のクリックも機体の移動も反応しない(README 参照)。
        /// </summary>
        public static EventSystem CreateLegacyEventSystem()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return go.GetComponent<EventSystem>();
        }

        /// <summary>
        /// 縦スクロールの一覧コンテナを組む。戻り値は Content の RectTransform
        /// (SlotListView.entryContainer / PartListView.entryContainer に渡す)。
        /// </summary>
        public static RectTransform CreateVerticalScrollList(string name, Transform parent, float spacing = 6f)
        {
            RectTransform root = CreateUIObject(name, parent);
            Stretch(root);
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.08f);
            var scrollRect = root.gameObject.AddComponent<ScrollRect>();

            RectTransform viewport = CreateUIObject("Viewport", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateUIObject("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = spacing;
            layout.padding = new RectOffset(4, 4, 4, 4);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = content;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            return content;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(GameObject go, float spacing = 6f)
        {
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
            return layout;
        }

        public static VerticalLayoutGroup AddVerticalLayout(GameObject go, float spacing = 4f)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        // --- アセット / 参照結線 ----------------------------------------------------------

        /// <summary>SerializedObject 経由で private フィールドへ Object 参照を入れる(挿し忘れ対策)。</summary>
        public static void SetRef(Object target, string propertyName, Object value)
        {
            if (target == null)
            {
                return;
            }

            // value 自体が null(= 呼び出し元で参照の取得・生成に失敗していた)場合、
            // 代入自体は成立してしまい例外も出ないため、後になって Play モードでの
            // NullReference/未配線エラーとして遅れて発覚する。ここで一次発生源を特定できるよう、
            // 呼び出し側のスタックトレースごと即座に警告する。
            if (value == null)
            {
                Debug.LogWarning(
                    $"[UGUIBuilderUtility] '{target.GetType().Name}.{propertyName}' へ null を代入しようとした。" +
                    "この参照を用意した呼び出し元(1つ上のスタックフレーム)で生成・取得が失敗している可能性が高い。",
                    target as Object);
            }

            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError(
                    $"[UGUIBuilderUtility] '{target.GetType().Name}.{propertyName}' が見つからない。" +
                    "フィールド名が変わっていないか確認すること。", target as Object);
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>URP の Lit を優先し、無ければ Standard にフォールバックする(PlaceholderPartGenerator と同じ方針)。</summary>
        public static Material GetOrCreateMaterial(string assetPath, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            // UnityEngine.Object のオーバーロード == を迂回する `??` は使わない(疑似 null を
            // 誤って「値あり」と扱う恐れがあるため)。PlaceholderPartGenerator と同じ if 方式に揃える。
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                Debug.LogWarning("[UGUIBuilderUtility] 使えるシェーダが見つからない。既定マテリアルのままにする。");
                return null;
            }

            var material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int lastSlash = path.LastIndexOf('/');
            string parent = path.Substring(0, lastSlash);
            string leaf = path.Substring(lastSlash + 1);

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// ユーザーレイヤーを名前で確保する。既に同名のレイヤーがあればそれを、無ければ最初の空き
        /// ユーザースロット(8-31)に登録して番号を返す。空きが無ければ -1(呼び出し側は既定マスクへ倒すこと)。
        /// TagManager.asset を直接編集する数少ない正攻法(レイヤーは他に設定 API が無い)。
        /// </summary>
        public static int EnsureLayer(string layerName)
        {
            var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets == null || tagManagerAssets.Length == 0)
            {
                Debug.LogWarning("[UGUIBuilderUtility] TagManager.asset を読み込めない。レイヤーは既定のままにする。");
                return -1;
            }

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                Debug.LogWarning("[UGUIBuilderUtility] TagManager.asset の layers を特定できない。");
                return -1;
            }

            for (int i = 0; i < layers.arraySize; i++)
            {
                SerializedProperty element = layers.GetArrayElementAtIndex(i);
                if (element.stringValue == layerName)
                {
                    return i;
                }
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty element = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(element.stringValue))
                {
                    element.stringValue = layerName;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    // 呼び出し側の SaveAssets 頼みにせず、ここで確実にディスクへ落とす
                    // (この後の処理が例外で中断すると、変更が反映されないまま消える)。
                    AssetDatabase.SaveAssets();
                    return i;
                }
            }

            Debug.LogWarning($"[UGUIBuilderUtility] 空いているユーザーレイヤーが無いため '{layerName}' を作成できない。");
            return -1;
        }
    }
}
