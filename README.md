# Swordman2 战斗系统技术架构与扩展接口

## 1. 文档范围

本文面向参与 Swordman2 开发的程序、动画和数值成员，说明当前战斗系统的：

- 模块边界与文件依赖；
- 启动、输入、模拟、命中和动画调用链；
- JSON 数据结构与约束；
- 主要 C# 类型、公开接口和状态所有权；
- 新增动作、动作对和动画资源的扩展流程。

战斗系统采用“C# 固定规则 + JSON 数值配置”结构。JSON 不包含脚本、C# 类型名或其他可执行内容；C# 负责校验数据、执行规则和解释数值。

## 2. 目录与文件职责

### 2.1 战斗数据和资源

| 路径 | 职责 |
|---|---|
| `Swordman2/Assets/Resources/CombatData/combat_catalog.json` | 全局战斗设置、键位、音频、动作和动作对数据 |
| `Swordman2/Assets/Resources/Models/Swordsman.fbx` | 角色模型和动画片段 |
| `Swordman2/Assets/Resources/Audio/` | 普通命中和动作对音效 |
| `New Folder/2.blend` | 角色与动画源工程 |

### 2.2 运行时脚本

| 文件 | 主要职责 |
|---|---|
| `CombatCatalog.cs` | JSON 数据类型、加载、校验、动作和动作对查询 |
| `CombatDefinitions.cs` | 运行时枚举和 `AttackRuntime` 动作实例 |
| `CombatBootstrap.cs` | 启动入口，创建场地、角色、摄像机、音频和 HUD |
| `CombatDirector.cs` | 输入采集、固定步模拟、普通命中和动作对结算 |
| `FighterController.cs` | 单角色状态、移动、攻击、缓冲、架势、生命和延迟条 |
| `FighterAnimationPlayer.cs` | Playables 动画图、混合、播放和攻击动画时间定位 |
| `CombatAudio.cs` | 音效加载、动作对预测序列和重叠播放 |
| `CombatHud.cs` | 运行时状态 UI 和动态数据展示 |
| `SplitCameraFollow.cs` | 分屏越肩摄像机跟随 |

### 2.3 编辑器脚本

| 文件 | 主要职责 |
|---|---|
| `SwordsmanModelImporter.cs` | 配置 FBX 动画导入、循环和根运动选项 |
| `CombatPlayModeVerifier.cs` | 战斗系统集成验证入口 |

## 3. 模块依赖

```text
combat_catalog.json
        │
        ▼
CombatCatalogLoader
        │  CombatCatalogData
        ▼
CombatBootstrap
        │
        ├── FighterController × 2
        │       └── FighterAnimationPlayer
        │
        ├── CombatDirector
        │       ├── FighterController × 2
        │       └── CombatAudio
        │
        ├── CombatHud
        │       └── CombatDirector
        │
        └── SplitCameraFollow × 2
```

依赖方向应保持单向：

- 数据类型不依赖角色、UI 或场景对象；
- 角色控制器不负责双方战斗规则；
- 双方交互只由 `CombatDirector` 结算；
- HUD 只读取公开状态，不写入战斗内部字段；
- 动画缺失不能中断战斗逻辑。

## 4. 启动和运行流程

### 4.1 初始化

`CombatBootstrap.BuildDemo()` 通过 `RuntimeInitializeOnLoadMethod` 在场景加载后运行：

1. 检查场景中是否已经存在 `CombatDirector`，防止重复初始化。
2. 调用 `CombatCatalogLoader.TryLoad()` 读取并校验 JSON。
3. 设置目标帧率并创建场地。
4. 从 Resources 加载模型和动画片段。
5. 创建两个 `FighterController`，互相设置 `Opponent`。
6. 创建 `CombatAudio` 和 `CombatDirector`。
7. 创建两个分屏摄像机和一个 `CombatHud`。

JSON 结构或规则数据无效时停止初始化。动画片段缺失只记录警告，战斗逻辑继续可用。

### 4.2 每帧与固定逻辑步

