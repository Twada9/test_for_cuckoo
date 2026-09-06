using System.Collections.Generic;
using ModularMech.Assembling;
using ModularMech.Data;
using ModularMech.Mechs;
using UnityEditor;
using UnityEngine;

namespace ModularMech.EditorTools
{
    /// <summary>
    /// プリミティブだけでプレースホルダ一式(パーツプレハブ / PartDefinition / LocomotionProfile /
    /// PartCatalog / 機体リグ)を生成するエディタ拡張(マイルストーン M1)。
    ///
    /// 数値は「実際に過積載とパワー不足を再現できる」ことを条件に決めてある。
    /// 以下は実データから計算した検証用の構成例で、コメントと数値が食い違わないよう
    /// パーツ値を変えたら必ずここも直すこと。
    ///
    /// <list type="bullet">
    /// <item><b>積載率 1.5 超(Run と Jump の両方が剥奪される)</b>:
    ///   装甲ヘッド(14) + 重装甲胴体(95) + 重装腕 L/R(38+38) + 軽量二脚(20) = 総重量 205。
    ///   軽量二脚の積載上限 120 に対して比 1.71 → §3.3 で Run 剥奪、1.5 超なので Jump も剥奪、
    ///   速度倍率は下限の 0.4。この構成の消費は 48 / 出力 75 なのでパワー不足は起きない。</item>
    /// <item><b>積載率 1.0〜1.5(Run だけ剥奪。Jump は残る)</b>:
    ///   センサーヘッド(6) + 重装甲胴体(95) + 標準腕 L/R(18+18) + 軽量二脚(20) = 総重量 157。
    ///   比 1.31 → Run のみ剥奪、速度倍率およそ 0.63。</item>
    /// <item><b>パワー比 1.0 未満</b>:
    ///   センサーヘッド(4) + 軽量胴体(5) + 標準腕 L/R(6+6) + ホバー脚(30) + ブースター(24)
    ///   = 消費 75。出力は軽量胴体の 60 だけなので比 0.8 → 加速度と旋回速度が 0.8 倍。
    ///   同じ構成でも標準胴体(出力 95)に替えると比 1.22 になりペナルティは消える。
    ///   総重量 118 / ホバー脚の上限 140 なので過積載は起きず、パワー不足だけを単独で確認できる。</item>
    /// </list>
    ///
    /// LocomotionProfile の数値は「移動方式ごとの性格」の唯一の出典でもある(CLAUDE.md D-14)。
    /// 戦略側に速度・旋回・ジャンプの倍率を置かない代わりに、ホバーは速め・旋回鈍め、
    /// 装軌は直進速め・旋回かなり遅め、四脚は少し遅く跳べない、をここの数値だけで作る。
    ///
    /// 既存アセットは**上書きしない**。既にあるものは警告して残す。
    /// 作り直したいときは該当アセットを削除してから再実行する。
    /// </summary>
    public static class PlaceholderPartGenerator
    {
        const string PartPrefabFolder = "Assets/Prefabs/Parts";
        const string MechPrefabFolder = "Assets/Prefabs/Mech";
        const string PartAssetFolder = "Assets/ScriptableObjects/Parts";
        const string LocomotionAssetFolder = "Assets/ScriptableObjects/Locomotion";
        const string MaterialFolder = "Assets/Materials/Placeholder";
        const string CatalogAssetPath = "Assets/ScriptableObjects/PartCatalog.asset";
        const string MechPrefabPath = MechPrefabFolder + "/Mech_Placeholder.prefab";

        /// <summary>PartCatalog がパーツ一覧を保持しているフィールド名。見つからなければ型で探しに行く。</summary>
        const string CatalogPartsField = "parts";

        // --- 生成対象の定義 -------------------------------------------------------------

        sealed class ProfileSpec
        {
            public string assetName;
            public LocomotionType type;
            public float moveSpeed;
            public float turnSpeed;
            public float jumpPower;
            public float acceleration;
            public float groundOffset;
            public float weightCapacity;
        }

        sealed class PartSpec
        {
            public string partId;
            public string displayName;
            public string description;
            public PartSlot slot;
            public PrimitiveType primitive;
            public Vector3 meshScale;
            public Vector3 meshOffset;
            public string colorName;
            public Color color;

