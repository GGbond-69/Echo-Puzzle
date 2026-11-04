# Echo Puzzle — M0 / M1 进展说明

全黑 3D 第三人称迷宫，靠声波反射的光带反推墙体形状。

---

## 怎么跑起来

**直接按 Play 就行。**

打开工程 → 任意场景 → 点上方 Play 按钮。关卡、玩家、相机、后处理、声波系统、
首页 UI 全部在运行时自动搭好，然后停在游戏首页。

不需要新建空物体、不需要挂脚本、不需要点菜单。

这是靠 `EchoRuntimeBootstrap` 实现的：它用 `[RuntimeInitializeOnLoadMethod]`
在场景加载后自动检查一次——

| 场景情况 | 行为 |
|---|---|
| 场景里**没有** `SonarSystem` | 自动创建 `EchoPuzzle_AutoBootstrap`（挂 `EchoBootstrap`），生成整个游戏 |
| 场景里**已经有** `SonarSystem` | 认为场景是搭好的，什么都不做 |

想关掉自动启动 → 把 `Assets/EchoPuzzle/Scripts/Bootstrap/EchoRuntimeBootstrap.cs` 里的
`AutoStart` 改成 `false`。

### 如果旧测试场景里有残留的手工方块

早期版本的测试场景（比如 `SampleScene`）里可能残留着一些手搓方块——包括旧版的"圆环墙壁"。
它们的几何和我们生成的关卡完全对不上，叠在一起会互相穿模。

`EchoRuntimeBootstrap` 会自动把这些根级物件停用（`DisableLegacyGeometry = true`），
并在 Console 打一行日志说明。想保留它们就把这个开关改成 `false`。

### 手动模式：把关卡直接生成到场景里

如果你想在 **Scene 视图里直接编辑**关卡（拖动墙体、改尺寸），用菜单：

| 菜单 | 作用 |
|---|---|
| `EchoPuzzle/在场景中生成关卡` | 生成真正的 GameObject 到当前场景 |
| `EchoPuzzle/切换墙体可见（编辑期）` | 墙体在「灰色可见」和「全黑不可见」之间切换 |
| `EchoPuzzle/清除生成的关卡` | 删掉 `EchoPuzzle_Level` 根节点 |

注意：**这两个菜单在 Play 模式下会拒绝执行**（会弹框提示）。
正确顺序是「停止 Play → 点菜单 → 再 Play」。生成之后场景里就有 `SonarSystem` 了，
自动启动不会再插手。

---

## 游戏流程与界面

```
首页(Home) ──Start──→ 进行中(Playing) ──Esc──→ 暂停(Paused)
                          │                        │ Resume / Restart
                          │←───────────────────────┘
                          │
                          └── 走到终点 ──→ 通关(Won)
```

| 界面 | 标题 | 按钮 |
|---|---|---|
| **首页** | `ECHO PUZZLE` | `Start` / `Exit`（退出程序） |
| **暂停** | `PAUSE` | `Resume` / `Restart` / `Exit`（**回首页，不是退出程序**） |
| **通关** | `YOU WIN!` + 通关时间 | `Restart` / `Exit`（**回首页**） |

- 背景全黑，所有文字纯白，**按钮只有文字，没有任何轮廓和底框**
- 按 `Esc` 在「进行中 ↔ 暂停」之间切换
- 光标规则：`进行中`/`通关` 锁定光标；`首页`/`暂停` 解锁，方便点按钮
- 界面按 1080 高度等比缩放，任何分辨率下版式一致

界面代码在 `Assets/EchoPuzzle/Scripts/UI/GameUI.cs`，版式和尺寸参数都在 Inspector 上
（`titleSize` / `buttonSize` / `buttonSpacing` / `titleTop`）。

---

## 声波长什么样：曳光弹

每次发声会朝周围发射一圈射线。每根射线渲染成一条**连续的光带**：

- **会生长**：光带从声源向外长出来，最前端是一颗更亮的弹头，
  位置 = `(当前时间 - 出发时间) × 声速`。所有顶点都会被夹在波前之后，
  所以看起来就是一条拖着尾巴往前飞的曳光弹。
- **尾巴会渐隐**：每个顶点的不透明度 = `初始不透明度 × exp(-已传播时间 × 衰减速度)`。
  尾巴是"被扫过最久"的地方，所以自然最暗；头部最新，所以最亮。
- **会反弹**：碰到墙按法线反射，可以弹 1～3 次（`maxBounces`）。
- **和地形无关**：光带是几何体，不是粒子点。走廊里再短的一段也是连续的一条线，
  不会因为射程短就只剩几个点。**宽度在屏幕上是恒定的** ——
  光带的四边形按"一条边向上、一条边指向相机"构造（不是相机的右向量），
  所以无论声波朝哪个方向飞、视角怎么转，都不会缩成一颗点或者变成一根隐形的线。

