using UnityEngine;
using UnityEngine.Rendering;

namespace Swordman2.Combat
{
    public static class CombatBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BuildDemo()
        {
            if (Object.FindAnyObjectByType<CombatDirector>() != null) return;

            if (!CombatCatalogLoader.TryLoad(out CombatCatalogData catalog)) return;

            Application.targetFrameRate = catalog.settings.logicFrameRate;
            QualitySettings.vSyncCount = 0;
            RemoveTemplateCamera();
            BuildArena();

            GameObject model = Resources.Load<GameObject>("Models/Swordsman");
            AnimationClip[] clips = Resources.LoadAll<AnimationClip>("Models/Swordsman");
            if (model == null)
                Debug.LogError("无法加载 Resources/Models/Swordsman.fbx，将显示后备胶囊体。");
            ValidateClips(clips, catalog);

            FighterController playerOne = new FighterController(1, new Vector3(-1.45f, 0f, 0f),
                new Color(0.22f, 0.55f, 1f), model, clips, catalog);
            FighterController playerTwo = new FighterController(2, new Vector3(1.45f, 0f, 0f),
                new Color(1f, 0.32f, 0.22f), model, clips, catalog);

            GameObject directorObject = new GameObject("Combat Director");
            CombatAudio combatAudio = directorObject.AddComponent<CombatAudio>();
            combatAudio.Initialize(catalog.audio);
            CombatDirector director = directorObject.AddComponent<CombatDirector>();
            director.Initialize(playerOne, playerTwo, combatAudio, catalog);

            BuildCamera("Player 1 Camera", playerOne, playerTwo, new Rect(0f, 0f, 0.5f, 1f), true);
            BuildCamera("Player 2 Camera", playerTwo, playerOne, new Rect(0.5f, 0f, 0.5f, 1f), false);

            GameObject hudObject = new GameObject("Combat HUD");
            hudObject.AddComponent<CombatHud>().Initialize(director);
            Debug.Log($"双人战斗演示已从 combat_catalog.json 初始化，共 {catalog.attacks.Length} 个动作。");
        }

        private static void BuildArena()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Arena Ground";
            ground.transform.SetPositionAndRotation(new Vector3(0f, -0.12f, 0f), Quaternion.identity);
            ground.transform.localScale = new Vector3(14f, 0.2f, 10f);
            Renderer renderer = ground.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateMaterial("Arena Ground Material", new Color(0.075f, 0.095f, 0.12f), 0.22f);

            for (int i = -5; i <= 5; i++)
            {
                GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.name = "Arena Grid";
                line.transform.position = new Vector3(i, 0.001f, 0f);
                line.transform.localScale = new Vector3(0.018f, 0.01f, 10f);
                Object.Destroy(line.GetComponent<Collider>());
                line.GetComponent<Renderer>().sharedMaterial = CreateMaterial("Grid Material",
                    new Color(0.12f, 0.16f, 0.2f), 0f);
            }

            Light existing = Object.FindAnyObjectByType<Light>();
            if (existing == null)
            {
                GameObject lightObject = new GameObject("Directional Light");
                existing = lightObject.AddComponent<Light>();
                existing.type = LightType.Directional;
                existing.intensity = 1.6f;
                lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            }
        }

        private static Material CreateMaterial(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private static void BuildCamera(string name, FighterController target, FighterController opponent,
            Rect rect, bool audioListener)
        {
            GameObject cameraObject = new GameObject(name);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.rect = rect;
            camera.fieldOfView = 63f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 80f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            if (audioListener) cameraObject.AddComponent<AudioListener>();

            SplitCameraFollow follow = cameraObject.AddComponent<SplitCameraFollow>();
            follow.Target = target.Root.transform;
            follow.Opponent = opponent.Root.transform;
            Vector3 right = Vector3.Cross(Vector3.up, target.Facing).normalized;
            cameraObject.transform.position = target.Position - target.Facing * follow.Distance +
                                              right * follow.ShoulderOffset + Vector3.up * follow.Height;
        }

        private static void RemoveTemplateCamera()
        {
            foreach (Camera camera in Object.FindObjectsByType<Camera>())
                Object.Destroy(camera.gameObject);
        }

        private static void ValidateClips(AnimationClip[] clips, CombatCatalogData catalog)
        {
            System.Collections.Generic.HashSet<string> required = new(System.StringComparer.OrdinalIgnoreCase)
            {
                catalog.commonAnimations.idle, catalog.commonAnimations.walkForward,
                catalog.commonAnimations.walkBackward, catalog.commonAnimations.walkLeft,
                catalog.commonAnimations.walkRight, catalog.commonAnimations.hitReaction
            };
            foreach (AttackDefinition attack in catalog.attacks)
            {
                required.Add(attack.animation.successRightToLeft);
                required.Add(attack.animation.successLeftToRight);
                required.Add(attack.animation.reboundRightToLeft);
                required.Add(attack.animation.reboundLeftToRight);
            }

            foreach (string requiredName in required)
            {
                bool found = false;
                foreach (AnimationClip clip in clips)
                {
                    if (clip.name.EndsWith(requiredName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) Debug.LogWarning($"FBX 缺少动画：{requiredName}。对应逻辑仍会正常执行。");
            }
        }
    }
}
