# 美术素材接入说明

本说明对照《美术需求文档.docx》，记录完整 V0.1 原型的素材入口。`场景.zip` 的船只、海面、船浪、岛岸建筑、码头与岸浪，以及 `物品与维修.zip` 的炸弹、石块、障碍物、破损和维修条已接入。人物、武器及未交付特效仍通过现有挂点替换占位显示；UI 以用户后续方案为准。仓库首页只介绍玩家玩法，本文件记录开发接入约定。

## 当前技术基线

- Unity **2022.3.62f3c1**，现有 **2D URP**，XY 平面正俯视。文档中的“模型”没有定案为 3D 资源。
- 角色、武器和定向投射物以本地 **+X 为正前方**，围绕 Z 轴转向。资源朝向不同时，在显示层调整局部旋转。
- 角色根节点保留刚体、碰撞器和行为脚本；显示、武器、Animator 和角色特效放在 `Visual` 下。动画关闭 **Apply Root Motion**，不动画化角色根节点的位置、旋转或缩放。
- 已交付场景素材使用 **180 PPU**（船、翅膀、船浪、海面）和 **240 PPU**（岛岸）；角色等占位贴图仍为 **100 PPU**。场景图使用 Bilinear、完整矩形 Sprite、无 mipmap，保留透明度；三帧动画共用画布与 Pivot，不逐帧裁边。
- 甲板维修网格为 12×5，每格 1 单位，格索引与旧存档兼容。物理边线贴合左侧上、下通行区和船头转角，舱体、烟囱、箱子和桶各自使用局部碰撞箱。

| 对象 | 当前物理基准 | 显示调整位置 |
| --- | --- | --- |
| 甲板 | 闭合多段边线，隐藏边线显示；`Deck Boundary` 下另有舱体和货物碰撞箱 | `Boat/Hull Visual`、`Boat/Wing Left`、`Boat/Wing Right` |
| 玩家、基础敌人 | BoxCollider2D，0.6 × 1.2 | `Visual` |
| 战斗/维护船员 | BoxCollider2D，0.45 × 0.7 | `Visual/Body`、`Weapon` 或 `Repair Tool` |
| 甲壳射手变体 | BoxCollider2D，0.84 × 1.68 | `Visual`；显示局部缩放 1.4，根缩放保持 1 |
| 石块/障碍物 | BoxCollider2D，1.2 × 0.94 | `Visual`，平面缩放 1.25；碰撞尺寸覆盖最大外观 |
| 子弹 | CircleCollider2D，半径 0.09 | `Visual`，当前局部缩放 0.18 |
| 定时炸弹 | BoxCollider2D，1.22 × 1.17 | `Visual`，平面缩放 1.45；引信条位于 Y=0.68 |
| 维修点 | 无碰撞器；默认甲板网格 1 单位 | `Visual`，平面缩放 3.08；原位置容得下完整破损时保留该位置 |

## 预制体入口

资源位于 `Assets/Ferryman/Prefabs`。15 个预制体的根节点缩放均为 1，显示与物理尺寸独立。Vessel 嵌套原 Boat 与 Player，素材替换保留现有 GUID 和序列化引用；main 与五个保留场景复用 `Environment/SeaEnvironment.prefab`。

