using Swordman2.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class CombatPlayModeVerifier
{
    private const string VerificationFlag = "Swordman2.CombatVerification";
    private static CombatDirector director;
    private static float stageStart;
    private static int stage;
    private static float expectedHealth;

    static CombatPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        SessionState.SetBool(VerificationFlag, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(VerificationFlag, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            stage = 0;
            stageStart = Time.realtimeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && stage >= 100)
        {
            SessionState.EraseBool(VerificationFlag);
            Debug.Log("VERIFY_PASS: Play Mode 战斗集成验证全部通过");
            EditorApplication.Exit(0);
        }
    }

    private static void Tick()
    {
        try
        {
            if (director == null)
            {
                director = Object.FindAnyObjectByType<CombatDirector>();
                if (director == null)
                {
                    if (Elapsed > 5f) Fail("运行时未创建 CombatDirector");
                    return;
                }
            }

            switch (stage)
            {
                case 0:
                    if (Elapsed < 0.25f) return;
                    Require(Object.FindObjectsByType<Camera>().Length == 2, "没有创建两个分屏摄像机");
                    Require(Object.FindAnyObjectByType<CombatHud>() != null, "没有创建战斗 HUD");
                    Require(director.PlayerOne.CurrentAnimation == "Idle_TwoHand_Sword", "P1 未加载待机动作");
                    Require(director.PlayerTwo.CurrentAnimation == "Idle_TwoHand_Sword", "P2 未加载待机动作");
                    LogRendererDiagnostics(director.PlayerOne);
                    LogRendererDiagnostics(director.PlayerTwo);
                    CapturePreview();
                    SetCloseRange();
                    director.PlayerOne.SubmitAttack(AttackKind.A);
                    director.PlayerTwo.SubmitAttack(AttackKind.A);
                    NextStage();
                    break;

                case 1 when director.PlayerOne.Mode == FighterMode.Rebound || Elapsed >= 8f:
                    Require(director.PlayerOne.Mode == FighterMode.Rebound, "A对A时 P1 未进入弹回");
                    Require(director.PlayerTwo.Mode == FighterMode.Rebound, "A对A时 P2 未进入弹回");
                    Require(Mathf.Approximately(director.PlayerOne.Health, 5f) &&
                            Mathf.Approximately(director.PlayerTwo.Health, 5f), "A对A错误扣血");
                    NextStage();
                    break;

                case 2 when (director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free) || Elapsed >= 8f:
                    Require(director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free,
                        "A对A弹回动画未正常结束");
                    director.PlayerOne.SubmitAttack(AttackKind.B);
                    director.PlayerTwo.SubmitAttack(AttackKind.C);
                    NextStage();
                    break;

                case 3 when director.PlayerTwo.Mode == FighterMode.Rebound || Elapsed >= 8f:
                    Require(director.PlayerOne.Mode == FighterMode.Attack, "B对C时 B方未继续成功动画");
                    Require(director.PlayerTwo.Mode == FighterMode.Rebound, "B对C时 C方未弹回");
                    Require(Mathf.Approximately(director.PlayerTwo.Health,
                            FighterController.MaxHealth - CombatDirector.BCPairDamage),
                        $"B对C时 C方伤害不是{CombatDirector.BCPairDamage}");
                    Require(director.PlayerTwo.EffectTime > 0.29f, "B对C时 C方未获得冻结的0.3秒效果");
                    NextStage();
                    break;

                case 4 when (director.PlayerOne.CurrentAttack != null &&
                             director.PlayerOne.CurrentAttack.ActualDuration -
                             director.PlayerOne.CurrentAttack.Elapsed <= 0.3f) || Elapsed >= 8f:
                    Require(director.PlayerOne.CurrentAttack != null, "等待缓冲测试时 P1 攻击已意外结束");
                    SetFarRange();
                    director.PlayerOne.SubmitAttack(AttackKind.A);
                    director.PlayerOne.SubmitAttack(AttackKind.B);
                    director.PlayerTwo.SubmitAttack(AttackKind.A);
                    NextStage();
                    break;

                case 5 when (director.PlayerOne.CurrentAttack != null && director.PlayerTwo.CurrentAttack != null &&
                             director.PlayerOne.CurrentAttack.Definition.Kind == AttackKind.B &&
                             director.PlayerTwo.CurrentAttack.Definition.Kind == AttackKind.A &&
                             director.PlayerOne.BufferedInputCount == 0 && director.PlayerTwo.BufferedInputCount == 0) || Elapsed >= 8f:
                    Require(director.PlayerOne.CurrentAttack != null &&
                            director.PlayerOne.CurrentAttack.Definition.Kind == AttackKind.B,
                        "最新输入B没有优先于旧输入A执行");
                    Require(Mathf.Approximately(director.PlayerOne.Stance, 0f),
                        "最新输入执行后架势消耗不正确");
                    Require(director.PlayerTwo.CurrentAttack != null &&
                            director.PlayerTwo.CurrentAttack.TimeScale > 1.15f,
                        "效果条没有作用于弹回后立即执行的缓冲攻击");
                    Require(Mathf.Approximately(director.PlayerTwo.EffectTime, 0f),
                        "效果条作用于下一次攻击后没有清零");
                    NextStage();
                    break;

                case 6 when (director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free) || Elapsed >= 8f:
                    Require(director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free,
                        "缓冲攻击未正常结束");
                    SetCloseRange();
                    expectedHealth = director.PlayerTwo.Health;
                    director.PlayerOne.SubmitAttack(AttackKind.A);
                    NextStage();
                    break;

                case 7 when (director.PlayerOne.CurrentAttack != null &&
                             director.PlayerOne.CurrentAttack.Phase == AttackPhase.Active) || Elapsed >= 8f:
                    Require(Mathf.Approximately(director.PlayerTwo.Health, expectedHealth),
                        "普通命中在有效期结束前提前结算");
                    NextStage();
                    break;

                case 8 when (director.PlayerOne.CurrentAttack != null &&
                             director.PlayerOne.CurrentAttack.Phase == AttackPhase.Recovery) || Elapsed >= 8f:
                    Require(Mathf.Approximately(director.PlayerTwo.Health, expectedHealth - 1f),
                        "普通命中未在有效期结束后结算");
                    stage = 100;
                    EditorApplication.update -= Tick;
                    EditorApplication.ExitPlaymode();
                    break;
            }
        }
        catch (System.Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private static float Elapsed => Time.realtimeSinceStartup - stageStart;

    private static void NextStage()
    {
        stage++;
        stageStart = Time.realtimeSinceStartup;
    }

    private static void SetCloseRange()
    {
        director.PlayerOne.Teleport(new Vector3(-0.75f, 0f, 0f));
        director.PlayerTwo.Teleport(new Vector3(0.75f, 0f, 0f));
        director.PlayerOne.UpdateFacing();
        director.PlayerTwo.UpdateFacing();
    }

    private static void SetFarRange()
    {
        director.PlayerOne.Teleport(new Vector3(-4f, 0f, 0f));
        director.PlayerTwo.Teleport(new Vector3(4f, 0f, 0f));
        director.PlayerOne.UpdateFacing();
        director.PlayerTwo.UpdateFacing();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new System.InvalidOperationException(message);
    }

    private static void CapturePreview()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        Camera[] cameras = Object.FindObjectsByType<Camera>();
        System.Array.Sort(cameras, (a, b) => a.rect.x.CompareTo(b.rect.x));
        if (cameras.Length != 2) return;

        const int halfWidth = 800;
        const int height = 900;
        Texture2D composite = new Texture2D(halfWidth * 2, height, TextureFormat.RGB24, false);
        for (int i = 0; i < 2; i++)
        {
            Camera camera = cameras[i];
            Rect originalRect = camera.rect;
            RenderTexture originalTarget = camera.targetTexture;
            RenderTexture renderTexture = RenderTexture.GetTemporary(halfWidth, height, 24,
                RenderTextureFormat.ARGB32);
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.targetTexture = renderTexture;
            camera.Render();

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            Texture2D half = new Texture2D(halfWidth, height, TextureFormat.RGB24, false);
            half.ReadPixels(new Rect(0f, 0f, halfWidth, height), 0, 0);
            half.Apply();
            composite.SetPixels(i * halfWidth, 0, halfWidth, height, half.GetPixels());
            Object.DestroyImmediate(half);
            RenderTexture.active = previousActive;
            camera.targetTexture = originalTarget;
            camera.rect = originalRect;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
        composite.Apply();
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "combat-preview.png"));
        File.WriteAllBytes(path, composite.EncodeToPNG());
        Object.DestroyImmediate(composite);
        Debug.Log("VERIFY_PREVIEW: " + path);
    }

    private static void LogRendererDiagnostics(FighterController fighter)
    {
        Renderer[] renderers = fighter.Root.GetComponentsInChildren<Renderer>(true);
        Bounds combined = new Bounds(fighter.Position, Vector3.zero);
        foreach (Renderer renderer in renderers) combined.Encapsulate(renderer.bounds);
        Debug.Log($"VERIFY_RENDERERS: P{fighter.PlayerIndex} count={renderers.Length} " +
                  $"root={fighter.Position} boundsCenter={combined.center} boundsSize={combined.size}");
    }

    private static void Fail(string message)
    {
        EditorApplication.update -= Tick;
        SessionState.EraseBool(VerificationFlag);
        Debug.LogError("VERIFY_FAIL: " + message);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(2);
    }
}
