# Ferryman 开发交接

更新日期：2026-11-22。实现基线：`main` 分支提交 `6ef57e148dc0c7c3ba89e545eda530f17cc6585d`（物品与维修素材接入、玩家说明更新）。仓库：<https://github.com/ZJBIG/Ferryman>。

## 从哪里开始

使用 Unity **2022.3.62f3c1** 打开项目，运行 [main 场景](../Assets/Ferryman/Scenes/main.unity)。完整航程在同一场景内完成出发、海路、途中停靠、终点、结算和返家；main 是 Build Settings 首项。其余五个 Ferryman 场景保留为演示或回归夹具。

接手时先读 [项目约定](../AGENTS.md)。功能细节与美术规格继续维护已有文档，本文件只提供交接入口：

| 内容 | 位置 |
| --- | --- |
| 策划覆盖、用户确认规则、验证范围 | [PrototypeAudit.md](PrototypeAudit.md) |
| 素材挂点、尺寸、动画和维修条几何 | [ArtIntegration.md](ArtIntegration.md) |
| 默认航程、事件与船员配置 | [Resources/Voyage](../Assets/Ferryman/Resources/Voyage)；入口 `DefaultVoyage.asset` |
| 行为与显示预制体 | [Prefabs](../Assets/Ferryman/Prefabs) |
| 20 项正式回归 | [Tests/PlayMode](../Assets/Ferryman/Tests/PlayMode) |
| 资源检查与 Windows 构建入口 | [PrototypeValidation.cs](../Assets/Ferryman/Editor/PrototypeValidation.cs) |
| 玩家玩法与操作说明 | [README.md](../README.md) |

## 当前已完成

V0.1 核心循环已连通：鼠标瞄准、左键连射、右键近战清障、敌人死亡、友方受击眩晕、投弹、船体损伤与维修、固定战斗/维护岗位、自动线性航程、逐波事件、上下客、成功与船毁结算、检查点恢复。

场景包已接入船体、翅膀、海面、三站岛岸和水花；交付的船浪、岸浪三帧动画已播放，其他静态图保持静态。最新物品包的 9 张静态图已接入炸弹、石块、障碍、甲板破损及维修条，编号作为外观样式，生成时选择一次。

维修条已核对 0%、50%、100% 和完成后移除；满格填充覆盖整个手绘内框。`Progress Bar/Fill` 只缩放进度，素材基准宽高放在 `Fill/Visual`，Frame 绘制在填充之上。具体位置和缩放比例见美术说明。

## 接续开发必须保留的规则

- 保持现有 2D URP、旧 Input Manager 和 `Ferryman` 命名空间。显示与物理解耦：动画、朝向及视觉缩放放在显示子物体，根节点保留行为和碰撞。
- 甲板为 12×5，边界使用闭合 EdgeCollider2D；不能改成覆盖甲板的实心碰撞箱。Update 读取输入，FixedUpdate 写刚体速度；进出场演出由 Vessel 整体移动并暂停、恢复角色刚体模拟。
- 玩家与友军受击只眩晕，默认 1 秒；停止动作、免疫伤害，重复命中不延长。子弹穿过边界、自身与友军，命中敌对角色只结算一次，命中眩晕目标仍消耗。
- 维修按住 E，1 单位网格、切比雪夫范围 1 格、同时 1 点、50 HP/s。移动或攻击意图、眩晕、松键和禁用立即中断，进度保存在维修点。战斗船员不维修，维护船员不战斗；船毁后不能通过维修复活。
- 主动放弃和正常关闭丢弃本轮；强退或崩溃恢复最近区域检查点，海路事件从该区域重新开始。成功与船毁均结算，航程 ID 防止重复支付。测试使用临时存档目录。
- main 的岛屿显示按路线 `sceneName` 映射，不能绑定固定站点索引；保留对重复海路和不同路线长度的支持。
- 六块航程 HUD 和 Prototype 的 Boat Status 专用脚本已删除，不恢复其面板、素材、脚本或测试。新 UI 由用户定义；现有出发、恢复、结算、错误界面及敌人 HP 保留。
- README 与 GitHub 简介只写玩家玩法和操作，不透露技术细节，也不链接开发说明。提交 Unity 资源须带 `.meta`；不提交缓存、日志、构建、凭据或 `.plastic`。简单字段、属性、输入读取和直白逻辑不新增测试。

## 最近验证与复现入口

以下为 **2026-11-21 素材接入后的验证结果**，本次交接文档整理没有重新运行 Unity：

| 范围 | 结果与本机证据 |
| --- | --- |
| 编译、资源与引用 | 15 个预制体、6 个 Build 场景检查通过；见 `.local/prototype-resource-observations.txt` |
| 正式回归 | 20/20 通过，覆盖移动、战斗、维修、岗位、事件、存档及完整航程；见 `.local/item-repair-results.xml` |
| 素材与填充画面 | 加上临时渲染检查共 21/21 通过；见 `.local/item-repair-visual-observations.txt`、`.local/item-repair-bar-preview.png` |
| Windows 构建 | 成功，0 错误、0 警告；见 `.local/item-repair-build.log`、`.local/prototype-build-summary.txt` |

本机可运行产物：`.local/build-prototype/Ferryman.exe`。`.local` 被 Git 忽略，新检出需重新生成；临时素材导入器和渲染检查脚本已删除，正式测试仍为 20 项。键鼠手感由用户实际体验。

按修改范围执行所需检查。命令行运行前关闭同项目的 Unity 编辑器；以下为本机 PowerShell 命令，管道会等待 Unity 进程完成：

```powershell
Set-Location 'D:\UnityGames\Ferryman'
$ferrymanUnity = 'D:\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
New-Item -ItemType Directory -Path '.local' -Force | Out-Null

# 编译与资源引用检查
& $ferrymanUnity -batchmode -projectPath . -executeMethod Ferryman.Editor.PrototypeValidation.Verify -quit -logFile .local/verify.log | Out-Null

# PlayMode 回归；运行测试时不要添加 -quit
& $ferrymanUnity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults .local/playmode-results.xml -logFile .local/playmode.log | Out-Null

# 资源检查并生成 Windows 产物
& $ferrymanUnity -batchmode -projectPath . -executeMethod Ferryman.Editor.PrototypeValidation.VerifyAndBuild -quit -logFile .local/build.log | Out-Null
```

构建后检查 Git diff；最近构建曾自动重序列化 URP 资源，提交前应区分构建自动改动与实际修改，保留用户已有内容。

## 后续待定义或交付

人物、武器、未交付特效及正式 UI 继续等待用户素材和规格；现有显示挂点与事件入口见美术说明。独立岛屿地面可在交付后替换，目前使用从示意图拆出的公共地面。

商店和永久成长属于策划明确的首版排除项；特殊岗位能力尚未定义。后续收到明确要求再实施，不将这些内容当作当前 V0.1 的遗漏。