`CombatDirector.Update()` 每个 Unity 帧完成输入采集，并把 `Time.deltaTime` 累加到内部 accumulator。随后以：

```text
simulationStep = 1 / settings.logicFrameRate
```

重复执行 `Simulate()`。默认逻辑帧率为 120Hz。

每个逻辑步的顺序：

1. 设置双方移动输入；
2. 更新双方朝向；
3. 推进移动；
4. 推进角色状态和动作逻辑帧；
5. 检查有效期、范围和时间重叠；
6. 结算动作对或已结束有效期的普通命中。

## 5. 时间模型

所有战斗时长使用逻辑帧：

```text
实际秒数 = 逻辑帧数 / settings.logicFrameRate
```

音频偏移、动画混合和摄像机平滑等纯表现参数继续使用秒。

每个动作独立保存：

- `windupFrames`：前摇；
- `activeFrames`：有效期；
- `recoveryFrames`：后摇。

动作总帧数由 `AttackDefinition.TotalFrames` 计算，不再存在全局动作速度常量。

## 6. JSON 根结构

`CombatCatalogData` 对应 JSON 根对象：

```json
{
  "settings": {},
  "commonAnimations": {},
  "controls": {},
  "audio": {},
  "attacks": [],
  "actionPairs": []
}
```

| 节点 | C# 类型 | 内容 |
|---|---|---|
| `settings` | `CombatSettings` | 逻辑帧率、生命、架势、移动、缓冲和延迟换算 |
| `commonAnimations` | `CommonAnimationData` | 待机、移动和受击动画 |
| `controls` | `CombatControls` | P1、P2 和系统键位 |
| `audio` | `CombatAudioData` | 音效路径、预测和循环参数 |
| `attacks` | `AttackDefinition[]` | 动作属性、逻辑阶段和动画源分段 |
| `actionPairs` | `ActionPairDefinition[]` | 每种无序动作组合的双方结果和数值 |

修改 JSON 后需重新进入 Play Mode，当前不支持运行中热重载。

## 7. 全局设置接口

`CombatSettings` 字段：

| 字段 | 单位 | 说明 |
|---|---|---|
| `logicFrameRate` | Hz | 固定战斗逻辑频率 |
| `maxHealth` | 数值 | 默认生命上限 |
| `maxStance` | 数值 | 默认架势上限 |
| `temporaryVitalLimit` | 数值 | 临时调节接口允许的最大值 |
| `moveSpeed` | m/s | 角色自由移动速度 |
| `inputBufferFrames` | 逻辑帧 | 最新攻击输入的有效时间 |
| `stanceRecoveryDelayFrames` | 逻辑帧 | 架势开始恢复前的等待时间 |
| `stanceRecoveryDurationFrames` | 逻辑帧 | 从 0 恢复到默认满架势所需时间 |
| `hitReactionFrames` | 逻辑帧 | 普通受击锁定时间 |
| `delayEffectAttackScale` | 倍率 | 延迟条换算为追加动作帧的倍率 |

## 8. 动作数据接口

`AttackDefinition` 示例：

```json
{
  "id": "A",
  "displayName": "轻击",
  "stanceCost": 1.0,
  "normalDamage": 1,
  "startupPoise": 1,
  "windupFrames": 14,
  "activeFrames": 23,
  "recoveryFrames": 23,
  "radius": 2.1,
  "blendSeconds": 0.08,
  "animation": {}
}
```

| 字段 | 说明 |
|---|---|
| `id` | 唯一动作 ID；键位和动作对均通过字符串 ID 引用 |
| `displayName` | UI 和战斗事件显示名称 |
| `stanceCost` | 启动动作需要消耗的架势 |
| `normalDamage` | 普通命中伤害 |
| `startupPoise` | 出手韧性，必须显式配置为非负整数；仅影响前摇被普通命中时是否打断 |
| `windupFrames` | 前摇逻辑帧数，必须大于 0 |
| `activeFrames` | 有效期逻辑帧数，必须大于 0 |
| `recoveryFrames` | 后摇逻辑帧数，必须大于 0 |
| `radius` | 攻击范围，必须大于 0 |
| `blendSeconds` | 切入动作动画时的混合时间 |
| `animation` | `AttackAnimationData` 动画资源与源分段 |