---

## 想调的东西在哪

所有手感参数都在 **`Player` 物体上的 `PlayerController` 组件**里，按 `Header` 分好了组，
改完立刻生效，不用改代码也不用重新生成关卡。

### ① 四种声波

| 分组 | 对应操作 |
|---|---|
| `① 走路声波` | `W A S D` 正常移动 |
| `② 冲刺声波` | `Shift` + 移动 |
| `③ 潜行声波` | `Ctrl` / `C` + 移动 |
| `④ 空格踏步声波` | 按 `空格` |

每一组里都有：

| 字段 | 含义 |
|---|---|
| `enabled` | 关掉这一种就不发声了 |
| `color` | **颜色**。想要纯白就保持白色 |
| `initialAlpha` | **初始不透明度 0..1** |
| `rayCount` | 射线数量 —— 最影响「能不能看懂墙的形状」，也是性能大头 |
| `range` | 射程（米） |
| `speed` | 声速，光带往前飞的快慢 |
| `trailSpacing` | 光带采样间距（米），越小越顺滑 |
| `maxBounces` | 反射次数 |
| `bounceFalloff` | 每次反射后的不透明度衰减系数 |
| `lifetimeCap` | 最长存活时长（秒），保险丝 |

**关于「不透明度变化速度」：**

这个值**在 `SonarTrailRenderer.fadeSpeed` 上，是全局统一的**，四种声波共用 ——
衰减速度是声波的物理属性，分开调反而会让手感不一致。

```
当前不透明度 = 初始不透明度 × exp(-已传播时间 × fadeSpeed)
```

所以**能亮多久完全由 `initialAlpha` 决定**：

```
可见时长 ≈ ln(初始不透明度 / 可见阈值) / fadeSpeed
```

按默认 `fadeSpeed = 1.6`、`visibleThreshold = 0.006`：

| 步伐 | `initialAlpha` | 亮到看不见大约 |
|---|---|---|
| 潜行 | `0.22` | 2.2 s |
| 走路 | `0.55` | 2.8 s |
| 冲刺 | `0.75` | 3.0 s |
| 空格踏步 | `1.00` | 3.2 s |

想让**全部**声波持续更久/更短 → 只改 `fadeSpeed`，四档关系不变。
想让**某一种**更持久 → 只改它的 `initialAlpha`。

### ② 光带的粗细和强度

在 `SonarSystem` 子物体 **`SonarTrailRenderer`** 上：

| 字段 | 默认 | 含义 |
|---|---|---|
| `glowIntensity` | `1.8` | 整体发光强度，**声波太暗先加大这个** |
| `trailWidth` | `0.09` | 光带宽度（米） |
| `fadeSpeed` | `1.6` | **全局衰减速度** |
| `visibleThreshold` | `0.006` | 低于这个不透明度就不画了 |
| `drawHead` | `true` | 最前端那颗更亮的弹头 |
| `maxTrailLength` | `34` | 光带实际铺多长（米） |

### ③ 移动速度

`PlayerController` 的 `移动速度（米/秒）` 分组：

| 字段 | 默认 |
|---|---|
| `sneakSpeed` | `1.35` |
| `walkSpeed` | `3.4` |
| `runSpeed` | `6.2` |
| `accel` | `16` |

**脚步节奏**（走过多少米踏一步）在紧邻的 `脚步节奏` 分组：

| 字段 | 默认 |
|---|---|
| `sneakStride` | `4.0` |
| `walkStride` | `1.9` |
| `runStride` | `1.5` |
| `minEmitInterval` | `0.16` 秒 |

调小 stride = 声波更连贯，但生成更频繁。

### ④ 发射方向与反射

`PlayerController` 的 `发射方向` / `反射之后` 分组：

- `hemisphereFromGround` — 勾上则脚步只往地表上半球辐射（不会穿地板）
- `minAngleToAxes` — 避让角，避开正前/正后/正左/正右/正上这 5 个正方向
- `spreadMode` — `Hemisphere`（漫反射）/ `Cone`（0 度即严格镜面单射线）/ `Sphere`
- `spreadAngle` — 扩散半角

---

## 操作

| 按键 | 作用 |
|---|---|
| `W A S D` | 移动（**走一段距离就踏出一圈声波**） |
| 鼠标 | 转视角 |
| `Shift` | 冲刺 |
| `Ctrl` / `C` | 潜行 |
| `空格` | 原地踏步：一次最亮的 ping |
| `Esc` | 解锁鼠标 |