            /// <summary>空ならスロット既定ソケットに付く(MechAssembly.GetDefaultSocketName)。</summary>
            public string boneName;

            public Vector3 attachOffset;
            public PartStats stats;
            public CapabilityFlags capabilities;

            /// <summary>Legs のみ。ProfileSpec.assetName と対応。</summary>
            public string locomotionProfile;
        }

        // 移動方式ごとの「性格」はここの数値だけで作る(CLAUDE.md D-14)。
        // 戦略側には速度・旋回・ジャンプの倍率を置かないので、以下がそのまま実挙動になり、
        // かつ StatPanelView が表示する実効速度と一致する。
        //   二脚(軽)  : 基準。速く、よく曲がり、よく跳ぶが積載上限が低い
        //   二脚(重)  : 遅く鈍いが積載上限が大きい
        //   四脚      : 二脚(軽)より少し遅く旋回も鈍い。跳躍は不得手(加減速の良さは戦略側の係数)
        //   ホバー    : 最速だが旋回は二脚(軽)の半分以下。ジャンプ不可
        //   装軌      : 直進はホバーに次いで速く、旋回は全方式で最も遅い。ジャンプ不可
        static readonly ProfileSpec[] Profiles =
        {
            new ProfileSpec { assetName = "Locomotion_Biped_Light", type = LocomotionType.Biped,
                              moveSpeed = 6f, turnSpeed = 200f, jumpPower = 6.5f, acceleration = 18f,
                              groundOffset = 0f, weightCapacity = 120f },
            new ProfileSpec { assetName = "Locomotion_Biped_Heavy", type = LocomotionType.Biped,
                              moveSpeed = 4f, turnSpeed = 120f, jumpPower = 4f, acceleration = 10f,
                              groundOffset = 0f, weightCapacity = 260f },
            new ProfileSpec { assetName = "Locomotion_Quadruped", type = LocomotionType.Quadruped,
                              moveSpeed = 5.2f, turnSpeed = 128f, jumpPower = 2.8f, acceleration = 15f,
                              groundOffset = 0f, weightCapacity = 240f },
            new ProfileSpec { assetName = "Locomotion_Hover", type = LocomotionType.Hover,
                              moveSpeed = 9.2f, turnSpeed = 84f, jumpPower = 0f, acceleration = 7f,
                              groundOffset = 0.9f, weightCapacity = 140f },
            new ProfileSpec { assetName = "Locomotion_Tracked", type = LocomotionType.Tracked,
                              moveSpeed = 8.8f, turnSpeed = 65f, jumpPower = 0f, acceleration = 9f,
                              groundOffset = 0f, weightCapacity = 320f },
        };

        static readonly Color TorsoColor = new Color(0.45f, 0.55f, 0.70f);
        static readonly Color HeadColor = new Color(0.85f, 0.80f, 0.45f);
        static readonly Color ArmColor = new Color(0.55f, 0.60f, 0.62f);
        static readonly Color LegColor = new Color(0.40f, 0.45f, 0.50f);
        static readonly Color PackColor = new Color(0.60f, 0.45f, 0.55f);
        static readonly Color HandColor = new Color(0.75f, 0.55f, 0.40f);

        // --- メニュー -------------------------------------------------------------------