`AttackDefinition` 还提供：

| 成员 | 说明 |
|---|---|
| `TotalFrames` | 三个逻辑阶段帧数之和 |
| `SuccessClip(SlashSide)` | 根据挥砍方向选择成功动画 |
| `ReboundClip(SlashSide)` | 根据挥砍方向选择弹回动画 |

## 9. 动画分段接口

`AttackAnimationData` 保存动画片段名和 FBX 源动作的阶段位置：

```json
{
  "successRightToLeft": "Attack_RtoL_Success",
  "successLeftToRight": "Attack_LtoR_Success",
  "reboundRightToLeft": "Attack_RtoL_Blocked",
  "reboundLeftToRight": "Attack_LtoR_Blocked",
  "sourceFrameRate": 30.0,
  "sourceStartFrame": 1,
  "sourceActiveStartFrame": 18,
  "sourceActiveEndFrame": 38,
  "sourceEndFrame": 52,
  "reboundEntryFrame": 21
}
```

逻辑帧与动画源帧相互独立：

```text
逻辑前摇   → sourceStartFrame 至 sourceActiveStartFrame
逻辑有效期 → sourceActiveStartFrame 至 sourceActiveEndFrame
逻辑后摇   → sourceActiveEndFrame 至 sourceEndFrame
```

`AttackRuntime.SourceAnimationTime` 按当前逻辑阶段分别插值源动画时间，`FighterController.Advance()` 每个逻辑步调用 `FighterAnimationPlayer.SetCurrentTime()`。因此单独改变某一逻辑阶段只会拉伸或压缩对应动画区段。

源分段必须满足：

```text
sourceStartFrame
  < sourceActiveStartFrame
  < sourceActiveEndFrame
  < sourceEndFrame
```

`reboundEntryFrame` 必须位于源动画起止范围内。

## 10. 动作运行时接口

`AttackRuntime` 是一次具体攻击的运行实例，不写回 JSON。

重要成员：

| 成员 | 说明 |
|---|---|
| `Definition` | 本次动作引用的静态 `AttackDefinition` |
| `Side` | 本次挥砍方向 |
| `ElapsedFrames` | 已推进的运行逻辑帧 |
| `TimeScale` | 延迟条作用后整段动作的时间倍率 |
| `RuntimeSpeed` | 动作对成立后继续动作的推进倍率 |
| `PreviousPhase` / `Phase` | 上一逻辑步和当前动作阶段 |
| `HadTemporalOverlap` | 有效期是否曾与对方有效期重叠 |
| `HadMutualRangeOverlap` | 是否曾在双方有效期内互相满足范围 |
| `TargetWasInRange` | 本方有效期内目标是否曾进入范围 |
| `Settled` | 本次攻击是否已经结算 |
| `PairAudioPlayed` | 本次动作对预测音效是否已触发 |
| `ActualActiveStart/End` | 考虑延迟后的有效期秒数边界 |
| `SourceAnimationTime` | 当前逻辑帧对应的动画源时间 |

## 11. 动作对数据接口

动作对采用无序组合，每种组合只保存一条：

```json
{
  "firstAction": "B",
  "secondAction": "C",
  "displayName": "B对C",
  "first": {
    "result": "Continue",
    "speedScale": 0.35,
    "damage": 0,
    "nextAttackDelayFrames": 0
  },
  "second": {
    "result": "Rebound",
    "speedScale": 0.40,
    "damage": 3,
    "nextAttackDelayFrames": 120
  }
}
```

`first` 永远对应 `firstAction`，`second` 永远对应 `secondAction`，不表示 P1/P2。运行时 `CombatCatalogData.GetPair()` 返回 `swapped`，由 `CombatDirector` 将双方数据映射到实际玩家。

