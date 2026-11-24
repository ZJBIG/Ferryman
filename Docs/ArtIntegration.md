# 美术素材接入说明

本说明对照《美术需求文档.docx》，记录完整 V0.1 原型的素材入口。`场景.zip` 的船只、海面、船浪、岛岸建筑、码头与岸浪，`物品与维修.zip` 的炸弹、石块、障碍物、破损和维修条，以及 `特效.zip` 的战斗与维修提示均已接入。人物、武器及未交付特效可通过现有挂点替换显示。正式 UI 素材与界面入口见下文。

## 当前技术基线

- Unity **2022.3.62f3c1**，现有 **2D URP**，XY 平面正俯视。文档中的“模型”没有定案为 3D 资源。
- 角色、武器和定向投射物以本地 **+X 为正前方**，围绕 Z 轴转向。资源朝向不同时，在显示层调整局部旋转。
- 角色根节点保留刚体、碰撞器和行为脚本；显示、武器、Animator 和角色特效放在 `Visual` 下。动画关闭 **Apply Root Motion**，不动画化角色根节点的位置、旋转或缩放。
- 已交付场景素材使用 **180 PPU**（船、翅膀、船浪、海面）和 **240 PPU**（岛岸）；角色等占位贴图仍为 **100 PPU**。场景图使用 Bilinear、完整矩形 Sprite、无 mipmap，保留透明度；三帧动画共用画布与 Pivot，不逐帧裁边。
- 甲板边线沿船头内栏杆闭合，左侧上、下通行区和舱体、烟囱、箱子、桶的局部碰撞箱保留。每格 1 单位，原 12×5 网格在船头侧扩展 3 列，旧列坐标和存档索引保持原位。维修、寻路、登船和物品落点按完整碰撞范围检查甲板内缘。

| 对象 | 当前物理基准 | 显示调整位置 |
| --- | --- | --- |
| 甲板 | 闭合多段边线，隐藏边线显示；`Deck Boundary` 下另有舱体和货物碰撞箱 | `Boat/Hull Visual`、`Boat/Wing Left`、`Boat/Wing Right` |
| 玩家 | BoxCollider2D，0.6 × 1.2 | `Visual` |
| 战斗/维护船员 | BoxCollider2D，0.45 × 0.7 | `Visual/Body`、`Weapon` 或 `Repair Tool` |
| 敌人 | 根节点 PolygonCollider2D 对齐各自身体轮廓，排除水花和手持炸弹 | `Sea Monster`；枪口独立转向 |
| 石块/障碍物 | BoxCollider2D，1.2 × 0.94 | `Visual`，平面缩放 1.25；碰撞尺寸覆盖最大外观 |
| 子弹 | CircleCollider2D，半径 0.09 | `Flight Direction/Visual`，所有射手统一使用长轨迹外观 |
| 定时炸弹 | BoxCollider2D，1.22 × 1.17 | `Visual`，平面缩放 1.45；引信条位于 Y=0.68 |
| 维修点 | 无碰撞器；默认甲板网格 1 单位 | `Visual`，平面缩放 1.45；最大可见范围约 0.92 × 0.72，原位置容得下完整破损时保留该位置 |

## 预制体入口

资源位于 `Assets/Ferryman/Prefabs`。预制体的根节点缩放均为 1，显示与物理尺寸独立。Vessel 嵌套原 Boat 与 Player，素材替换保留现有 GUID 和序列化引用；main 与五个保留场景复用 `Environment/SeaEnvironment.prefab`。

