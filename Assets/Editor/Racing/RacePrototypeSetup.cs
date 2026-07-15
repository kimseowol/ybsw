// PRD-001
using System.IO;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ybsw.Racing;

namespace Ybsw.Editor.Racing
{
    public static class RacePrototypeSetup
    {
        const string ModelPath = "Assets/Art/GuineaPig/GuineaPigPlayer/Meshy_AI_guinea_pig_tricolor_l_quadruped_model_Animation_Walking_withSkin.fbx";
        const string BaseTexturePath = "Assets/Art/GuineaPig/GuineaPigPlayer/Meshy_AI_guinea_pig_tricolor_l_quadruped_texture_0.png";
        const string GeneratedFolder = "Assets/RacingGenerated";
        const string PlayerMaterialPath = GeneratedFolder + "/GuineaPigPlayer.mat";
        const string PlayerControllerPath = GeneratedFolder + "/GuineaPigWalk.controller";
        const string PlayerPrefabPath = GeneratedFolder + "/GuineaPigPlayer.prefab";
        const string TrackMaterialPath = GeneratedFolder + "/Track.mat";
        const string BarrierMaterialPath = GeneratedFolder + "/Barrier.mat";
        const string FinishMaterialPath = GeneratedFolder + "/Finish.mat";
        const string ScenePath = "Assets/Scenes/GuineaPigRace.unity";

        [MenuItem("Tools/YBSW/Build Guinea Pig Race Prototype")]
        public static void BuildPrototype()
        {
            EnsureFolder(GeneratedFolder);
            ConfigureModelImporter();

            Material playerMaterial = CreateOrReplaceMaterial(
                PlayerMaterialPath,
                Color.white,
                AssetDatabase.LoadAssetAtPath<Texture2D>(BaseTexturePath));
            AnimatorController animatorController = CreateAnimatorController();
            GameObject playerPrefab = CreatePlayerPrefab(playerMaterial, animatorController);
            CreateRaceScene(playerPrefab);

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Guinea pig race prototype generated at " + ScenePath);
        }