动作 ID 必须按不区分大小写的升序保存，例如 B+C，不能保存 C+B。

`PairParticipantData` 字段：

| 字段 | 说明 |
|---|---|
| `result` | `Continue` 或 `Rebound` |
| `speedScale` | 动作对成立后立即作用于当前剩余动作或弹回动作的推进倍率，必须大于 0；`1.0` 为正常速度，小于 `1.0` 会减速 |
| `damage` | 该参与方立即受到的伤害 |
| `nextAttackDelayFrames` | 该参与方获得的延迟条帧数 |

`Continue` 由 `ApplyPairContinuationSpeed()` 处理；`Rebound` 由 `EnterRebound()` 处理。JSON 只选择固定结果并提供数值。

## 12. 延迟条机制

动作对中的 `nextAttackDelayFrames` 通过 `FighterController.ApplyDelayEffect()` 写入角色延迟条。

延迟条在 Free、Attack、Rebound 和 Hit 状态下均按逻辑帧持续递减；游戏暂停时不推进。它不会为了下一动作而暂停或强制保留。角色下一次成功启动攻击时只读取并清空当时的剩余延迟条：

```text
追加动作帧 = DelayEffectFrames × delayEffectAttackScale
TimeScale = (基础总帧 + 追加动作帧) / 基础总帧
```

`TimeScale` 按比例影响前摇、有效期和后摇，动画三段继续分别同步。

当前 A+C 和 B+C 的相关参与方配置为 `nextAttackDelayFrames: 120`。在 `logicFrameRate: 120` 下，延迟条自然有效期为 1 秒；如果完整延迟条立刻用于下一动作，且 `delayEffectAttackScale: 2.0`，则会追加 240 逻辑帧，即 2 秒动作时间。动作对当下的 `speedScale` 与黄色延迟条是两套独立效果，不存在其他隐藏动作对缓速。

## 13. 命中与动作对规则

双方同时处于 `AttackPhase.Active` 时记录有效期重叠；同时互相满足攻击范围且均未结算时成立动作对。

普通命中只在本方有效期结束后结算，必须同时满足：

- 本方有效期内目标曾进入攻击范围；
- 本方有效期与对方有效期没有任何时间重叠；
- 本次攻击尚未被其他规则结算。

如果有效期有时间重叠但没有满足双方攻击范围，则不会成立动作对，也不会在之后转为普通命中。

当先手有效期结束并产生普通命中，而后手在命中前一逻辑步仍处于攻击前摇时，比较双方动作的 `startupPoise`：

- 后手韧性严格大于先手：正常扣血，但保留当前攻击和缓冲，不播放受击动画；
- 后手韧性小于或等于先手：正常扣血，取消当前攻击，清空输入缓冲并播放受击动画；
- 后手不处于攻击前摇：按普通打断受击处理；
- 生命降到 0 不会强制打断，当前系统允许 0 血继续攻击。

有效期采用半开区间 `[windupFrames, windupFrames + activeFrames)`，配置的有效帧数与固定逻辑步严格一致。同一逻辑步中先手离开有效期、后手离开前摇时，使用后手的 `PreviousPhase` 判断出手韧性。

## 14. 输入接口

`CombatControls` 包含两个 `PlayerControls` 和一个 `SystemControls`。

`PlayerControls`：

| 字段 | 说明 |
|---|---|
| `moveLeft/Right/Down/Up` | Unity Input System `Key` 名称 |
| `attacks` | `AttackBinding[]`，把动作 ID 绑定到键名 |

`AttackBinding`：

```json
{ "action": "A", "key": "F" }
```

每个玩家必须绑定全部动作。键名通过 `Enum.TryParse<Key>()` 转换，无效键名会使目录校验失败。

攻击缓冲由 `FighterController.SubmitAttack()` 管理：只保留最新动作，新的输入会替换旧输入并重置缓冲帧数。

## 15. 音频接口

`CombatAudioData`：