        [MenuItem("Tools/ModularMech/Generate Placeholder Assets")]
        public static void GenerateAll()
        {
            EnsureFolders();

            var createdProfiles = new Dictionary<string, LocomotionProfile>(Profiles.Length);
            int created = 0;
            int skipped = 0;

            for (int i = 0; i < Profiles.Length; i++)
            {
                ProfileSpec spec = Profiles[i];
                string path = $"{LocomotionAssetFolder}/{spec.assetName}.asset";

                var existing = AssetDatabase.LoadAssetAtPath<LocomotionProfile>(path);
                if (existing != null)
                {
                    Debug.LogWarning($"[PlaceholderPartGenerator] 既存のため上書きしない: {path}");
                    createdProfiles[spec.assetName] = existing;
                    skipped++;
                    continue;
                }

                var profile = ScriptableObject.CreateInstance<LocomotionProfile>();
                profile.type = spec.type;
                profile.baseMoveSpeed = spec.moveSpeed;
                profile.baseTurnSpeed = spec.turnSpeed;
                profile.baseJumpPower = spec.jumpPower;
                profile.acceleration = spec.acceleration;
                profile.groundOffset = spec.groundOffset;
                profile.weightCapacity = spec.weightCapacity;

                AssetDatabase.CreateAsset(profile, path);
                createdProfiles[spec.assetName] = profile;
                created++;
            }

            PartSpec[] partSpecs = BuildPartSpecs();
            var definitions = new List<PartDefinition>(partSpecs.Length);

            for (int i = 0; i < partSpecs.Length; i++)
            {
                PartSpec spec = partSpecs[i];

                GameObject prefab = CreatePartPrefab(spec, ref created, ref skipped);
                PartDefinition definition = CreatePartDefinition(spec, prefab, createdProfiles, ref created, ref skipped);

                if (definition != null)
                {
                    definitions.Add(definition);
                }
            }

            CreateCatalog(definitions, ref created, ref skipped);
            CreateMechPrefab(ref created, ref skipped);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PlaceholderPartGenerator] 完了。新規作成 {created} 件 / 既存のため据え置き {skipped} 件。");
        }

        // --- パーツ定義 -----------------------------------------------------------------