        static void ConfigureModelImporter()
        {
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new FileNotFoundException("Guinea pig model was not imported.", ModelPath);
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.SaveAndReimport();

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            for (int index = 0; index < clips.Length; index++)
            {
                clips[index].loopTime = true;
                clips[index].loopPose = true;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        static AnimatorController CreateAnimatorController()
        {
            DeleteAssetIfPresent(PlayerControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(PlayerControllerPath);
            AnimationClip walkClip = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .First(clip => !clip.name.StartsWith("__preview__"));
            AnimatorState state = controller.layers[0].stateMachine.AddState("Walk");
            state.motion = walkClip;
            controller.layers[0].stateMachine.defaultState = state;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static GameObject CreatePlayerPrefab(Material material, RuntimeAnimatorController controller)
        {
            GameObject root = new GameObject("GuineaPigPlayer");
            try
            {
                root.AddComponent<NetworkObject>();
                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                networkTransform.Interpolate = true;

                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 3f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.55f, 0f);
                collider.size = new Vector3(0.95f, 1.1f, 1.45f);
                root.AddComponent<GuineaPigPlayer>();

                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                GameObject visual = PrefabUtility.InstantiatePrefab(model) as GameObject;
                visual.name = "Visual";
                visual.transform.SetParent(root.transform, false);
                NormalizeVisual(visual, 1.45f);

                Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
                for (int index = 0; index < renderers.Length; index++)
                {
                    renderers[index].sharedMaterial = material;
                }

                Animator animator = visual.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    animator = visual.AddComponent<Animator>();
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                DeleteAssetIfPresent(PlayerPrefabPath);
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                PersistNetworkObjectHash();
                return savedPrefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void NormalizeVisual(GameObject visual, float targetLongestSide)
        {
            Bounds bounds = GetRendererBounds(visual);
            float longestSide = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = targetLongestSide / longestSide;
            visual.transform.localScale = Vector3.one * scale;

            bounds = GetRendererBounds(visual);
            visual.transform.localPosition = new Vector3(0f, -bounds.min.y, 0f);
        }

        static void PersistNetworkObjectHash()
        {
            AssetDatabase.ImportAsset(
                PlayerPrefabPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            NetworkObject networkObject = prefab.GetComponent<NetworkObject>();
            SerializedObject serializedObject = new SerializedObject(networkObject);
            SerializedProperty hashProperty = serializedObject.FindProperty("GlobalObjectIdHash");
            if (hashProperty == null || hashProperty.uintValue == 0)
            {
                throw new System.InvalidOperationException("NGO did not generate a prefab hash.");
            }

            uint generatedHash = hashProperty.uintValue;
            hashProperty.uintValue = generatedHash;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(networkObject);
            AssetDatabase.SaveAssetIfDirty(networkObject);
        }

        static Bounds GetRendererBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(target.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        static void CreateRaceScene(GameObject playerPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material trackMaterial = CreateOrReplaceMaterial(TrackMaterialPath, new Color(0.18f, 0.22f, 0.16f), null);
            Material barrierMaterial = CreateOrReplaceMaterial(BarrierMaterialPath, new Color(0.78f, 0.36f, 0.18f), null);
            Material finishMaterial = CreateOrReplaceMaterial(FinishMaterialPath, new Color(1f, 0.92f, 0.18f), null);

            CreateTrack(trackMaterial, barrierMaterial, finishMaterial);
            CreateLightingAndCamera();
            CreateNetworking(playerPrefab);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        static void CreateTrack(Material trackMaterial, Material barrierMaterial, Material finishMaterial)
        {
            CreateCube("Track", new Vector3(0f, -0.25f, 10f), new Vector3(10f, 0.5f, 64f), trackMaterial);
            CreateCube("LeftBarrier", new Vector3(-5.4f, 0.5f, 10f), new Vector3(0.8f, 1f, 64f), barrierMaterial);
            CreateCube("RightBarrier", new Vector3(5.4f, 0.5f, 10f), new Vector3(0.8f, 1f, 64f), barrierMaterial);
            CreateCube("StartLine", new Vector3(0f, 0.015f, -16f), new Vector3(10f, 0.03f, 0.35f), finishMaterial);
            CreateCube("FinishLine", new Vector3(0f, 0.015f, 35f), new Vector3(10f, 0.03f, 0.6f), finishMaterial);

            GameObject finishTrigger = new GameObject("RaceSessionAndFinishTrigger");
            finishTrigger.transform.position = new Vector3(0f, 1f, 35f);
            BoxCollider finishCollider = finishTrigger.AddComponent<BoxCollider>();
            finishCollider.isTrigger = true;
            finishCollider.size = new Vector3(10f, 2f, 1f);
            finishTrigger.AddComponent<NetworkObject>();
            finishTrigger.AddComponent<RaceSession>();
        }

        static void CreateLightingAndCamera()
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light directionalLight = lightObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;
            directionalLight.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 8f, -14f);
            cameraObject.transform.rotation = Quaternion.Euler(24f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.farClipPlane = 180f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<RaceCamera>();
        }

        static void CreateNetworking(GameObject playerPrefab)
        {
            GameObject networkingObject = new GameObject("NetworkManager");
            NetworkManager networkManager = networkingObject.AddComponent<NetworkManager>();
            UnityTransport transport = networkingObject.AddComponent<UnityTransport>();
            networkingObject.AddComponent<RaceLauncher>();
            networkingObject.AddComponent<RaceTouchControls>();

            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.NetworkConfig.EnableSceneManagement = true;
        }

        static GameObject CreateCube(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        static Material CreateOrReplaceMaterial(string path, Color color, Texture texture)
        {
            DeleteAssetIfPresent(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Material material = new Material(shader);
            material.color = color;
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.18f);
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        static void DeleteAssetIfPresent(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