| 字段 | 说明 |
|---|---|
| `normalHit` | 普通命中音效 Resources 路径 |
| `pairSequence` | 连续动作对音效路径数组，播放到末尾后循环 |
| `pairChainWindowSeconds` | 动作对仍视为连续序列的最大间隔 |
| `pairPredictionLeadSeconds` | 动作对预测音效最大提前量 |
| `volume` | AudioSource 音量 |

`CombatDirector.TryPlayPredictedPairAudio()` 根据双方未来有效期交集和当前范围预测动作对，`CombatAudio.PlayActionPair()` 负责序列索引与播放。`CombatAudio` 使用一个预加载的二维 `AudioSource`，通过 `PlayOneShot()` 允许音效重叠，不再维护 AudioSource 池或运行时跳过音频开头。

`Assets/Resources/Audio/` 中的战斗音效使用已经裁除前导静音的 WAV/PCM 文件，并启用 `preloadAudioData`。新增或替换短音效时应先清除明显的前导静音，避免把素材空白误判为代码触发延迟。

## 16. 主要 C# 公开接口

### 16.1 CombatCatalogLoader

| 接口 | 用途 |
|---|---|
| `TryLoad(out CombatCatalogData)` | 从 Resources 加载、解析并校验目录 |
| `Validate(CombatCatalogData)` | 返回合并后的校验错误文本；空字符串表示通过 |

### 16.2 CombatCatalogData

| 接口 | 用途 |
|---|---|
| `GetAttack(string id)` | 不区分大小写查询动作 |
| `GetPair(string firstAction, string secondAction, out bool swapped)` | 查询无序动作对并返回参数是否交换 |
| `RebuildLookups()` | 数据重新构造后重建运行时查询表 |

### 16.3 CombatDirector

| 成员 | 用途 |
|---|---|
| `Initialize(FighterController, FighterController, CombatAudio, CombatCatalogData)` | 注入双方角色、音频和目录 |
| `Catalog` | 当前只读战斗目录 |
| `PlayerOne` / `PlayerTwo` | 双方控制器 |
| `LastEvent` | 最近一次战斗事件文本 |

双方交互规则应添加在 `CombatDirector`，不要放入单个 `FighterController`。

### 16.4 FighterController

状态读取接口：

| 成员 | 用途 |
|---|---|
| `Mode` | 当前 Free、Attack、Rebound 或 Hit |
| `CurrentAttack` | 当前攻击实例；非攻击状态为 null |
| `Health` / `Stance` | 当前生命和架势 |
| `DelayEffectFrames` | 当前延迟条逻辑帧 |
| `Position` / `Facing` | 战斗根节点位置与水平朝向 |
| `CurrentAnimation` | 当前动画片段名称 |
| `BufferedInputCount` | 当前缓冲输入数量 |

控制和结算接口：

| 接口 | 调用方/用途 |
|---|---|
| `SetMovementInput(Vector2)` | `CombatDirector` 写入本逻辑步移动输入 |
| `SubmitAttack(string)` | 输入层或其他系统提交动作 ID |
| `UpdateFacing()` | 朝向锁定目标 |
| `UpdateMovement(float)` | 推进自由移动 |
| `Advance(float)` | 推进状态、动画、架势和缓冲 |
| `IsInRangeOf(FighterController, float)` | `CombatDirector` 范围判定 |
| `MarkAttackSettled()` | 标记当前攻击已结算 |
| `EnterRebound(float)` | 应用动作对弹回结果 |
| `ReceiveInterruptingHit(int)` | 扣血、取消攻击、清空缓冲并进入受击锁定 |
| `ReceiveArmoredHit(int)` | 只扣血，保留当前攻击、动画和输入缓冲 |
| `ApplyPairDamage(int)` | 应用动作对直接伤害 |
| `ApplyPairContinuationSpeed(float)` | 应用 Continue 结果的推进倍率 |
| `ApplyDelayEffect(int)` | 写入延迟条帧数 |
| `RestoreVitals()` | 恢复默认生命和架势 |
| `SetTemporaryVitals(float, float)` | 设置临时生命和架势 |
| `Teleport(Vector3)` | 调试或场景系统安全移动 CharacterController |
| `Dispose()` | 销毁角色动画 PlayableGraph |