        static PartSpec[] BuildPartSpecs()
        {
            return new[]
            {
                // ---- Torso: 出力を供給する。重装甲は出力が伸びないので過積載とパワー不足の起点になる ----
                new PartSpec
                {
                    partId = "torso_light_01", displayName = "軽量胴体", slot = PartSlot.Torso,
                    description = "出力控えめ。積載に余裕を作りたいとき用。",
                    primitive = PrimitiveType.Cube, meshScale = new Vector3(0.70f, 0.90f, 0.50f),
                    colorName = "Torso", color = TorsoColor, attachOffset = new Vector3(0f, 0.10f, 0f),
                    stats = new PartStats { weight = 30f, powerOutput = 60f, powerDraw = 5f, stability = 0.2f },
                    capabilities = CapabilityFlags.Walk | CapabilityFlags.Crouch | CapabilityFlags.Emote,
                },
                new PartSpec
                {
                    partId = "torso_standard_01", displayName = "標準胴体", slot = PartSlot.Torso,
                    description = "出力と重量のバランス型。既定機体はこれを使う。",
                    primitive = PrimitiveType.Cube, meshScale = new Vector3(0.85f, 1.00f, 0.60f),
                    colorName = "Torso", color = TorsoColor, attachOffset = new Vector3(0f, 0.10f, 0f),
                    stats = new PartStats { weight = 45f, powerOutput = 95f, powerDraw = 8f, stability = 0.4f },
                    capabilities = CapabilityFlags.Walk | CapabilityFlags.Run | CapabilityFlags.Crouch | CapabilityFlags.Emote,
                },
                new PartSpec
                {
                    partId = "torso_heavy_01", displayName = "重装甲胴体", slot = PartSlot.Torso,
                    description = "重い割に出力が伸びない。重装腕と軽量二脚に組み合わせると積載率 1.7 の過積載になる。",
                    primitive = PrimitiveType.Cube, meshScale = new Vector3(1.10f, 1.10f, 0.80f),
                    colorName = "Torso", color = TorsoColor, attachOffset = new Vector3(0f, 0.10f, 0f),
                    // 重量 95: 装甲ヘッド + 重装腕 ×2 + 軽量二脚 と組んで 205 になり、
                    // 積載上限 120 に対して 1.5 を確実に超える(= Jump も剥奪される)ようにしてある。
                    stats = new PartStats { weight = 95f, powerOutput = 75f, powerDraw = 14f, moveSpeedMod = -0.5f, stability = 0.9f },
                    capabilities = CapabilityFlags.Walk | CapabilityFlags.Crouch | CapabilityFlags.Emote,
                },

                // ---- Head ----
                new PartSpec
                {
                    partId = "head_sensor_01", displayName = "センサーヘッド", slot = PartSlot.Head,
                    description = "軽量。旋回に補正。",
                    primitive = PrimitiveType.Sphere, meshScale = new Vector3(0.40f, 0.40f, 0.40f),
                    colorName = "Head", color = HeadColor, attachOffset = new Vector3(0f, 0.15f, 0f),
                    stats = new PartStats { weight = 6f, powerDraw = 4f, turnSpeedMod = 15f },
                    capabilities = CapabilityFlags.Emote,
                },
                new PartSpec
                {
                    partId = "head_armored_01", displayName = "装甲ヘッド", slot = PartSlot.Head,
                    description = "重いが消費電力が小さい。",
                    primitive = PrimitiveType.Cube, meshScale = new Vector3(0.45f, 0.40f, 0.45f),
                    colorName = "Head", color = HeadColor, attachOffset = new Vector3(0f, 0.15f, 0f),
                    stats = new PartStats { weight = 14f, powerDraw = 2f, stability = 0.2f },
                    capabilities = CapabilityFlags.None,
                },

                // ---- Arms: 左右で別アセット。PartDefinition.slot が単一スロットのため共用はできない ----
                ArmSpec("arm_standard_l_01", "標準腕 (左)", PartSlot.ArmLeft, CapabilityFlags.GrabLeft, false),
                ArmSpec("arm_standard_r_01", "標準腕 (右)", PartSlot.ArmRight, CapabilityFlags.GrabRight, false),
                ArmSpec("arm_heavy_l_01", "重装腕 (左)", PartSlot.ArmLeft, CapabilityFlags.GrabLeft, true),
                ArmSpec("arm_heavy_r_01", "重装腕 (右)", PartSlot.ArmRight, CapabilityFlags.GrabRight, true),

                // ---- Legs: 移動方式と積載上限を決める。ここを替えると挙動そのものが変わる ----
                new PartSpec
                {
                    partId = "legs_biped_light_01", displayName = "軽量二脚", slot = PartSlot.Legs,
                    description = "速いが積載上限が低い(120)。重装備を載せるとすぐ過積載になる。",
                    primitive = PrimitiveType.Capsule, meshScale = new Vector3(0.50f, 0.55f, 0.50f),
                    meshOffset = new Vector3(0f, -0.55f, 0f),
                    colorName = "Leg", color = LegColor, attachOffset = new Vector3(0f, -0.10f, 0f),
                    stats = new PartStats { weight = 20f, powerDraw = 8f, moveSpeedMod = 0.5f, jumpPowerMod = 0.5f },
                    capabilities = CapabilityFlags.Walk | CapabilityFlags.Run | CapabilityFlags.Jump | CapabilityFlags.Crouch,
                    locomotionProfile = "Locomotion_Biped_Light",
                },
                new PartSpec
                {
                    partId = "legs_biped_heavy_01", displayName = "重量二脚", slot = PartSlot.Legs,
                    description = "遅いが積載上限 260。重装甲を担げる。",
                    primitive = PrimitiveType.Capsule, meshScale = new Vector3(0.70f, 0.55f, 0.70f),
                    meshOffset = new Vector3(0f, -0.55f, 0f),
                    colorName = "Leg", color = LegColor, attachOffset = new Vector3(0f, -0.10f, 0f),
                    stats = new PartStats { weight = 45f, powerDraw = 14f, moveSpeedMod = -0.5f, stability = 0.6f },
                    capabilities = CapabilityFlags.Walk | CapabilityFlags.Run | CapabilityFlags.Jump,
                    locomotionProfile = "Locomotion_Biped_Heavy",
                },
                new PartSpec
                {
                    partId = "legs_quad_01", displayName = "四脚", slot = PartSlot.Legs,
                    description = "二脚より安定寄り。加減速が素直で積載上限 240。",
                    primitive = PrimitiveType.Cube, meshScale = new Vector3(0.90f, 0.45f, 1.00f),
                    meshOffset = new Vector3(0f, -0.55f, 0f),
                    colorName = "Leg", color = LegColor, attachOffset = new Vector3(0f, -0.10f, 0f),
                    stats = new PartStats { weight = 50f, powerDraw = 15f, stability = 0.8f },
                    capabilities = CapabilityFlags.Walk | CapabilityFlags.Run | CapabilityFlags.Jump,
                    locomotionProfile = "Locomotion_Quadruped",
                },
                new PartSpec
                {
                    partId = "legs_hover_01", displayName = "ホバー脚", slot = PartSlot.Legs,
                    description = "常時 0.9m 浮いて滑るように動く。速いがジャンプ不可。消費電力 30 と大きい。",
                    primitive = PrimitiveType.Capsule, meshScale = new Vector3(0.80f, 0.25f, 0.80f),
                    meshOffset = new Vector3(0f, -0.60f, 0f),
                    colorName = "Leg", color = LegColor, attachOffset = new Vector3(0f, -0.10f, 0f),
                    // 消費 30: 軽量胴体(出力 60)+ ブースター(24)+ ヘッド/腕 と組むと消費 75 になり、
                    // パワー比 0.8 のパワー不足を単独で再現できる(過積載は起きない重さに留めてある)。
                    stats = new PartStats { weight = 30f, powerDraw = 30f, moveSpeedMod = 1f },
                    capabilities = CapabilityFlags.Walk | CapabilityFlags.Hover | CapabilityFlags.Dash,
                    locomotionProfile = "Locomotion_Hover",
                },
                new PartSpec
                {
                    partId = "legs_tracked_01", displayName = "装軌脚", slot = PartSlot.Legs,
                    description = "直進が速く旋回が遅い。その場旋回ができる。積載上限 320。",
                    primitive = PrimitiveType.Cube, meshScale = new Vector3(1.00f, 0.40f, 1.40f),
                    meshOffset = new Vector3(0f, -0.70f, 0f),
                    colorName = "Leg", color = LegColor, attachOffset = new Vector3(0f, -0.10f, 0f),
                    stats = new PartStats { weight = 60f, powerDraw = 16f, turnSpeedMod = -20f, stability = 1f },
                    capabilities = CapabilityFlags.Walk | CapabilityFlags.Run,
                    locomotionProfile = "Locomotion_Tracked",
                },

                // ---- Backpack ----
                new PartSpec
                {
                    partId = "backpack_battery_01", displayName = "増設バッテリー", slot = PartSlot.Backpack,
                    description = "出力 +40。パワー不足の解消用。ただし重い。",
                    primitive = PrimitiveType.Cube, meshScale = new Vector3(0.55f, 0.55f, 0.30f),
                    colorName = "Pack", color = PackColor, attachOffset = new Vector3(0f, 0f, -0.05f),
                    stats = new PartStats { weight = 22f, powerOutput = 40f, powerDraw = 2f },
                    capabilities = CapabilityFlags.None,
                },
                new PartSpec
                {
                    partId = "backpack_booster_01", displayName = "ブースター", slot = PartSlot.Backpack,
                    description = "Dash / Hover を付与するが消費 24。軽量胴体 + ホバー脚と組むとパワー比 0.8 になる。",
                    primitive = PrimitiveType.Cube, meshScale = new Vector3(0.50f, 0.45f, 0.40f),
                    colorName = "Pack", color = PackColor, attachOffset = new Vector3(0f, 0f, -0.05f),
                    stats = new PartStats { weight = 16f, powerDraw = 24f, jumpPowerMod = 1.5f },
                    capabilities = CapabilityFlags.Dash | CapabilityFlags.Hover,
                },

                // ---- Hands: 腕が無いと装備できない(LoadoutValidator の HandRequiresArm) ----
                HandSpec("hand_gripper_l_01", "グリッパー (左)", PartSlot.HandLeft, 5f, 3f, 0f),
                HandSpec("hand_gripper_r_01", "グリッパー (右)", PartSlot.HandRight, 5f, 3f, 0f),
                HandSpec("hand_shield_l_01", "シールド (左)", PartSlot.HandLeft, 12f, 1f, 0.2f),
                HandSpec("hand_shield_r_01", "シールド (右)", PartSlot.HandRight, 12f, 1f, 0.2f),
            };
        }

