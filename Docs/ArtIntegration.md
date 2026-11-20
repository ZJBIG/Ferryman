# 美术素材接入说明

本说明对照《美术需求文档.docx》，记录完整 V0.1 原型的素材入口。船体、玩家、两类岗位 NPC、敌人变体、物品、维修点、环境、岛屿、乘客和结算均已接入玩法，当前占位显示可独立替换。

## 当前技术基线

- Unity **2022.3.62f3c1**，现有 **2D URP**，XY 平面正俯视。文档中的“模型”没有定案为 3D 资源。
- 角色、武器和定向投射物以本地 **+X 为正前方**，围绕 Z 轴转向。资源朝向不同时，在显示层调整局部旋转。
- 角色根节点保留刚体、碰撞器和行为脚本；显示、武器、Animator 和角色特效放在 `Visual` 下。动画关闭 **Apply Root Motion**，不动画化角色根节点的位置、旋转或缩放。
- 当前贴图使用 **100 Pixels Per Unit**。正式素材的 PPU、枢轴、过滤方式、压缩和动画拆分需根据实际交付确认；已有占位贴图不代表最终美术风格。
- 以下是当前回归使用的物理基准，最终美术尺寸尚未定案。替换图片、调整显示缩放或动画时保持这些碰撞尺寸；需要修改物理基准时同步验证。

| 对象 | 当前物理基准 | 显示调整位置 |
| --- | --- | --- |
| 甲板 | 闭合边线，宽 12、高 3 | `Boat/Hull Visual`、`Boat/Deck Surface` |
| 玩家、基础敌人 | BoxCollider2D，0.6 × 1.2 | `Visual` |
| 战斗/维护船员 | BoxCollider2D，0.45 × 0.7 | `Visual/Body`、`Weapon` 或 `Repair Tool` |
| 甲壳射手变体 | BoxCollider2D，0.84 × 1.68 | `Visual`；显示局部缩放 1.4，根缩放保持 1 |
| 石块 | BoxCollider2D，0.75 × 0.75 | `Visual`，当前局部缩放 0.75 |
| 子弹 | CircleCollider2D，半径 0.09 | `Visual`，当前局部缩放 0.18 |
| 定时炸弹 | BoxCollider2D，0.5 × 0.5 | `Visual`，根缩放保持 1 |
| 维修点 | 无碰撞器；默认甲板网格 1 单位 | `Visual`；保持根节点受击位置与格索引 |

## 预制体入口

资源位于 `Assets/Ferryman/Prefabs`。14 个预制体的根节点缩放均为 1，显示与物理尺寸独立。Vessel 嵌套原 Boat 与 Player，素材替换保留现有 GUID 和序列化引用。

| 预制体 | 接入位置 | 用法 |
| --- | --- | --- |
| `Player.prefab` | `Visual` | 替换 Sprite，或在此下放置完整显示预制体及 Animator；Character 组件的 `Visual` 字段指向需要占位调色的 SpriteRenderer |
| `Enemy.prefab`、`Enemies/*` | `Visual` | 基础敌人与四个变体均固定在船外；变体有独立显示名、生命、攻击方式与目标倾向 |
| `Crew/CombatCrew.prefab` | `Visual/Body`、`Weapon`、`Muzzle` | 船员显示和发射口，Visual 根缩放 1，Body 子层控制尺寸 |
| `Crew/MaintenanceCrew.prefab` | `Visual/Body`、`Repair Tool` | 维护外观与工具；动画读取刚体速度和 IsRepairing |
| `Vessel.prefab` | 嵌套 `Boat` / `Player` | 整船由航程控制进出场；行为期间不动画化根节点 |
| 玩家、敌人 | `Visual/Muzzle` | 将挂点移至武器发射口；CombatCharacter 组件的 `Muzzle` 字段已绑定，随显示转向。未绑定时使用 `Muzzle Distance`，默认 0.75 |
| `Projectile.prefab` | `Visual` | 放置飞弹、弹道、TrailRenderer 或飞行粒子；`Visual Root` 沿实际飞行方向旋转，根节点的圆形碰撞器保持独立 |
| `Stone.prefab` | `Visual` | 替换障碍物显示；不改变根碰撞器的 Size |
| `Boat.prefab` | `Hull Visual`、`Deck Surface` | 分别替换船体和甲板显示；可添加船舷、装饰及水花子物体 |
| `TimedBomb.prefab` | `Visual`、`Fuse Bar` | 替换炸弹与引信显示；`Fuse Fill` 指向横向填充挂点，倒计时缩放其 X 轴，清除/爆炸时随根移除 |
| `RepairPoint.prefab` | `Visual`、`Progress Bar` | 固定损坏外观；进度条放在点上方，`Progress Fill` 指向横向填充挂点，维修时缩放其 X 轴 |

`Aim Marker` 白色短线和 `Melee Outline` 橙色扇形是占位反馈。接入武器和挥击特效后，可在显示层停用短线，并清空玩家的 `Melee Outline` 引用。红色边界来自 `Deck Boundary` 的 LineRenderer；隐藏红框时只关闭该渲染组件，保留整个 `Deck Boundary` 对象及 EdgeCollider2D。

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

玩家维修动画读取 `PlayerCharacter.IsRepairing`。维修点共享状态读取 `RepairPoint.Progress`、`RequiredProgress` 和 `IsBeingRepaired`；损坏模型不按进度变换。维修者暂时中断时只隐藏进度条，完成后移除整个点。首点显示在实际受击位置；同格后续点按网格距离逐圈随机选格并放在格心，网格占用不受视觉缩放影响。