| 预制体 | 接入位置 | 用法 |
| --- | --- | --- |
| `Player.prefab` | `Visual` | 替换 Sprite，或在此下放置完整显示预制体及 Animator；Character 组件的 `Visual` 字段指向需要占位调色的 SpriteRenderer |
| `Enemy.prefab`、`Enemies/*` | `Visual` | 基础敌人与四个变体均固定在船外；变体有独立显示名、生命、攻击方式与目标倾向 |
| `Crew/CombatCrew.prefab` | `Visual/Body`、`Weapon`、`Muzzle` | 船员显示和发射口，Visual 根缩放 1，Body 子层控制尺寸 |
| `Crew/MaintenanceCrew.prefab` | `Visual/Body`、`Repair Tool` | 维护外观与工具；动画读取刚体速度和 IsRepairing |
| `Vessel.prefab` | 嵌套 `Boat` / `Player` | 整船由航程控制进出场；行为期间不动画化根节点 |
| 玩家、敌人 | `Visual/Muzzle` | 将挂点移至武器发射口；CombatCharacter 组件的 `Muzzle` 字段已绑定，随显示转向。未绑定时使用 `Muzzle Distance`，默认 0.75 |
| `Projectile.prefab` | `Visual` | 放置飞弹、弹道、TrailRenderer 或飞行粒子；`Visual Root` 沿实际飞行方向旋转，根节点的圆形碰撞器保持独立 |
| `Stone.prefab` | `Visual` | 替换障碍物显示时按最大可见外观核对根碰撞器尺寸和落地空间 |
| `Boat.prefab` | `Hull Visual`、`Wing Left`、`Wing Right` | 分别显示原画船体和左右翅膀；旧 `Deck Surface` 占位层已删除，船舱与箱子为图中装饰 |
| `Environment/SeaEnvironment.prefab` | `Sea Motion/Sea Surface -1..2`、`Sailing Wake` | 四块海图交替镜像循环，船浪使用三帧 Animator；显示独立于甲板碰撞 |
| `TimedBomb.prefab` | `Visual`、`Fuse Bar` | 替换炸弹与引信显示；`Fuse Fill` 指向横向填充挂点，倒计时缩放其 X 轴，清除/爆炸时随根移除 |
| `RepairPoint.prefab` | `Visual`、`Progress Bar` | 固定损坏外观；进度条放在点上方，`Progress Fill` 指向横向填充挂点，维修时缩放其 X 轴 |

`Aim Marker` 白色短线和 `Melee Outline` 橙色扇形是占位反馈。接入武器和挥击特效后，可在显示层停用短线，并清空玩家的 `Melee Outline` 引用。`Deck Boundary` 的 LineRenderer 已关闭，保留整个对象及闭合 EdgeCollider2D，不添加覆盖全甲板的实心碰撞。

## 物品与维修素材

新包的 9 张独立图片均为静态外观，编号代表不同样式，不作为帧动画。`111维修示意.png` 对齐船体原画后，示例破损的可见范围约 310×262 船图像素，换算后采用 3.08 倍显示比例。原图画布与透明度保持交付内容，Pivot 对齐可见物品中心；物品使用 1800 PPU，破损和维修条使用 1000 PPU。

| 原包资源 | 项目资源 | 显示入口 |
| --- | --- | --- |
| 炸弹-1、炸弹-2 | `Art/Items/BombDynamite.png`、`BombRound.png` | `TimedBomb/Visual`，两种炸弹外观 |
| 石块-1、石块-2、障碍物 | `Art/Items/StoneWide.png`、`StoneTall.png`、`DeckObstacle.png` | `Stone/Visual`，三种可清除障碍外观 |
| 维修处-1、维修处-2 | `Art/Repair/HoleWide.png`、`HoleSplinter.png` | `RepairPoint/Visual`，两种固定破损；旧 Crack 占位层已删除 |
| 维修进度条-外框、Fill | `Art/Repair/ProgressFrame.png`、`ProgressFill.png` | `RepairPoint/Progress Bar/Background` 与 `Fill/Visual` |

`DestructibleObstacle` 和 `RepairPoint` 的 Visual / Appearances 绑定静态样式，实例生成时选一次外观。破损贴图不随维修进度改变。SpriteRenderer 保持白色，根节点缩放和碰撞保持原规则；炸弹已移除占位显示的 45° 旋转。原引信条继续显示倒计时。

维修条保留 `Progress Bar → Fill → Visual`。Progress Bar 父层平面缩放 1.25，外框和填充一起放大，位于破损上方 Y=1.02。填充从内框左侧增长；100% 的不透明区域覆盖原画矩形 `(1090,920)–(2150,1082)`，父层缩放前的宽高基准为 `1.06 × 0.162`，略压入手绘黑边，Frame 绘制在 Fill 上方。Fill 的局部位置为 `(-0.385,-0.0125)`，素材宽高缩放 `1060/981`、`162/122` 放在其 Visual 子层，Fill 本身仅承载进度比例，避免运行时的 `(比例,1,1)` 覆盖素材高度。松键、中断和完成的显示规则沿用原实现。

## 动画与特效触发

以下 UnityEvent 可以在 Inspector 中绑定显示子物体的 `Animator.SetTrigger(string)` 或 `ParticleSystem.Play()`。事件不要求已有 Animator Controller；实际素材交付时再配置动画参数和过渡。

