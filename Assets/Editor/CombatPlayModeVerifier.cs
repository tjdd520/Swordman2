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

    [MenuItem("Swordman2/Run Combat Verification")]
    public static void Run()
    {
        director = null;
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
            if (Application.isBatchMode) EditorApplication.Exit(0);
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
                    VerifySimulationProtocol();
                    CapturePreview();
                    SetCloseRange();
                    director.PlayerOne.SubmitAttack("A");
                    director.PlayerTwo.SubmitAttack("A");
                    NextStage();
                    break;

                case 1 when director.PlayerOne.Mode == FighterMode.Rebound || Elapsed >= 8f:
                    Require(director.PlayerOne.Mode == FighterMode.Rebound, "A对A时 P1 未进入弹回");
                    Require(director.PlayerTwo.Mode == FighterMode.Rebound, "A对A时 P2 未进入弹回");
                    Require(Mathf.Approximately(director.PlayerOne.Health, director.PlayerOne.MaximumHealth) &&
                            Mathf.Approximately(director.PlayerTwo.Health, director.PlayerTwo.MaximumHealth),
                        "A对A错误扣血");
                    NextStage();
                    break;

                case 2 when (director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free) || Elapsed >= 8f:
                    Require(director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free,
                        "A对A弹回动画未正常结束");
                    director.PlayerOne.SubmitAttack("B");
                    director.PlayerTwo.SubmitAttack("C");
                    NextStage();
                    break;

                case 3 when director.PlayerTwo.Mode == FighterMode.Rebound || Elapsed >= 8f:
                    Require(director.PlayerOne.Mode == FighterMode.Attack, "B对C时 B方未继续成功动画");
                    Require(director.PlayerTwo.Mode == FighterMode.Rebound, "B对C时 C方未弹回");
                    PairParticipantData bcSecond = director.Catalog.GetPair("B", "C", out _).second;
                    Require(Mathf.Approximately(director.PlayerTwo.Health,
                            director.PlayerTwo.MaximumHealth - bcSecond.damage),
                        "B对C时 C方伤害与JSON不一致");
                    Require(director.PlayerTwo.DelayEffectFrames > bcSecond.nextAttackDelayFrames * 0.5f,
                        "B对C时 C方未获得JSON配置的延迟条");
                    NextStage();
                    break;

                case 4 when (director.PlayerOne.CurrentAttack != null &&
                             director.PlayerOne.CurrentAttack.ActualDuration -
                             director.PlayerOne.CurrentAttack.Elapsed <= 0.3f) || Elapsed >= 8f:
                    Require(director.PlayerOne.CurrentAttack != null, "等待缓冲测试时 P1 攻击已意外结束");
                    SetFarRange();
                    director.PlayerOne.SubmitAttack("A");
                    director.PlayerOne.SubmitAttack("B");
                    director.PlayerTwo.SubmitAttack("A");
                    NextStage();
                    break;

                case 5 when (director.PlayerOne.CurrentAttack != null && director.PlayerTwo.CurrentAttack != null &&
                             director.PlayerOne.CurrentAttack.Definition.id == "B" &&
                             director.PlayerTwo.CurrentAttack.Definition.id == "A" &&
                             director.PlayerOne.BufferedInputCount == 0 && director.PlayerTwo.BufferedInputCount == 0) || Elapsed >= 8f:
                    Require(director.PlayerOne.CurrentAttack != null &&
                            director.PlayerOne.CurrentAttack.Definition.id == "B",
                        "最新输入B没有优先于旧输入A执行");
                    Require(director.PlayerOne.Stance >= 0f &&
                            director.PlayerOne.Stance < director.PlayerOne.MaximumStance,
                        "最新输入执行后架势消耗不正确");
                    NextStage();
                    break;

                case 6 when (director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free) || Elapsed >= 8f:
                    Require(director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free,
                        "缓冲攻击未正常结束");
                    SetCloseRange();
                    expectedHealth = director.PlayerTwo.Health;
                    director.PlayerOne.SubmitAttack("A");
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
                    Require(Mathf.Approximately(director.PlayerTwo.Health,
                            expectedHealth - director.Catalog.GetAttack("A").normalDamage),
                        "普通命中未在有效期结束后结算");
                    NextStage();
                    break;

                case 9 when (director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free) || Elapsed >= 8f:
                    Require(director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free,
                        "普通命中测试结束后角色未恢复自由状态");
                    director.PlayerOne.RestoreVitals();
                    director.PlayerTwo.RestoreVitals();
                    SetCloseRange();
                    director.PlayerOne.SubmitAttack("A");
                    NextStage();
                    break;

                case 10 when (director.PlayerOne.CurrentAttack != null &&
                              director.PlayerOne.CurrentAttack.Phase == AttackPhase.Active &&
                              director.PlayerOne.CurrentAttack.ActiveEndFrames -
                              director.PlayerOne.CurrentAttack.ElapsedFrames <= 5f) || Elapsed >= 8f:
                    Require(director.PlayerOne.CurrentAttack != null &&
                            director.PlayerOne.CurrentAttack.Phase == AttackPhase.Active,
                        "无法进入高韧性承伤测试时机");
                    director.PlayerTwo.SubmitAttack("B");
                    NextStage();
                    break;

                case 11 when director.PlayerTwo.Health < director.PlayerTwo.MaximumHealth || Elapsed >= 8f:
                    Require(Mathf.Approximately(director.PlayerTwo.Health,
                            director.PlayerTwo.MaximumHealth - director.Catalog.GetAttack("A").normalDamage),
                        "高韧性后手没有正常承受先手伤害");
                    Require(director.PlayerTwo.Mode == FighterMode.Attack &&
                            director.PlayerTwo.CurrentAttack?.Definition.id == "B",
                        "B的出手韧性高于A时仍被错误打断");
                    NextStage();
                    break;

                case 12 when (director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free) || Elapsed >= 8f:
                    Require(director.PlayerOne.Mode == FighterMode.Free && director.PlayerTwo.Mode == FighterMode.Free,
                        "高韧性换血测试未正常结束");
                    director.PlayerOne.RestoreVitals();
                    director.PlayerTwo.RestoreVitals();
                    SetCloseRange();
                    director.PlayerOne.SubmitAttack("B");
                    NextStage();
                    break;

                case 13 when (director.PlayerOne.CurrentAttack != null &&
                              director.PlayerOne.CurrentAttack.Phase == AttackPhase.Active &&
                              director.PlayerOne.CurrentAttack.ActiveEndFrames -
                              director.PlayerOne.CurrentAttack.ElapsedFrames <= 5f) || Elapsed >= 8f:
                    Require(director.PlayerOne.CurrentAttack != null &&
                            director.PlayerOne.CurrentAttack.Phase == AttackPhase.Active,
                        "无法进入低韧性打断测试时机");
                    director.PlayerTwo.SubmitAttack("A");
                    director.PlayerTwo.SubmitAttack("C");
                    Require(director.PlayerTwo.BufferedInputCount == 1, "打断前未建立输入缓冲");
                    NextStage();
                    break;

                case 14 when director.PlayerTwo.Mode == FighterMode.Hit || Elapsed >= 8f:
                    Require(director.PlayerTwo.Mode == FighterMode.Hit,
                        "A的出手韧性低于B时没有被打断");
                    Require(Mathf.Approximately(director.PlayerTwo.Health,
                            director.PlayerTwo.MaximumHealth - director.Catalog.GetAttack("B").normalDamage),
                        "低韧性后手没有正常承受B的伤害");
                    Require(director.PlayerTwo.CurrentAttack == null,
                        "低韧性后手被打断后仍保留当前攻击");
                    Require(director.PlayerTwo.BufferedInputCount == 0,
                        "低韧性后手被打断后没有清空输入缓冲");
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

    private static void VerifySimulationProtocol()
    {
        CombatCatalogData catalog = director.Catalog;
        CombatSimulation probe = new CombatSimulation(catalog, -1f, 0f, 1f, 0f);
        CombatSnapshot snapshot = probe.Step(
            new CombatInputCommand(1, 1, 0f, 0f, "A"),
            new CombatInputCommand(1, 2, 0f, 0f));
        Require(snapshot.tick == 1 && snapshot.playerOne.currentAttack?.actionId == "A",
            "CombatInputCommand 未驱动纯逻辑动作");
        bool emittedAttack = false;
        foreach (CombatEvent combatEvent in probe.Events)
            if (combatEvent.type == CombatEventType.AttackStarted && combatEvent.sourcePlayer == 1)
                emittedAttack = true;
        Require(emittedAttack, "纯逻辑层未产生 AttackStarted 事件");

        string json = JsonUtility.ToJson(snapshot);
        CombatSnapshot decoded = JsonUtility.FromJson<CombatSnapshot>(json);
        probe.ApplySnapshot(decoded);
        CombatSnapshot restored = probe.CaptureSnapshot();
        Require(restored.tick == snapshot.tick &&
                restored.playerOne.currentAttack?.actionId == snapshot.playerOne.currentAttack?.actionId &&
                Mathf.Approximately(restored.playerOne.currentAttack?.elapsedFrames ?? -1f,
                    snapshot.playerOne.currentAttack?.elapsedFrames ?? -2f),
            "CombatSnapshot 序列化恢复不完整");

        CombatSimulation reboundProbe = new CombatSimulation(catalog, -0.75f, 0f, 0.75f, 0f);
        long reboundTick = 1;
        CombatSnapshot reboundSnapshot = reboundProbe.Step(
            new CombatInputCommand(reboundTick, 1, 0f, 0f, "A"),
            new CombatInputCommand(reboundTick, 2, 0f, 0f, "A"));
        while (reboundSnapshot.playerOne.mode != FighterMode.Rebound && reboundTick < 240)
        {
            reboundTick++;
            reboundSnapshot = reboundProbe.Step(
                new CombatInputCommand(reboundTick, 1, 0f, 0f),
                new CombatInputCommand(reboundTick, 2, 0f, 0f));
        }
        AttackAnimationData reboundAnimation = catalog.GetAttack("A").animation;
        Require(reboundSnapshot.playerOne.mode == FighterMode.Rebound &&
                reboundSnapshot.playerTwo.mode == FighterMode.Rebound,
            "A对A未进入双方弹回状态");
        Require(reboundSnapshot.playerOne.lockedAnimationStartFrame >=
                reboundAnimation.sourceActiveStartFrame &&
                reboundSnapshot.playerOne.lockedAnimationStartFrame <
                reboundAnimation.sourceActiveEndFrame,
            "弹回动画没有从动作对成立时的实际挥击帧接入");
        string reboundJson = JsonUtility.ToJson(reboundSnapshot);
        CombatSnapshot reboundDecoded = JsonUtility.FromJson<CombatSnapshot>(reboundJson);
        Require(Mathf.Approximately(reboundDecoded.playerOne.lockedAnimationStartFrame,
                reboundSnapshot.playerOne.lockedAnimationStartFrame),
            "CombatSnapshot 未保存弹回动画接入帧");

        CombatSimulation delayProbe = new CombatSimulation(catalog, -0.75f, 0f, 0.75f, 0f);
        long tick = 1;
        CombatSnapshot delaySnapshot = delayProbe.Step(
            new CombatInputCommand(tick, 1, 0f, 0f, "B"),
            new CombatInputCommand(tick, 2, 0f, 0f, "C"));
        while (delaySnapshot.playerTwo.delayEffectFrames <= 0f && tick < 240)
        {
            tick++;
            delaySnapshot = delayProbe.Step(new CombatInputCommand(tick, 1, 0f, 0f),
                new CombatInputCommand(tick, 2, 0f, 0f));
        }
        Require(delaySnapshot.playerTwo.delayEffectFrames > 0f, "动作对未写入延迟条");
        float delayBeforeAdvance = delaySnapshot.playerTwo.delayEffectFrames;
        tick++;
        delaySnapshot = delayProbe.Step(new CombatInputCommand(tick, 1, 0f, 0f),
            new CombatInputCommand(tick, 2, 0f, 0f));
        Require(delaySnapshot.playerTwo.delayEffectFrames < delayBeforeAdvance,
            "延迟条没有在弹回状态持续递减");
        while (delaySnapshot.playerTwo.mode != FighterMode.Free && tick < 360)
        {
            tick++;
            delaySnapshot = delayProbe.Step(new CombatInputCommand(tick, 1, 0f, 0f),
                new CombatInputCommand(tick, 2, 0f, 0f));
        }
        Require(delaySnapshot.playerTwo.mode == FighterMode.Free &&
                delaySnapshot.playerTwo.delayEffectFrames > 0f,
            "延迟条未能保留到弹回结束后的有效时间窗口");
        tick++;
        delaySnapshot = delayProbe.Step(new CombatInputCommand(tick, 1, 0f, 0f),
            new CombatInputCommand(tick, 2, 0f, 0f, "A"));
        Require(delaySnapshot.playerTwo.currentAttack?.TimeScale > 1f &&
                Mathf.Approximately(delaySnapshot.playerTwo.delayEffectFrames, 0f),
            "下一动作没有消费当时剩余的延迟条");
    }

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
        if (Application.isBatchMode) EditorApplication.Exit(2);
    }
}
