# Ferryman · 摆渡人

Unity 2D 可玩原型：选择首位护送乘客，沿固定航线自动摆渡，处理海上战斗和甲板事件，协同船员维修，到站送客并结算奖励。

## 运行

1. 使用 **Unity 2022.3.62f3c1** 打开仓库根目录，等待导入完成。
2. 打开 **`Assets/Ferryman/Scenes/Departure.unity`**，Game 视图选择 **16:9**，点击 Play。
3. 出发岛显示四种乘客的固定岗位与数值。点击“护送启航”，首位乘客的目的地为最终岛屿。
4. 船只从左侧进入画面中心，沿 **出发岛 → 近海 → 途中岛 → 外海 → 最终岛** 航行，离场向右移动并播放过场。事件期间海面停止滚动。
5. 途中岛有 **75%** 概率出现顺路乘客；停靠等待 **3 秒** 后自动上船。到达目的地后下船，本轮奖励随之增加。
6. 成功或船毁失败均结算已获得的本轮奖励，返回出发岛后可开始新的满耐久航程。

| 操作 | 行为 |
| --- | --- |
| WASD / 方向键 | 甲板内移动，斜向等速，松键停止 |
| 鼠标 | 控制显示朝向，根碰撞箱保持固定 |
| 按住左键 | 无限子弹连射，无装弹 |
| 点击右键 | 一次扇形近战，清除石块和炸弹；两键同时输入时近战优先 |
| 按住 E | 自动维修范围内的点；移动、攻击、松键或眩晕立即中断，进度保留 |

蓝色人物为玩家，青色船员为战斗岗，黄色船员为维护岗。红色和紫色敌人分别优先攻击玩家与 NPC，橙色敌人投放定时炸弹。玩家和友军受击只眩晕 **1 秒**，不扣血、不死亡；期间免伤、停止移动与攻击，重复命中不延长眩晕。敌人扣血至零后停止攻击并销毁。

近海的固定战斗有两轮：先出现两名射击敌人，再出现投弹手和甲壳射手。必须清空当前轮才出现下一轮，全部击败后获得奖励。外海随机出现甲板落石或漂流炸弹。炸弹落地后倒计时 **3 秒**，可被近战提前清除；爆炸只损伤船只。

## 数值与资源配置

入口数据为 **`Assets/Ferryman/Resources/Voyage/DefaultVoyage.asset`**。在 Inspector 配置线性路线、场景、固定事件、随机遭遇池、各类事件次数、乘客池、上船概率、航行和过场时长。

| 配置 | 内容 |
| --- | --- |
| `CrewDefinition`，`Resources/Voyage/Crew` | 固定岗位、速度、攻击或维修属性、肖像、运输报酬 |
| `EncounterDefinition`，`Resources/Voyage/Events` | 逐轮敌人类型、数量、出生位置、奖励与镜头缩放 |
| `SeaEventDefinition` | 石块/炸弹预制体、数量、奖励 |
| `Prefabs/Enemies` | 玩家优先射手、NPC 优先射手、投弹手、大体型射手 |
| `Prefabs/Crew` | 战斗与维护岗位的显示、碰撞和行为 |
| `Prefabs/Vessel.prefab` | 嵌套原 Boat/Player，绑定船 HP、甲板、伤害系统 |

事件表在启航时按种子一次性生成。每段海路最多一个事件，固定事件必执行；随机事件只占剩余海路。配置的敌人/特殊事件总次数包含固定事件，固定数量超额时仍保留固定事件。特殊类型分别限制出现次数，容量不足时优先保留敌人遭遇，允许连续事件。

基础战斗参数保留：玩家攻击 **10**、射击间隔 **0.2 秒**；基础射手生命 **100**、攻击 **10**、间隔 **1.5 秒**；子弹速度 **8**、寿命 **5 秒**；近战半径 **1.5**、角度 **90°**、冷却 **0.5 秒**。变体数值单独配置。射击角色使用 `Projectile` 攻击和玩家/NPC 优先倾向；攻击船只的角色使用 `Bomb` 和 `ShipFirst`，并绑定炸弹预制体。

甲板按交付船原画调整为 **12 × 5** 的闭合 EdgeCollider2D，内部可走，边线显示已隐藏；玩家为动态刚体，碰撞箱 **0.6 × 1.2**、速度 **4**，船员碰撞箱 **0.45 × 0.7**。角色显示放在子物体，零摩擦材质支持沿边滑动。子弹穿过边界、发起者、友军和石块；命中敌对角色只结算一次，眩晕目标也消耗子弹。

船只默认 **1000 HP**，每累计 **100 实际伤害**生成一个维修点，余数保留。首点在受击位置，格已占用时逐圈随机搜索新的切比雪夫距离层，直到全甲板。默认维修网格为 **1 单位**。每点进度满 **100** 才一次性恢复 **100 HP**，船毁后不能维修复活。

玩家维修为 **50 HP/s**、切比雪夫范围 **1 格**、同时 **1 点**。近点优先，同距进度高优先，仍相同才随机；已有任务不被新点抢占，每个并行点获得完整速度。维护 NPC 全船寻路绕障，到位后以目标维修点为中心分配任务，当前工作全部完成后再换目标。玩家与多个维护 NPC 可以协同维修同一点，速度叠加、回血只结算一次。战斗 NPC 自动瞄准最近的敌人，始终不维修。