| Inspector 字段 | 所在组件 | 触发条件 |
| --- | --- | --- |
| `Shot Fired` | `CombatCharacter` | 子弹成功发射后；冷却、眩晕或缺少必需战斗引用时不触发 |
| `Melee Performed` | `PlayerCharacter` | 一次近战成功执行后，挥空也触发；冷却和眩晕期间不触发 |
| `Hit Received` | `Character` | 受击被接受后；友方开始眩晕，敌人扣血。免疫命中不重复触发 |
| `Stun Recovered` | `Character` | 友方的受击反馈结束后，默认 1 秒；重复命中不延长时间 |
| `Health Changed` | `BoatHealth` | 实际伤害或维修完成改变船只 HP 后，参数为当前 HP、最大 HP |
| `Damaged` | `BoatHealth` | 接受船只伤害后，参数为世界受击位置、实际扣除的 HP |
| `Destroyed` | `BoatHealth` | HP 归零；停止船上行动，航程进入失败并结算已获得奖励 |
| `Boarded` / `Disembarked` | `Passenger` | 乘客岗位/目的地初始化后，以及到达目的地下船时触发 |
| `Repair Started` / `Repair Stopped` | `RepairPoint` | 第一个维修者开始、最后一个离开（含中断、完成、船毁）；多人共用此点的状态 |
| `Repair Completed` | `RepairPoint` | 进度达标并完成一次 HP 结算后；同一点只触发一次 |

移动动画可以读取角色 Rigidbody2D 的 `velocity`，眩晕状态读取 `Character.IsStunned`，敌人 HP 读取 `Character.Health`。已有 `DamageSystem.DamageReceived` 和 `DamageSent` 仍只在伤害被接受后派发，参数依次为发起者、目标、伤害值。

玩家维修动画读取 `PlayerCharacter.IsRepairing`。维修点共享状态读取 `RepairPoint.Progress`、`RequiredProgress` 和 `IsBeingRepaired`；损坏模型不按进度变换。维修者暂时中断时只隐藏进度条，完成后移除整个点。首点优先使用实际受击位置，靠近舱体、货物或边线时改用能容下破损的格心。后续点按网格距离逐圈选格。事件道具生成和炸弹落地还检查角色及同场道具占用，踢飞后的视觉可直接穿过这些区域。

维护 NPC 动作读取 `MaintenanceCrewMember.IsRepairing` 与刚体速度，战斗 NPC 使用 CombatCharacter 的攻击事件。岗位与特色来自 `CrewDefinition`；肖像字段 `Portrait` 可单独绑定。乘客的 `Definition` 与 `DestinationStopIndex` 提供显示信息，岗位不会在航程中改变。

船只 HP、玩家状态与操作提示的旧面板已删除。正式表现读取 BoatHealth 或订阅 `Health Changed` / `Destroyed`；伤害、维修和乘客数据继续由现有业务组件提供。

正式素材自带颜色、材质或颜色动画时：

1. 将对应 SpriteRenderer 的 Color 调为素材所需颜色，通常为白色。
2. 关闭角色的 **Use Placeholder Feedback**，避免受击变灰、闪白覆盖动画颜色；受击和恢复事件仍会触发。
3. 关闭子弹的 **Use Faction Tint**，避免发射时覆盖素材颜色；显示朝向与碰撞仍正常工作。

## 对象销毁后的表现

| Inspector 字段 | 所在组件 | 行为 |
| --- | --- | --- |
| `Impact Effect Prefab` | `Projectile` | 命中敌对角色时在命中位置独立生成；目标眩晕免疫时仍生成。穿过边界、发起者、友军和石块时不生成 |
| `Death Effect Prefab` | `EnemyCharacter` | 生命归零时在敌人位置独立生成 |
| `Cleared Effect Prefab` | `DestructibleObstacle` | 石块或炸弹被清除时在障碍物位置独立生成；同一对象只生成一次 |
| `Explosion Effect Prefab` | `TimedBomb` | 引信爆炸时在炸弹位置独立生成；提前清除时不触发爆炸，不同时触发清除特效 |
| `Completed Effect Prefab` | `RepairPoint` | 维修完成时独立生成，随后损坏点与进度条销毁 |
| `Disembark Effect Prefab` | `Passenger` | 到站下船时独立生成，乘客行为与碰撞立即停用并销毁 |