维护 NPC 动作读取 `MaintenanceCrewMember.IsRepairing` 与刚体速度，战斗 NPC 使用 CombatCharacter 的攻击事件。岗位与特色来自 `CrewDefinition`；肖像字段 `Portrait` 可单独绑定。乘客的 `Definition` 与 `DestinationStopIndex` 提供显示信息，岗位不会在航程中改变。

场景中的 `Boat Status` 使用 `BoatStatusDisplay` 提供占位 HP、操作提示与状态。正式 UI 接入时停用该显示组件，读取 BoatHealth 或订阅 `Health Changed` / `Destroyed`；不需要改伤害和维修逻辑。

正式素材自带颜色、材质或颜色动画时：

1. 将对应 SpriteRenderer 的 Color 调为素材所需颜色，通常为白色。
2. 关闭角色的 **Use Placeholder Feedback**，避免受击变灰、闪白覆盖动画颜色；受击和恢复事件仍会触发。
3. 关闭子弹的 **Use Faction Tint**，避免发射时覆盖素材颜色；显示朝向与碰撞仍正常工作。

## 对象销毁后的表现

| Inspector 字段 | 所在组件 | 行为 |
| --- | --- | --- |
| `Impact Effect Prefab` | `Projectile` | 命中敌对角色时在命中位置独立生成；目标眩晕免疫时仍生成。穿过边界、发起者、友军和石块时不生成 |
| `Death Effect Prefab` | `EnemyCharacter` | 生命归零时在敌人位置独立生成 |
| `Cleared Effect Prefab` | `DestructibleObstacle` | 石块被清除时在障碍物位置独立生成；同一对象只生成一次 |
| `Explosion Effect Prefab` | `TimedBomb` | 引信爆炸时在炸弹位置独立生成；提前清除时不触发爆炸，不同时触发清除特效 |
| `Completed Effect Prefab` | `RepairPoint` | 维修完成时独立生成，随后损坏点与进度条销毁 |
| `Disembark Effect Prefab` | `Passenger` | 到站下船时独立生成，乘客行为与碰撞立即停用并销毁 |

这些字段默认留空。特效实例位于场景中，不挂在即将销毁的对象下，使用特效预制体自身的播放设置。敌人和障碍物仍立即失去碰撞并销毁；需要死亡动画时，将相应表现放在独立死亡特效预制体中。

特效资源必须自行结束并清理：简单粒子设置 `Looping` 关闭、`Play On Awake` 开启、`Stop Action = Destroy`；动画特效由其完成回调清理。飞行特效作为子弹的显示子物体，在子弹消耗或 5 秒寿命结束时随之清理。

## 场景、航程和 UI

Departure、Sea、Island、FinalIsland 使用相同环境层结构：`Environment/Sea Surface` 为基础海面，`Sea Motion/Wave` 为航行滚动层，`Sailing Wake/Foam` 为水花，岛屿场景提供 `Island and Dock/Beach`、`Land` 和 `Dock`。可以替换这些显示子层或加入环境显示预制体，不添加甲板实心碰撞。

`VoyageScenePresentation` 的 Sea Motion 和 Sailing Wake 引用决定滚动与水花启停。航行时滚动、事件时停止。船只进出场由 Controller 的左入/右出整体平移和 Fade 驱动；演出期间暂停角色刚体模拟，结束恢复行为。正式动画保持显示与实际甲板位置一致，避免额外动画化船根造成双重位移。

摄像机固定中心，Controller 自动控制正交缩放；`EncounterDefinition.Camera Size` 配置遭遇演出尺寸。大体型射手使用占位变体验证缩放，正式大型敌人素材仍属后续制作。

`VoyageView` 为可停用的占位 IMGUI 显示。正式 UI 读取 `VoyageController` 的 Phase、Message、StopIndex、Passengers、Enemies、PendingReward、Progress、DeliveredPassengers 与 Fade；调用 StartVoyage、ResumeVoyage、AbandonVoyage、ReturnHome 或 RetrySettlement 对接现有流程。无需新增存档或伤害系统。

已提供船 HP、玩家/NPC 眩晕和岗位状态、敌人 HP、炸弹数量、乘客目的地、待结算及永久奖励、恢复和结算界面。错误存档保留永久数据并显示停止状态，结算写入失败可重试。UI 布局和正式图片仍由美术交付确定。

## 素材替换约束

| 美术需求 | 对应玩法阶段与约束 |
| --- | --- |
| 海面、海浪、泡沫、船尾水花 | 显示独立于碰撞，滚动与水花随 Sailing 阶段启停 |
| 战斗岗位 NPC、维护岗位 NPC | 固定岗位，战斗不维修、维护不战斗；外观与工具用于识别岗位 |
| 敌人投放炸弹表现 | 炸弹采用抛物线占位飞行，飞行中无碰撞，落地后开始引信；显示缩放不改变落点 |
| 岛屿、码头、乘客及船只进出场 | 真实场景切换和自动上下客已接入，正式表现保留既有岗位/目的地和时间规则 |
| 敌人 HP、眩晕、乘客、奖励和结算 UI | 读取既有状态与奖励数据，停用占位视图后绑定正式 UI |
| 大型敌人与特殊镜头 | 已提供可配置遭遇缩放，正式大型怪物素材不要求首批制作 |

## 交付与替换检查

每项素材附资源名称、用途、文件清单、朝向与枢轴说明、世界尺寸参考、动画拆分及所需材质。批量制作前共同确定美术风格、最终比例、维修网格、NPC 数量、动画方向、UI 字段和导入规格。

导入实际素材时按资源用途放入现有 `Assets/Ferryman/Art`，需要时再分子目录。提交素材、目录及预制体对应的 `.meta`，保留已有预制体 GUID。替换后确认显示方向、Muzzle 位置、动画不移动根节点、碰撞未随视觉缩放变化，以及独立特效能结束；涉及碰撞或移动基准时运行 README 中的关键回归。