| 预制体 | 接入位置 | 用法 |
| --- | --- | --- |
| `Player.prefab` | `Visual` | 替换 Sprite，或在此下放置完整显示预制体及 Animator；Character 组件的 `Visual` 字段指向需要占位调色的 SpriteRenderer |
| `Enemy.prefab`、`Enemies/*` | `Sea Monster` | 怪物保留原画朝向，独立的 `Visual/Muzzle` 瞄准目标。替换素材时同步调整根碰撞轮廓和投弹准备状态的钳尖轮廓。身体和持弹贴图需开启 Read/Write，受击图标从当前 Sprite 裁剪区的可见像素选取位置 |
| `Crew/CombatCrew.prefab` | `Visual/Body`、`Weapon`、`Muzzle` | 船员显示和发射口，Visual 根缩放 1，Body 子层控制尺寸 |
| `Crew/MaintenanceCrew.prefab` | `Visual/Body`、`Repair Tool` | 维护外观与工具；动画读取刚体速度和 IsRepairing |
| `Vessel.prefab` | 嵌套 `Boat` / `Player` | 整船由航程控制进出场；行为期间不动画化根节点 |
| 玩家、敌人 | `Visual/Muzzle` | 将挂点移至武器发射口；CombatCharacter 组件的 `Muzzle` 字段已绑定，随显示转向。未绑定时使用 `Muzzle Distance`，默认 0.75 |
| `Projectile.prefab` | `Flight Direction/Visual` | `Flight Direction` 沿飞行方向旋转，原画在 Visual 层转为本地 +X 朝前；根节点的圆形碰撞器保持独立 |
| `Stone.prefab` | `Visual` | 替换障碍物显示时按最大可见外观核对根碰撞器尺寸和落地空间 |
| `Boat.prefab` | `Hull Visual`、`Wing Left`、`Wing Right` | 分别显示原画船体和左右翅膀；旧 `Deck Surface` 占位层已删除，船舱与箱子为图中装饰 |
| `Environment/SeaEnvironment.prefab` | `Sea Motion/Sea Surface -1..2`、`Sailing Wake` | 原海图交替镜像循环，上方镜像一排覆盖海战取景；船浪使用三帧 Animator |
| `TimedBomb.prefab` | `Visual`、`Fuse Bar` | 替换炸弹与引信显示；`Fuse Fill` 指向横向填充挂点，倒计时缩放其 X 轴，清除/爆炸时随根移除 |
| `RepairPoint.prefab` | `Visual`、`Progress Bar` | 固定损坏外观；进度条放在点上方，`Progress Fill` 指向横向填充挂点，维修时缩放其 X 轴 |

`Aim Marker` 白色短线持续显示瞄准方向。玩家自身近战挥击图和枪口额外特效已取消，`Melee Outline` 保持关闭；只显示目标接触位置的命中反馈，敌人沿用原红色受击。Muzzle 仍决定子弹出生位置，攻击事件挂点保留。`Deck Boundary` 的 LineRenderer 已关闭，保留整个对象及闭合 EdgeCollider2D。

## 物品与维修素材

新包的 9 张独立图片均为静态外观，编号代表不同样式，不作为帧动画。破损采用 1.45 倍显示比例，最大可见范围约 0.92 × 0.72 世界单位，与落地炸弹约 0.99 单位的最长边匹配。原图画布与透明度保持交付内容，Pivot 对齐可见物品中心；物品使用 1800 PPU，破损和维修条使用 1000 PPU。

| 原包资源 | 项目资源 | 显示入口 |
| --- | --- | --- |
| 炸弹-1、炸弹-2 | `Art/Items/BombDynamite.png`、`BombRound.png` | `TimedBomb/Visual`，两种炸弹外观 |
| 石块-1、石块-2、障碍物 | `Art/Items/StoneWide.png`、`StoneTall.png`、`DeckObstacle.png` | `Stone/Visual`，三种可清除障碍外观 |
| 维修处-1、维修处-2 | `Art/Repair/HoleWide.png`、`HoleSplinter.png` | `RepairPoint/Visual`，两种固定破损；旧 Crack 占位层已删除 |
| 维修进度条-外框、Fill | `Art/Repair/ProgressFrame.png`、`ProgressFill.png` | `RepairPoint/Progress Bar/Background` 与 `Fill/Visual` |

`DestructibleObstacle` 和 `RepairPoint` 的 Visual / Appearances 绑定静态样式，实例生成时选一次外观。破损贴图不随维修进度改变。SpriteRenderer 保持白色，根节点缩放和碰撞保持原规则；炸弹已移除占位显示的 45° 旋转。原引信条继续显示倒计时。