> 水平输入在 `PlayerController.HandleMove` 里有一行 `h = -h;`。
>
> **相机可以穿墙，而且必须能穿。** 如果给相机加防穿墙，玩家就能靠"相机在哪儿被挡住了"
> 反推出墙体轮廓 —— 这是一条会直接破坏玩法的信息泄漏渠道。
> `cameraCollision` **默认关闭**。

---

## 出问题时先看这两个诊断

### 声波看不见 → 打开 `SonarSystem.showOnScreenStats`

```
声波统计
  波源 3   待发射射线 412   飞行中批次 1
  登记光带 1280   绘制四边形 18432
  上一次发声 0.3 秒前   射线 160
  声波正常
```

- **波源 = 0 且一直「从未」** → 没在发声，走两步试试（W）
- **绘制四边形 = 0** → 光带被剔除了，看 `fadeSpeed` / `initialAlpha` / `visibleThreshold`
- **四边形 > 0 但屏幕还是黑的** → 渲染问题，加大 `SonarTrailRenderer.glowIntensity`

### 撞到看不见的墙 → 看 Console 的 `[EchoPuzzle] 关卡自检`

会打印：用**和玩家一样粗的球体**扫过走廊和房间的结果，以及**所有碰撞体的类型和包围盒**。

三个已经踩过的坑，写下来免得再犯：

> **1. 波源必须在所有批次回收完之后才能删除。**
> `SonarSystem.Update` 里如果批次一调度出去就把 `Wave` 从 `m_Waves` 删掉，
> 那么下一帧 `ProcessBatch` 命中墙面后生成的子射线，会挂在**一个已经脱离列表的
> Wave** 上 —— 反射永远不生效，`绘制四边形` 恒为 0（表现就是"完全看不到声波"）。
> 现在用 `Wave.InFlight` 计数：只有 `InFlight == 0 && Pending.Count == 0` 才算结束。

> **2. Mesh 每帧重建时必须给出确定的包围盒。**
> `Mesh.Clear()` 之后再 `SetVertices` **不会**刷新 `bounds`，
> Unity 会拿上一帧的旧包围盒做视锥剔除 —— 表现就是"某个视角看得到声波，
> 一转动相机全部**同时**消失，转回来又同时出现"。
> 现在直接覆盖 `MeshRenderer.localBounds`。

> **3. Unity 的 `PrimitiveType.Cylinder` 自带 `CapsuleCollider`，不是圆柱体。**
> 用它做房间地板/天花板时，胶囊上下两个半径 = 圆盘半径的半球会鼓进房间里，
> 等于凭空多出两道弧形墙。现在地板和天花板都改用**扁平的 Box**。

> **4. 运行时批量新建碰撞体之后要 `Physics.SyncTransforms()`。**
> 否则 PhysX 用的是物体被摆放**之前**的位置，表现也是"撞空气墙"。

---

## 关卡

`LevelBuilder.cs` 里用常量**手写**，不是随机生成。俯视布局：

```
                    走廊 B (沿 +X, 宽 4, 长 20)
  ┌───────────────────────────────┐  ← 终点
  │
  │          ╭─────────╮
  └──────────│ 圆形房间 │            (半径 8, 高 3.2)
  ┌──────────╰─────────╯
  │ 出生点
  └──────────┐
            走廊 A (沿 +Z, 宽 4, 长 16)
```

- 出生点在走廊 A 起点（约 `z = 2.5`），面朝 `+Z`。
  直走约 **13.5 m** 进入圆形房间，房间直径 16 m，可以一直走到 `z ≈ 32`
- 终点在走廊 B 末端，有触发区 + 一对发光立柱
- 天花板高度 `CeilingHeight = 3.2` —— **这个值是声波玩法的核心参数**
- **每块圆墙板的正面都朝向圆心**（`yaw = angle + 180°`，
  因为 Cube 的正面是 +Z，要转 180° 才能让 -Z 轴指向圆心）。
  两个门洞开在方向 `270°`（走廊 A）和 `0°`（走廊 B）

### 圆墙拼接（改动前先读）

面板宽度必须同时满足两件事：

```
panelW = max( 弦长 , 2 × 墙厚 × tan(半步角) ) × 1.03
```

1. **弦长** `2 × RoomRadius × sin(半步角)` —— 少了会出现锯齿缝，圆轮廓不闭合
2. **墙厚造成的斜角外伸** `2 × 墙厚 × tan(半步角)` —— 矩形面板有厚度，
   斜角会往相邻面板的空档里伸