### 16.5 FighterAnimationPlayer

| 接口 | 用途 |
|---|---|
| `HasClip(string)` | 查询动画片段是否存在 |
| `PlaybackSpeedForDuration(string, float)` | 计算完整动画匹配目标时长的速度 |
| `SetCurrentTime(float)` | 把攻击动画定位到逻辑阶段对应源时间 |
| `ClipDuration(string)` | 查询动画长度 |
| `Play(...)` | 播放并混合动画；缺失片段时只警告 |
| `Tick(float)` | 推进循环和混合状态 |
| `Dispose()` | 销毁 PlayableGraph |

### 16.6 CombatAudio

| 接口 | 用途 |
|---|---|
| `Initialize(CombatAudioData)` | 加载并预载音频，建立一个支持 `PlayOneShot()` 重叠播放的二维 AudioSource |
| `PlayActionPair(float predictedPairTime)` | 按预测动作对时间推进并播放循环序列 |
| `PlayNormalHit()` | 播放普通命中音效 |

### 16.7 CombatHud

`Initialize(CombatDirector)` 注入唯一数据源。HUD 通过 `CombatDirector` 和双方 `FighterController` 读取状态，不应持有独立战斗数值副本。

## 17. 数据校验契约

`CombatCatalogLoader.Validate()` 检查：

- 逻辑帧率、生命和架势基础值；
- 动作 ID 非空、唯一；
- 三个逻辑阶段均大于 0；
- 每个动作显式配置非负整数 `startupPoise`；
- 攻击范围、架势消耗和伤害有效；
- 动画源分段严格递增；
- P1、P2 键名有效且均绑定全部动作；
- 动作对引用有效、顺序正确且不重复；
- 动作对数量完整；
- `result` 只能为 Continue 或 Rebound；
- 动作对速度、伤害和延迟数值有效。

N 个动作必须存在：

```text
N × (N + 1) / 2
```

条无序动作对。

## 18. 新增动作流程

新增动作 D：

1. 在 `attacks` 中增加唯一 ID 为 D 的 `AttackDefinition`。
2. 填写伤害、架势消耗、三个逻辑阶段和范围。
3. 填写出手韧性、四个动画片段名和源动画分段。
4. 在 P1、P2 的 `attacks` 键位数组中分别绑定 D。
5. 若原有动作是 A、B、C，增加 A+D、B+D、C+D、D+D。
6. 重新进入 Play Mode，使目录重新加载并校验。

只使用 Continue、Rebound、伤害、速度和延迟条时无需修改 C#。

## 19. 扩展全新动作对效果

当新效果不能由现有字段表达时，应按以下边界扩展：

1. 在 `PairParticipantData` 增加纯数据字段；
2. 在 `CombatCatalogLoader.ValidateParticipant()` 增加约束；
3. 在 `CombatDirector.ApplyPairValues()` 或 `ApplyPairResult()` 解释该字段；
4. 如效果只影响单角色内部状态，再由 `CombatDirector` 调用新的 `FighterController` 接口；
5. JSON 仍不得保存脚本名、类名或任意可执行内容。

保持“目录选择与提供数值，C# 定义规则语义”的设计边界。

## 20. 有意保留在 C# 的规则

以下规则不应转移到 JSON：

- 有效期重叠的检测方法；
- 动作对要求双方有效期同时成立且互相满足范围；
- 普通命中在有效期结束后确认；
- 输入缓冲只保留最新动作；
- 延迟条的消费时机和状态生命周期；
- Continue、Rebound、Hit 的状态切换；
- 动画缺失时逻辑继续执行；
- 固定逻辑步的推进顺序。

JSON 决定动作、资源、键位、选择和数值；C# 保证规则稳定、可检查且可扩展。