维修条保留 `Progress Bar → Fill → Visual`。Progress Bar 父层平面缩放 1.25，外框和填充一起放大，位于破损上方 Y=1.02。填充从内框左侧增长；100% 的不透明区域覆盖原画矩形 `(1090,920)–(2150,1082)`，父层缩放前的宽高基准为 `1.06 × 0.162`，略压入手绘黑边，Frame 绘制在 Fill 上方。Fill 的局部位置为 `(-0.385,-0.0125)`，素材宽高缩放 `1060/981`、`162/122` 放在其 Visual 子层，Fill 本身仅承载进度比例，避免运行时的 `(比例,1,1)` 覆盖素材高度。松键、中断和完成的显示规则沿用原实现。

## 动画与特效触发

特效包的编号图每次触发随机选一张，实例存续期间保持所选外观。新增贴图位于 `Art/Effects`，一次性效果位于 `Prefabs/Effects`。受击、眩晕和炸弹警告复用 `Art/UI/Combat` 中已经接入的同款图片。

| 素材 | 显示时机 |
| --- | --- |
| 攻击特效 1–3 | 战斗船员和射击敌人成功开火时，在枪口沿瞄准方向显示 0.10 秒；玩家枪口额外特效已取消 |
| 子弹轨迹 | 所有射手固定使用 `ProjectileTrailLong`，沿飞行方向显示，保留阵营颜色；短轨迹素材保留供美术替换 |
| 命中反馈 1–2 | 玩家飞踢命中非敌方实体（人物、石块、炸弹、船上实心物体），以及敌方子弹命中玩家或友军时，在接触位置显示 0.15 秒；敌人只使用原有红色受击反馈 |
| 近战挥击 1–2 | 素材保留；玩家自身挥击显示已取消，挥空不生成攻击特效，近战执行事件仍触发 |
| 受击 | 怪物实际受伤后，沿用航程界面在其身体可见像素上的红色提示；子弹和飞踢不再叠加黄色命中特效 |
| 眩晕 | 玩家和友军眩晕时，显示在现有眩晕进度环内 |
| 敌人死亡 | 敌人被击败后，在死亡位置独立显示 0.40 秒 |
| 炸弹 | 落地后引信计时期间，显示在倒计时条旁 |
| 炸弹被摧毁 | 近战清除炸弹时，在原落点独立显示烟雾 0.30 秒 |
| 维修表现 | 在有人维修时显示在维修点进度条右侧，最后一位维修者离开或维修完成时隐藏 |

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

旧船只 HP、玩家状态与操作提示面板已删除；正式耐久、眩晕和敌人 HP 使用交付素材，读取 BoatHealth/Character 状态。伤害、维修和乘客数据继续由现有业务组件提供。

正式素材自带颜色、材质或颜色动画时：

1. 将对应 SpriteRenderer 的 Color 调为素材所需颜色，通常为白色。
2. 关闭角色的 **Use Placeholder Feedback**，避免受击变灰、闪白覆盖动画颜色；受击和恢复事件仍会触发。
3. 关闭子弹的 **Use Faction Tint**，避免发射时覆盖素材颜色；显示朝向与碰撞仍正常工作。

## 对象销毁后的表现

| Inspector 字段 | 所在组件 | 行为 |
| --- | --- | --- |
| `Impact Effect Prefab` | `Projectile` | 敌方子弹命中玩家或友军时在命中位置独立生成；目标眩晕免疫时仍生成。敌人受伤使用原有红色反馈；穿过边界、发起者、同阵营角色和石块时不生成 |
| `Melee Impact Effect Prefab` | `PlayerCharacter` | 飞踢命中非敌方实体时在接触位置独立生成，同一实体只触发一次；敌人使用原有红色受击反馈 |
| `Death Effect Prefab` | `EnemyCharacter` | 生命归零时在敌人位置独立生成 |
| `Cleared Effect Prefab` | `DestructibleObstacle` | 石块或炸弹被清除时在障碍物位置独立生成；同一对象只生成一次 |
| `Explosion Effect Prefab` | `TimedBomb` | 引信爆炸时在炸弹位置独立生成；提前清除时不触发爆炸，不同时触发清除特效 |
| `Completed Effect Prefab` | `RepairPoint` | 维修完成时独立生成，随后损坏点与进度条销毁 |
| `Disembark Effect Prefab` | `Passenger` | 到站下船时独立生成，乘客行为与碰撞立即停用并销毁 |