**`Prototype.unity` 保留为独立战斗与维修演示及回归夹具**，包含两名固定敌人、两块石块和两枚炸弹；完整航程入口为 Departure。继续使用旧 Input Manager 和现有 2D URP。

## 存档与首版范围

永久奖励与检查点位于 `Application.persistentDataPath/Voyage`。启航时和每个区域抵达、上下客完成后自动保存检查点，包含种子、区域、玩家/乘客位置、目的地、船 HP、维修点进度与本轮奖励。

主动放弃、正常关闭游戏会丢弃本轮；强退或崩溃后，在出发界面选择“恢复航程”。恢复到最近区域检查点，重新执行该海路的事件。结算写入永久奖励时使用航程 ID 去重，防止结算写入后崩溃造成重复支付。损坏存档会显示错误，保留已有永久数据。

策划第 10 章明确首版暂不加入商店和永久成长；特殊岗位能力尚未定义。金币与特殊升级道具已经获取、显示和保存，成长消费待后续定义。逐项覆盖见 [策划核查与验证](Docs/PrototypeAudit.md)。

## 美术接入

`场景.zip` 的船体、左右翅膀、海面、船浪、三站建筑、码头和岸浪已接入五个场景，资源位于 `Assets/Ferryman/Art/Scene`。船浪和岸浪各播放三帧循环，海面与船浪随航行启停；海面使用共享 `Prefabs/Environment/SeaEnvironment.prefab`。原包缺独立岛屿地面，`Ground.png` 从交付示意图拆层并用同图地面补齐遮挡区域。

角色、投射物和物品通过 `Visual` 替换显示；船体挂在 `Hull Visual`，翅膀挂在 `Wing Left` / `Wing Right`。Muzzle 随显示转向，根节点保留行为和碰撞。Inspector 提供攻击、受击、恢复、维修、上下船和船毁事件；独立命中、死亡、清障、爆炸、维修完成和下船特效预制体自行清理。

按用户截图删除六块航程 HUD（船只/玩家状态、区域/事件、待结算收益、乘客列表、操作提示和返航按钮），同时删除独立原型的 `Boat Status` 显示及其脚本。原有出发选择、恢复、结算、错误界面和敌人 HP 标签保留；后续 UI 由用户定义。正式显示可读取 `VoyageController`、`BoatHealth`、`Passenger` 和角色状态。详细挂点、尺寸和原图对应关系见 [美术素材接入说明](Docs/ArtIntegration.md)。

## 验证

保留 **20 项关键回归**：移动 3、战斗 3、船损维修 3、NPC 岗位 3、事件调度 3、存档 2、完整航程 3。简单字段、属性和输入读取不另写测试。

Unity 菜单 `Window → General → Test Runner`，切换 **PlayMode**，点击 **Run All**。移动测试隔离演示敌人和障碍物；战斗、维修、岗位和航程使用真实 Unity 帧。存档与航程测试使用独立临时目录，避免写入玩家存档。

运行前关闭使用此项目的 Unity Editor：

```powershell
$unityEditor = 'D:\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
$projectDirectory = (Get-Location).Path
New-Item -ItemType Directory -Path .local -Force | Out-Null
& $unityEditor -batchmode -nographics -projectPath $projectDirectory -runTests -testPlatform PlayMode -testResults "$projectDirectory/.local/playmode-results.xml" -logFile "$projectDirectory/.local/playmode.log"
```

测试自动退出，不传 `-quit`。单独验证默认资源与引用时使用 `-executeMethod Ferryman.Editor.PrototypeValidation.Verify -quit`；验证并构建 Windows 时使用 `-executeMethod Ferryman.Editor.PrototypeValidation.VerifyAndBuild -quit`，产物在 `.local/build-prototype/Ferryman.exe`。

本机内存紧张时，命令可追加 `--burst-disable-compilation -job-worker-count 1` 并限制该次进程并发；业务代码未使用 Burst，不修改项目 Burst 设置。若 Windows 构建报告 Editor/Player 布局不同，先检查 Editor `Library/BuildPlayerData/Editor/TypeDb-All.json` 是否陈旧，再备份并移除该单文件重建；无需删除整个 Library。

AI 负责编译、引用、关键回归和渲染检查；实际键鼠手感由用户体验确认。

### 脚本编辑器

双击脚本时，Unity 使用 External Tools 中选择的 IDE 打开整个解决方案。本项目使用 Visual Studio Editor **2.0.29**，支持 Visual Studio 2026 的 SDK 风格工程；`.vsconfig` 声明了 **使用 Unity 的游戏开发** 工作负载。

项目打开或脚本重新编译后，失效的 IDE 路径会通过 Unity 安装检测恢复同类编辑器路径，缺少解决方案时自动生成。需要手动重建时执行 `Tools → Ferryman → Repair Script Editor`。有效选择保持不变，不硬编码 IDE 版本或安装目录。

## 仓库约定

提交 Assets 及全部 `.meta`、Packages、ProjectSettings 和项目文档。Library、日志、构建输出、IDE 缓存、自动工程文件、本机设置和 Unity Version Control 元数据由 `.gitignore` 排除。脚本使用 `Ferryman` 命名空间。