这些字段默认留空。特效实例位于场景中，不挂在即将销毁的对象下，使用特效预制体自身的播放设置。玩家近战踢中石块或炸弹时，立即关闭碰撞并移除逻辑对象，引信条随炸弹根节点移除。原 `Visual` 从根节点脱离，保留当次外观，沿远离玩家的方向旋转飞出相机视口后销毁。常规清理和引信爆炸沿用原来的独立表现入口。敌人死亡动画使用独立死亡特效预制体。

特效资源必须自行结束并清理：简单粒子设置 `Looping` 关闭、`Play On Awake` 开启、`Stop Action = Destroy`；动画特效由其完成回调清理。飞行特效作为子弹的显示子物体，在子弹消耗或 5 秒寿命结束时随之清理。

## 场景、航程和 UI

完整游戏入口是 `Assets/Ferryman/Scenes/main.unity`。它只有一台相机，共享 `Environment/Sea Motion` 海面与 `Environment/Sailing Wake` 船浪。`Environment/Island Stops` 下的 Departure、Island、FinalIsland 分别包含 `Ground`、`Landmarks`、`Shore Waves` 和 `Dock`；航程只启用当前岛屿，海路关闭整个 Island Stops。出发、航行、停靠、结算和返家始终留在 main。

Departure、Sea、Island、FinalIsland 和 Prototype 保留为原区域演示与回归夹具，也使用共享海面预制体。旧岛屿场景仍采用 `Environment/Island and Dock` 层级。`Sea Motion/Sea Surface -1..2` 为海图，`Sailing Wake` 为船浪；显示图层与甲板碰撞独立。

交付素材位于 `Assets/Ferryman/Art/Scene`，共 15 张图片：

| 原包资源 | 项目资源 | 配置 |
| --- | --- | --- |
| 船/船体、小翅膀左、小翅膀右 | `Ship/Hull.png`、`WingLeft.png`、`WingRight.png` | 3840×2160、180 PPU，Unity Pivot `(1600/3840, 1010/2160)`；甲板中心对应原图左上坐标 `(1600,1150)` |
| 海/海面 | `Sea/Sea.png` | 9600 像素宽、180 PPU，四块复用图水平铺设，完整循环宽 106.6667 世界单位 |
| 海/船海浪/船海浪-1..3 | `Sea/ShipWake1..3.png` | 5720×3240、180 PPU，Pivot `(3205/5720, 1781/3240)`；按示意图中的船体画布偏移 `(1605,309)` 对齐 |
| 岛岸/出发点、途径点、终点、码头 | `Islands/Departure.png`、`Waypoint.png`、`Destination.png`、`Dock.png` | 同画布中心 Pivot、240 PPU，在三站替换 Landmarks 图层 |
| 岛岸/岛屿海浪/岛屿海浪-1..3 | `Islands/ShoreWave1..3.png` | 同画布中心 Pivot、240 PPU，三帧循环 |
| 岛岸/111岛岸示意 | `Islands/Ground.png` | 原包无独立地面；从示意图拆出公共地面，去除烘焙建筑、码头和岸浪，仅用同图地面克隆补齐遮挡区域；原图不修改 |

船浪与岸浪各使用 6 FPS 的三帧循环 Animator，这是交付包中的全部帧动画。main 的三站岸浪分别绑定同一岸浪 Controller；船体、翅膀、建筑和码头保持交付的静态图。船浪与船只共用世界原点；岛岸各图层共用局部位置 `(1.278,7.1)`，避免帧间抖动。单独替换岛屿地面时保持其与建筑、码头及岸浪的画布对齐。

`VoyageScenePresentation` 的 Sea Motion、Sailing Wake 和 Scroll Loop Width 决定滚动、水花启停及循环宽度。航行时滚动、事件时停止。main 另外绑定 Island Stops，按路线 `sceneName` 匹配岛屿子节点；重复海路和不同路线长度不依赖固定站点索引。区域切换在过场遮罩期间重置海面位置和镜头。船只进出场由 Controller 的左入/右出整体平移和 Fade 驱动；演出期间暂停角色刚体模拟，结束恢复行为。正式动画保持显示与实际甲板位置一致，避免额外动画化船根造成双重位移。