命中、炸弹清除和敌人死亡使用独立的特效预制体，由 `SpriteEffect` 到时销毁。玩家近战踢中石块或炸弹时，立即关闭碰撞并移除逻辑对象，引信条随炸弹根节点移除。原 `Visual` 从根节点脱离，保留当次外观，沿远离玩家的方向旋转飞出相机视口后销毁。定时引爆的独立特效入口等待对应的爆炸素材。

特效资源必须自行结束并清理：简单粒子设置 `Looping` 关闭、`Play On Awake` 开启、`Stop Action = Destroy`；动画特效由其完成回调清理。飞行特效作为子弹的显示子物体，在子弹消耗或 5 秒寿命结束时随之清理。

## 场景、航程和 UI

完整游戏入口是 `Assets/Ferryman/Scenes/main.unity`，默认分支海图含 20 个节点，一轮经过 8 座岛、7 段海路。它只有一台相机，共享 `Environment/Sea Motion` 海面与 `Environment/Sailing Wake` 船浪。`Environment/Island Stops` 下的 Departure、Island、FinalIsland 分别包含 `Ground`、`Landmarks`、`Shore Waves` 和 `Dock`，三组显示复用于所有停靠层；航程只启用当前岛屿，海路关闭整个 Island Stops。出发、航行、停靠、结算和返家始终留在 main，旧线性配置和检查点也继续支持。

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

`VoyageScenePresentation` 的 Sea Motion、Sailing Wake 和 Scroll Loop Width 决定滚动、水花启停及循环宽度。每段海路事件前后各航行休整 5 秒，合计 10 秒；战斗、炸弹和落石期间游戏时间正常推进，海面继续滚动、船浪照常播放，角色保持行动。main 另外绑定 Island Stops，按路线 `sceneName` 匹配岛屿子节点；重复海路和不同路线长度不依赖固定站点索引。区域切换在过场遮罩期间重置海面位置和镜头。船只进出场由 Controller 的左入/右出整体平移和 Fade 驱动；演出期间暂停角色刚体模拟，结束恢复行为。正式动画保持显示与实际甲板位置一致，避免额外动画化船根造成双重位移。

摄像机固定 X=2.306。main 岛屿区域 Y=3.3、Size=8.6，普通航行 Y=0、Size=6，两种战斗遭遇使用 `EncounterDefinition` 的 Y=3.5、Size=9.5。镜头平滑移到战斗取景后才生成首波，战斗结束恢复航行镜头。以 16:9 取景，船顶位于距画面底部约 59% 处，海怪位于上方 Y=8.5。投弹伏击的两个出生点间距为 4.4 单位。

半潜海怪 PNG 位于 `Assets/Ferryman/Art/Enemies`。Sprite 导入矩形去除透明留白，原 PNG 保留完整画布。礁虾和提灯鮟鱇用于两类追击射手，岩甲龟用于大型射手，木桶寄居蟹用于投弹手。`Sea Monster` 独立于瞄准节点，替换图时保持本地旋转为零。

寄居蟹在蓄力时将 `Sea Monster` 切换为完整的持物图，出手时切回待机图。`Bomb Poses` 每项绑定一张持物图、对应的飞行物贴图和钳口出手点。持物图的 PPU 与 Pivot 按桶壳和水线与待机图配准。两种投掷物离手后的最长可见边约 0.69 单位，与持物图接近，着陆后显示原始炸弹图标与引信条。甲板落石由独立航程事件生成。

海怪纹理使用 Kaiser mipmap、Trilinear 和 -0.4 mip bias，保留海战镜头缩小后的轮廓与线条，导入保持无压缩。

`UI.zip` 的正式素材位于 `Assets/Ferryman/Art/UI`。五个航程场景的 `Voyage Bootstrap` 共用 `VoyageUI.asset`，由 `VoyageView` 按 1280×720 参考坐标绘制。替换贴图后在 main 运行检查标题、按钮实际点击区域、进度填充和窗口缩放。标题和对话使用压缩包里的布局示意，运行时使用拆分的标题、船桨、文字框和人像框素材。

