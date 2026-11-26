# Ferryman 开发交接

更新日期为 2026-11-26。仓库见 [GitHub](https://github.com/ZJBIG/Ferryman)。

使用 Unity **2022.3.62f3c1** 打开项目，运行 [main 场景](../Assets/Ferryman/Scenes/main.unity)。默认分支图包含 20 个节点，一轮经过 8 座岛、7 段海路；出发、停靠、海路、结算和返家复用 main，不重载场景。旧线性配置与旧检查点继续兼容。

三日航程可从 Unity main 运行，当前 Windows 和 WebGL 下载入口见 [README](../README.md)。历史验证记录见 [PrototypeAudit](PrototypeAudit.md)。

## 权威入口

| 内容 | 位置 |
| --- | --- |
| 项目约定与当前实现约束 | [AGENTS.md](../AGENTS.md) |
| 分支岛屿、风险收益、六类升级与价格 | [ExpeditionDesign.md](ExpeditionDesign.md) |
| 策划覆盖、恢复边界和验证记录 | [PrototypeAudit.md](PrototypeAudit.md) |
| 素材挂点、世界尺寸、特效与 UI | [ArtIntegration.md](ArtIntegration.md) |
| 默认路线、遭遇与船员 | [Resources/Voyage](../Assets/Ferryman/Resources/Voyage)，入口 `DefaultVoyage.asset` |
| 航程、图、活动、升级、存档与界面 | [Scripts/Voyage](../Assets/Ferryman/Scripts/Voyage) |
| PlayMode 回归 | [Tests/PlayMode](../Assets/Ferryman/Tests/PlayMode) |
| 资源校验和 Windows 构建 | [PrototypeValidation.cs](../Assets/Ferryman/Editor/PrototypeValidation.cs) |
| 玩家入口与下载 | [README.md](../README.md) |

## 接续约束

- 保持 2D URP、旧 Input Manager；显示放子物体，根节点保留行为与碰撞。甲板原 12×5 网格已在船头扩展 3 列，旧格索引兼容；边界仍是闭合 EdgeCollider2D。
- Update 读输入、FixedUpdate 写刚体速度。进出场整体移动 Vessel 并暂停/恢复角色刚体模拟；区域显示按 `sceneName` 映射，不绑定固定站点索引。
- 固定 NPC 岗位不变，空闲自由闲逛，任务优先。船长维修无升级基准为 30 HP/s、范围 1、同修 1；并行每点完整速度，移动/攻击意图、眩晕、松键与禁用中断维修。
- 海路事件前后各航行休整 5 秒；战斗、炸弹和落石期间时间、海面、船浪和角色行动持续。炸弹损船并弹开友军，不眩晕人物；飞踢可弹开友军。
- 船长的玉米枪有独立枪口火光和后坐力，萝卜刀沿用近战碰撞。海怪命中反馈包含闪白、回弹、黄色粒子和飘字；船长和船员受到眩晕时先倒地，再闪白回弹。破损贴图画在角色脚下，维修进度条画在角色上方。
- 普通局内与永久升级各最多 5，相加生效。第三日终点港的四项金币整备可超过 5 级，终战入口恢复时保留。集市用本轮钱包，永久整备用结算钱包。写盘成功才替换永久内存数据，结算按 runId 去重。
- 正常关闭/主动放弃丢弃本轮，强退恢复区域检查点。分支选路、岛屿处理、待选奖励与局内等级随检查点保存；恢复先应用船上限再还原 HP。version 1 缺升级字段按零级，null/空路径按旧线性。测试只用临时存档目录。
- Unity 资源提交须带 `.meta`；不提交 Library、UserSettings、日志、构建、凭据或 `.plastic`。构建后核对 diff，区分自动重序列化与本轮修改。

## 复现命令

先关闭同项目的 Unity 编辑器，再按改动范围执行所需检查。以下 PowerShell 管道会等待 Unity 进程结束；PlayMode 测试不要添加 `-quit`。

```powershell
Set-Location 'D:\UnityGames\Ferryman'
$ferrymanUnity = 'D:\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
New-Item -ItemType Directory -Path '.local' -Force | Out-Null

# 编译与资源引用
& $ferrymanUnity -batchmode -projectPath . -executeMethod Ferryman.Editor.PrototypeValidation.Verify -quit -logFile .local/verify.log | Out-Null

# PlayMode 回归
& $ferrymanUnity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults .local/playmode-results.xml -logFile .local/playmode.log | Out-Null

# 校验并构建 Windows；构建不等于发布
& $ferrymanUnity -batchmode -projectPath . -executeMethod Ferryman.Editor.PrototypeValidation.VerifyAndBuild -quit -logFile .local/build.log | Out-Null

# WebGL 构建须安装同版本 WebGL Build Support
& $ferrymanUnity -batchmode -projectPath . -buildTarget WebGL -executeMethod Ferryman.Editor.PrototypeValidation.BuildWeb -quit -logFile .local/build-web.log | Out-Null
```

Windows 产物位于 `.local/build-prototype`，WebGL 位于 `.local/build-web`。WebGL 通过静态 HTTP 服务器运行，浏览器存档用 IndexedDB 同步。打包时把 `Assets/Ferryman/Resources/Voyage/NotoSansSC-OFL.txt` 附在 ZIP 中。

特殊岗位能力和未交付人物、武器、特效继续等待定义或素材；独立岛屿地面交付后可替换，现行尺寸与挂点以 ArtIntegration 为准。