**门洞半角要留足余量。** 靠门洞最边缘那块面板的斜角还会往门里伸一点，
门洞实际净宽如果小于走廊宽度，就会在门口形成空气墙。
现在 `96` 段 + `12°` 门洞半角，净宽约 5.6 m，走廊只有 4 m。

---

## 代码结构

```
Assets/EchoPuzzle/
├── Editor/
│   └── EchoSceneBuilder.cs       菜单工具：把整关生成到当前场景
├── Shaders/
│   ├── EchoSonarTrail.shader     声波光带：生长 + 逐顶点渐隐 + 加色混合
│   ├── EchoMatte.shader          墙体：黑（正式）/ 灰+弱光照（调试）
│   └── EchoOutline.shader        玩家：白色外扩描边 + 边缘光
└── Scripts/
    ├── Core/
    │   ├── SonarSystem.cs          声波传播核心（事件队列 + RaycastCommand 批处理）
    │   ├── SonarTrailRenderer.cs   光带铺面 + 动态 Mesh（1 个 DrawCall）
    │   └── EchoMaterials.cs        运行时创建材质
    ├── Level/
    │   └── LevelGoal.cs            终点触发（通关后通知 GameUI）
    ├── UI/
    │   └── GameUI.cs               首页 / 暂停 / 通关三个界面 + 状态机
    ├── Player/
    │   └── PlayerController.cs     第三人称移动 + 相机 + 四种声波参数
    └── Bootstrap/
        ├── LevelBuilder.cs         手写教学关：走廊 / 圆房间 / 走廊
        ├── EchoBootstrap.cs        运行时组装整个场景
        └── EchoRuntimeBootstrap.cs 按 Play 就自动启动的入口
```

---

## 出问题时的排查手段

诊断用的屏幕面板已经全部拆掉了。如果又出现"声波看不见"之类的问题，
按下面的顺序二分定位：

| 怀疑 | 怎么查 |
|---|---|
| 关卡没生成 | 层级里有没有 `EchoPuzzle_AutoBootstrap`？Console 有没有 `[EchoPuzzle] 已自动生成关卡` |
| 声波没生成 | 在 `SonarSystem` 里临时加一行 `Debug.Log(m_Waves.Count)`，看波源数是否非 0 |
| 光带没铺出来 | 在 `SonarTrailRenderer.LateUpdate` 末尾打印 `quads`，看几何量是否非 0 |
| 几何有了但看不见 | 加大 `SonarTrailRenderer.glowIntensity` / `EchoBootstrap.bloomIntensity` |
| 撞到看不见的墙 | `Physics.SyncTransforms()` 有没有在生成完关卡后调用 |

---

## 光带是怎么画出来的（改动前先读）

`SonarTrailRenderer` 每帧重新铺一遍几何：

1. 每条射线记录 `origin / dir / length / startTime（波前离开声源的时刻）/ speed / 初始不透明度`
2. 每帧算波前位置 `front = (now - startTime) × speed`
3. 尾巴位置 `tail = front - 可见时长 × speed`
4. 在 `[tail, min(front, length)]` 区间里每 `trailSpacing` 米铺一个四边形
5. 每个顶点的 `color.a` = 波前扫过它那一刻的不透明度 —— 头和尾天生不同，所以自带渐变

**四边形方向**：一条边是"相机上向量去掉沿射线方向分量"，另一条边是
"指向相机方向去掉沿射线方向分量"。两者都不是相机右向量，
所以水平飞行的声波看起来像光带而不是隐形的线。

**注意**：每个四边形只有 2 个不同的 alpha（尾端和头端），
这是刻意的 —— 逐顶点插值的透明度才是"曳光弹"的尾巴。

---

## 还没做（按你的要求暂时跳过）

- 原地踏步做成独立音效（目前空格是调试用的强 ping）
- 投掷石子
- 特殊地面、机关、怪物

接口已经留好：
- `SonarSystem.Emit(position, p)` 是通用发声入口，投掷石子只是换个位置和参数
- `EmitMode` 支持 `Sphere` / `Hemisphere` / `Cone`
- `PlayerController.CurrentNoiseLoudness` 已按 走/冲/潜 输出 `1.0 / 1.6 / 0.25`，
  这就是将来怪物听觉 AI 的输入

## 已知取舍

- 光带四边形上限 `SonarTrailRenderer.MaxQuads = 160000`
- 单次发声的射线事件上限 `MaxEventsPerWave = 1600`
- 反射后的扩散是**随机近似**（朗伯分布），不是精确声学
- 声波**不做深度遮挡**，可以穿过墙被看到 —— 符合"声波信息即全部信息"的设计