| 界面 | 版面与数据 |
| --- | --- |
| 开始 | 出发岛和码头作为背景，左侧摆渡船，右侧三个标题字和纵排船桨按钮。存在检查点时可继续游戏，新游戏先确认是否丢弃检查点。 |
| 航线 | 羊皮纸上显示出发、六层各三节点和终点，只高亮相连下一层；未探索连接为曲虚线，已探索为曲实线。预览与正式航程使用同一种子；保留旧线性模式显示。 |
| 局内 | 左上显示船只耐久，灰色和橙色分别填充手绘槽内的已损失量和剩余量。主角和两类船员各自显示眩晕倒计时环，靠近屏幕右缘时切到左侧。敌人血槽贴近身体下缘，缺血处透出海面。受击图标在当前姿势的可见像素上显示。 |
| 结算 | 成功与船毁共用居中面板，上方显示送达人数和本轮金币，内框显示道具与累计奖励。返回按钮沿用原有返航流程。 |
| 对话 | 途中乘客上船前，使用底部羊皮纸与左置头像框显示姓名、目的地和岗位。暂无立绘的角色使用岗位字。 |
| 岛屿活动 | 停靠时在羊皮纸内显示战利品、补给、船坞、集市、接驳、宝藏和遗迹选择，以及船 HP、本轮金币/道具；乘客上船对话先完成，之后显示活动。时间与角色移动继续，鼠标战斗输入在岛屿选择期间关闭。 |
| 升级与永久整备 | 免费奖励使用三张卡，最多三个不同未满级选项；出发岛显示六类永久升级、等级和价格。集市消费本轮报酬，永久整备消费已结算钱包，局内与永久分别最多五级。 |

main 的 `UI Layout` 保持停用，编辑模式启用可查看 `Upgrade Options/Cards` 的静态布局。实际分支地图、活动与升级界面由 `VoyageExpeditionUI` 绘制，复用现有羊皮纸、岛屿、战斗/事件和升级素材。六类升级为火力、连射、船体强化、快速维修、并行维修、维修范围；范围暂复用并行维修图标，规则见 [ExpeditionDesign.md](ExpeditionDesign.md)。替换升级卡须保留名称、图标、描述与点击按钮的布局空间。耐久、敌人血量和眩晕素材已去除透明留白，替换其框和填充时保留原始画布相对偏移；眩晕填充纹理保持 Read/Write Enabled。


## 素材替换约束

| 美术需求 | 对应玩法阶段与约束 |
| --- | --- |
| 海面、海浪、泡沫、船尾水花 | 显示独立于碰撞，海路 Sailing 与 Event 阶段持续滚动、播放，不在战斗、炸弹或落石事件中暂停 |
| 战斗岗位 NPC、维护岗位 NPC | 固定岗位，战斗不维修、维护不战斗；外观与工具用于识别岗位 |
| 道具入场 | 特殊事件道具从相机上方落下，敌人从身边抛出炸弹。两种入场方式都在落地前关闭碰撞，触地前复查落点，炸弹触地后开始引信 |
| 岛屿、码头、乘客及船只进出场 | main 内区域切换和自动上下客已接入，正式表现保留既有岗位/目的地和时间规则 |
| 敌人 HP、眩晕、乘客、奖励和结算 UI | 已接入正式素材，读取既有战斗状态与航程奖励 |
| 大型敌人与特殊镜头 | 岩甲龟使用单独的美术规格，战斗与航行自动切换镜头位置及尺寸 |

## 交付与替换检查

每项素材附资源名称、用途、文件清单、朝向与枢轴说明、世界尺寸参考、动画拆分及所需材质。批量制作前共同确定美术风格、最终比例、维修网格、NPC 数量、动画方向、UI 字段和导入规格。

导入实际素材时按资源用途放入现有 `Assets/Ferryman/Art`，需要时再分子目录。提交素材、目录及预制体对应的 `.meta`，保留已有预制体 GUID。替换后确认显示方向、Muzzle 位置、动画不移动根节点、碰撞未随视觉缩放变化，以及独立特效能结束；涉及碰撞或移动基准时运行现有 PlayMode 关键回归，范围见 [策划覆盖与验证](PrototypeAudit.md)。