        static PartSpec ArmSpec(string partId, string displayName, PartSlot slot, CapabilityFlags grab, bool heavy)
        {
            return new PartSpec
            {
                partId = partId,
                displayName = displayName,
                description = heavy ? "重い腕。重装甲胴体と組んで2本積むと軽量二脚では積載率 1.5 超になる。" : "標準的な腕。",
                slot = slot,
                primitive = PrimitiveType.Capsule,
                meshScale = heavy ? new Vector3(0.30f, 0.45f, 0.30f) : new Vector3(0.22f, 0.45f, 0.22f),
                meshOffset = new Vector3(0f, -0.40f, 0f),
                colorName = "Arm",
                color = ArmColor,
                attachOffset = new Vector3(0f, -0.05f, 0f),
                // 重装腕は片腕 38。重装甲胴体(95) + 装甲ヘッド(14) + 軽量二脚(20) と合わせて 205 になり、
                // 積載上限 120 に対して比 1.71 ―― §3.3 の「1.5 超で Jump も剥奪」を実際に踏める。
                stats = heavy
                    ? new PartStats { weight = 38f, powerDraw = 12f, moveSpeedMod = -0.2f, stability = 0.3f }
                    : new PartStats { weight = 18f, powerDraw = 6f },
                // GrabLeft / GrabRight は v1 では読まれない予約フラグ(CLAUDE.md D-12)。
                // 付与自体は仕様どおり行い、UI の能力表示にだけ現れる。
                capabilities = grab,
            };
        }