摄像机固定 X=2.306；main 岛屿区域 Y=3.3、Size=8.6，海路 Y=0、Size=6；旧区域场景保持对应取景，Prototype 的 Y=0、Size=7。岛屿取景同时容纳建筑、码头和完整船体。main 的 Island Camera Height / Island Camera Size 与 Camera Size 配置两种基础取景。Controller 读取 `VoyageScenePresentation.CameraSize` 并平滑切换遭遇尺寸；默认遭遇 Size=7.5，敌人位于船外 Y=6/6.5，避开船体与翅膀构图。正式大型敌人素材仍属后续制作。

`VoyageView` 为可停用的占位 IMGUI 显示。正式 UI 读取 `VoyageController` 的 Phase、Message、StopIndex、Passengers、Enemies、PendingReward、Progress、DeliveredPassengers 与 Fade；调用 StartVoyage、ResumeVoyage、AbandonVoyage、ReturnHome 或 RetrySettlement 对接现有流程。无需新增存档或伤害系统。

按截图删除六块航程 HUD：船只 HP/玩家状态/维修与炸弹数量、区域/事件、待结算收益/送达人数、乘客列表、操作提示、返航按钮；同时删除 Prototype 的 Boat Status 对象及专用脚本。面板原用内置白纹理与系统字体，没有专用美术或测试；共享 `White.png` 和 20 项玩法回归保留。

现有出发选择、恢复、结算、错误界面、淡入淡出和敌人 HP 标签仍由 `VoyageView` 绘制。途中岗位/目的地及 3 秒上船等待仍在业务数据中。错误存档保留永久数据，结算写入失败可重试。

main 的 `UI Layout` 是默认停用的空 Canvas，参考分辨率为 1280×720。挂点来自《UI界面文档》的版面与资源规格。正式素材交付后可从下表的场景节点开始绑定。

| 挂点 | 接入内容及数据 |
| --- | --- |
| `Title Screen` | 全屏背景、顶部标题、居中纵排按钮。启航、恢复和退出沿用 `VoyageController` 接口。 |
| `Route Map` | 中央 960×480，分别预留底图、连线和站点图标。路线读取 `Definition.stops`，启航后海路事件读取 `EventPlan`，当前位置读取 `StopIndex`。 |
| `In-Game` | 左上船只耐久条与相邻眩晕环。耐久读取 `Vessel.Boat.CurrentHealth` / `MaxHealth`，眩晕环读取 `Vessel.Player.StunRemainingRatio`，剩余比例从 1 降至 0。 |
| `Settlement` | 居中弹出框和内部道具区。已实现的整轮结算读取 `PendingReward`、`DeliveredPassengers` 与 `Phase`。 |
| `Dialogue` | 底部对话框，左侧人像与右侧文本区。船员资料已有 `CrewDefinition.portrait`。 |
| `Upgrade Options` | 中央卡牌区域。用于后续素材排版，升级规则与界面流程待定义。 |

## 素材替换约束

| 美术需求 | 对应玩法阶段与约束 |
| --- | --- |
| 海面、海浪、泡沫、船尾水花 | 显示独立于碰撞，滚动与水花随 Sailing 阶段启停 |
| 战斗岗位 NPC、维护岗位 NPC | 固定岗位，战斗不维修、维护不战斗；外观与工具用于识别岗位 |
| 敌人投放炸弹表现 | 炸弹采用抛物线占位飞行，飞行中无碰撞，落地后开始引信；显示缩放不改变落点 |
| 岛屿、码头、乘客及船只进出场 | main 内区域切换和自动上下客已接入，正式表现保留既有岗位/目的地和时间规则 |
| 敌人 HP、眩晕、乘客、奖励和结算 UI | 读取既有状态与奖励数据；后续按用户方案接入正式表现 |
| 大型敌人与特殊镜头 | 已提供可配置遭遇缩放，正式大型怪物素材不要求首批制作 |

## 交付与替换检查

每项素材附资源名称、用途、文件清单、朝向与枢轴说明、世界尺寸参考、动画拆分及所需材质。批量制作前共同确定美术风格、最终比例、维修网格、NPC 数量、动画方向、UI 字段和导入规格。

导入实际素材时按资源用途放入现有 `Assets/Ferryman/Art`，需要时再分子目录。提交素材、目录及预制体对应的 `.meta`，保留已有预制体 GUID。替换后确认显示方向、Muzzle 位置、动画不移动根节点、碰撞未随视觉缩放变化，以及独立特效能结束；涉及碰撞或移动基准时运行现有 PlayMode 关键回归，范围见 [策划覆盖与验证](PrototypeAudit.md)。