        static PartSpec HandSpec(string partId, string displayName, PartSlot slot, float weight, float draw, float stability)
        {
            return new PartSpec
            {
                partId = partId,
                displayName = displayName,
                description = "手持ち装備。対応する腕が無いと装備できない。",
                slot = slot,
                primitive = PrimitiveType.Cube,
                meshScale = new Vector3(0.22f, 0.22f, 0.22f),
                colorName = "Hand",
                color = HandColor,
                attachOffset = new Vector3(0f, -0.10f, 0f),
                stats = new PartStats { weight = weight, powerDraw = draw, stability = stability },
                capabilities = CapabilityFlags.None,
            };
        }

        // --- アセット生成 ---------------------------------------------------------------

        static GameObject CreatePartPrefab(PartSpec spec, ref int created, ref int skipped)
        {
            string path = $"{PartPrefabFolder}/Part_{spec.partId}.prefab";

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                Debug.LogWarning($"[PlaceholderPartGenerator] 既存のため上書きしない: {path}");
                skipped++;
                return existing;
            }

            var root = new GameObject($"Part_{spec.partId}");
            GameObject mesh = null;

            try
            {
                mesh = GameObject.CreatePrimitive(spec.primitive);
                mesh.name = "Mesh";
                mesh.transform.SetParent(root.transform, false);
                mesh.transform.localPosition = spec.meshOffset;
                mesh.transform.localScale = spec.meshScale.sqrMagnitude < 0.0001f ? Vector3.one : spec.meshScale;

                // 機体は CharacterController で動くので、パーツ側のコライダは干渉するだけ。
                var collider = mesh.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }

                Material material = GetOrCreateMaterial(spec.colorName, spec.color);
                if (material != null)
                {
                    var renderer = mesh.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = material;
                    }
                }

                // アタッチ情報の唯一の出典はプレハブ側(CLAUDE.md D-1)。
                // ボーン名を空にしてあるパーツは MechAssembly のスロット既定ソケットに付く。
                var attachment = root.AddComponent<PartAttachment>();
                attachment.Configure(spec.boneName ?? string.Empty, spec.attachOffset, Vector3.zero, Vector3.one);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                created++;
                return prefab;
            }
            finally
            {
                if (mesh != null)
                {
                    Object.DestroyImmediate(mesh);
                }

                Object.DestroyImmediate(root);
            }
        }

        static PartDefinition CreatePartDefinition(
            PartSpec spec,
            GameObject prefab,
            Dictionary<string, LocomotionProfile> profiles,
            ref int created,
            ref int skipped)
        {
            string path = $"{PartAssetFolder}/Part_{spec.partId}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<PartDefinition>(path);
            if (existing != null)
            {
                Debug.LogWarning($"[PlaceholderPartGenerator] 既存のため上書きしない: {path}");
                skipped++;
                return existing;
            }

            var definition = ScriptableObject.CreateInstance<PartDefinition>();
            definition.partId = spec.partId;
            definition.displayName = spec.displayName;
            definition.description = spec.description;
            definition.slot = spec.slot;
            definition.meshPrefab = prefab;

            // プレースホルダは全て剛体付け。Skinned は購入アセット導入時に切り替える。
            definition.attachmentMode = AttachmentMode.RigidToBone;
            definition.stats = spec.stats;
            definition.grantedCapabilities = spec.capabilities;

            if (!string.IsNullOrEmpty(spec.locomotionProfile))
            {
                if (profiles.TryGetValue(spec.locomotionProfile, out LocomotionProfile profile))
                {
                    definition.locomotionProfile = profile;
                }
                else
                {
                    Debug.LogWarning($"[PlaceholderPartGenerator] LocomotionProfile '{spec.locomotionProfile}' が見つからない ({spec.partId})。");
                }
            }

            AssetDatabase.CreateAsset(definition, path);
            created++;
            return definition;
        }

        static void CreateCatalog(List<PartDefinition> definitions, ref int created, ref int skipped)
        {
            var existing = AssetDatabase.LoadAssetAtPath<PartCatalog>(CatalogAssetPath);
            if (existing != null)
            {
                Debug.LogWarning(
                    $"[PlaceholderPartGenerator] 既存のため上書きしない: {CatalogAssetPath}。" +
                    "新しいパーツを載せるには、カタログの parts リストへ手動で追加するか、既存アセットを削除して再実行する。");
                skipped++;
                return;
            }

            var catalog = ScriptableObject.CreateInstance<PartCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);

            var serialized = new SerializedObject(catalog);
            SerializedProperty listProperty = serialized.FindProperty(CatalogPartsField) ?? FindPartListProperty(serialized);

            if (listProperty == null || !listProperty.isArray)
            {
                Debug.LogError(
                    "[PlaceholderPartGenerator] PartCatalog のパーツ一覧フィールドを特定できなかった。" +
                    "生成したカタログは空のままなので、Inspector で手動登録すること。");
                created++;
                return;
            }

            listProperty.arraySize = definitions.Count;
            for (int i = 0; i < definitions.Count; i++)
            {
                listProperty.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);

            // 索引はキャッシュされるので、内容を書き換えたら明示的に無効化する。
            catalog.Invalidate();

            created++;
        }

        /// <summary>フィールド名が変わっていても拾えるよう、PartDefinition 配列を型で探す保険。</summary>
        static SerializedProperty FindPartListProperty(SerializedObject serialized)
        {
            SerializedProperty iterator = serialized.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.isArray && iterator.propertyType == SerializedPropertyType.Generic &&
                    iterator.arrayElementType.Contains(nameof(PartDefinition)))
                {
                    return iterator.Copy();
                }
            }

            return null;
        }

        // --- 機体リグ -------------------------------------------------------------------

        /// <summary>
        /// パーツを取り付けるためのプレースホルダ機体。
        /// ボーン名は <see cref="MechAssembly.GetDefaultSocketName"/> の既定ソケット名と一致させてある。
        /// ここが食い違うと剛体パーツが1つも付かないので、名前は必ずこの表と揃えること。
        /// </summary>
        static void CreateMechPrefab(ref int created, ref int skipped)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(MechPrefabPath);
            if (existing != null)
            {
                Debug.LogWarning($"[PlaceholderPartGenerator] 既存のため上書きしない: {MechPrefabPath}");
                skipped++;
                return;
            }

            var root = new GameObject("Mech_Placeholder");

            try
            {
                Transform skeletonRoot = CreateBone("Root", root.transform, Vector3.zero);
                Transform hips = CreateBone("Hips", skeletonRoot, new Vector3(0f, 1.00f, 0f));
                Transform spine = CreateBone("Spine", hips, new Vector3(0f, 0.30f, 0f));
                Transform chest = CreateBone("Chest", spine, new Vector3(0f, 0.35f, 0f));
                Transform neck = CreateBone("Neck", chest, new Vector3(0f, 0.30f, 0f));
                CreateBone("Head", neck, new Vector3(0f, 0.15f, 0f));
                CreateBone("Backpack", chest, new Vector3(0f, 0.05f, -0.35f));

                CreateArm(chest, "_L", -1f);
                CreateArm(chest, "_R", 1f);
                CreateLeg(hips, "_L", -1f);
                CreateLeg(hips, "_R", 1f);

                var characterController = root.AddComponent<CharacterController>();
                characterController.center = new Vector3(0f, 1.0f, 0f);
                characterController.height = 2.0f;
                characterController.radius = 0.5f;

                var animator = root.AddComponent<Animator>();
                animator.applyRootMotion = false; // 移動は完全にスクリプト駆動(CLAUDE.md D-7)

                var assembly = root.AddComponent<MechAssembly>();
                var animationDriver = root.AddComponent<MechAnimationDriver>();
                var runtime = root.AddComponent<MechRuntime>();
                var locomotion = root.AddComponent<MechLocomotionController>();

                var catalog = AssetDatabase.LoadAssetAtPath<PartCatalog>(CatalogAssetPath);

                SetReference(assembly, "skeletonRoot", skeletonRoot);

                SetReference(animationDriver, "animator", animator);

                SetReference(runtime, "assembly", assembly);
                SetReference(runtime, "animationDriver", animationDriver);
                SetReference(runtime, "catalog", catalog);
                SetDefaultPartIds(runtime);

                // MechLocomotionController は Animator を直接持たない(書き込みは MechAnimationDriver に一本化)。
                SetReference(locomotion, "runtime", runtime);
                SetReference(locomotion, "characterController", characterController);
                SetReference(locomotion, "animationDriver", animationDriver);

                PrefabUtility.SaveAsPrefabAsset(root, MechPrefabPath);
                created++;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void CreateArm(Transform chest, string suffix, float side)
        {
            Transform shoulder = CreateBone($"Shoulder{suffix}", chest, new Vector3(0.35f * side, 0.20f, 0f));
            Transform upperArm = CreateBone($"UpperArm{suffix}", shoulder, new Vector3(0.15f * side, 0f, 0f));
            Transform lowerArm = CreateBone($"LowerArm{suffix}", upperArm, new Vector3(0f, -0.45f, 0f));
            CreateBone($"Hand{suffix}", lowerArm, new Vector3(0f, -0.40f, 0f));
        }

        static void CreateLeg(Transform hips, string suffix, float side)
        {
            Transform upperLeg = CreateBone($"UpperLeg{suffix}", hips, new Vector3(0.20f * side, -0.10f, 0f));
            Transform lowerLeg = CreateBone($"LowerLeg{suffix}", upperLeg, new Vector3(0f, -0.45f, 0f));
            CreateBone($"Foot{suffix}", lowerLeg, new Vector3(0f, -0.45f, 0f));
        }

        static Transform CreateBone(string name, Transform parent, Vector3 localPosition)
        {
            var bone = new GameObject(name);
            bone.transform.SetParent(parent, false);
            bone.transform.localPosition = localPosition;
            return bone.transform;
        }

        static void SetDefaultPartIds(MechRuntime runtime)
        {
            // PartSlots.All の宣言順(Head, Torso, ArmLeft, ArmRight, Legs, Backpack, HandLeft, HandRight)。
            // 総重量 107 / 上限 120、消費 32 / 出力 95 で、ペナルティのかからない基準構成。
            string[] ids =
            {
                "head_sensor_01",
                "torso_standard_01",
                "arm_standard_l_01",
                "arm_standard_r_01",
                "legs_biped_light_01",
                string.Empty,
                string.Empty,
                string.Empty,
            };

            var serialized = new SerializedObject(runtime);
            SerializedProperty property = serialized.FindProperty("defaultPartIds");
            if (property == null || !property.isArray)
            {
                return;
            }

            property.arraySize = ids.Length;
            for (int i = 0; i < ids.Length; i++)
            {
                property.GetArrayElementAtIndex(i).stringValue = ids[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetReference(Object target, string propertyName, Object value)
        {
            if (target == null)
            {
                return;
            }

            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[PlaceholderPartGenerator] '{target.GetType().Name}.{propertyName}' が見つからない。手動で設定すること。");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // --- ユーティリティ -------------------------------------------------------------

        static Material GetOrCreateMaterial(string colorName, Color color)
        {
            string path = $"{MaterialFolder}/Placeholder_{colorName}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                Debug.LogWarning("[PlaceholderPartGenerator] 使えるシェーダが見つからない。プレースホルダは既定マテリアルのままにする。");
                return null;
            }

            var material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PartPrefabFolder);
            EnsureFolder(MechPrefabFolder);
            EnsureFolder("Assets/ScriptableObjects");
            EnsureFolder(PartAssetFolder);
            EnsureFolder(LocomotionAssetFolder);
            EnsureFolder("Assets/Materials");
            EnsureFolder(MaterialFolder);
        }

        static void EnsureFolder(string path)
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
    }
}
