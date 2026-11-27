# Ferryman · 摆渡人

在分支群岛之间护送乘客。船只自动航行，你在甲板上射击、清障和修船。第三日整备后离港，四位特殊船员与已送达的剧情乘客并肩迎战垃圾潮巨像。

## 打开试玩

- **Windows 64 位**　下载后双击 EXE，游戏会自动解包并启动。[下载单文件 EXE](https://github.com/ZJBIG/Ferryman/releases/download/v0.3-playtest.3/Ferryman-Windows-x64.exe)
- **Web / HTML5**　下载后双击 HTML，用最新版 Edge 或 Chrome 打开。[下载单文件 HTML](https://github.com/ZJBIG/Ferryman/releases/download/v0.3-playtest.3/Ferryman-WebGL.html)
- **开发项目（Unity）**　克隆仓库或从 GitHub 的 `Code → Download ZIP` 下载源码。在 Unity Hub 中添加项目根目录，使用 **Unity 2022.3.62f3c1** 打开，双击 [main 场景](Assets/Ferryman/Scenes/main.unity)，点击顶部 ▶ Play。

![甲板上的物品与维修](Docs/Images/Gameplay.png)

## 玩法与操作

从出发岛点击「空船启航」，跟随开局教程熟悉操作，再在海图选择下一座岛。每回经过八座岛、七段海路。靠港时可以听乘客讲述出行缘由，再决定接载或谢绝。战斗船员自动射击，维护船员绕障维修。船况、局内升级和报酬延续至第三日。第三日抵达终点港后可以消费金币整备，准备好就点击「离港迎战」。

前往战斗岛、精英岛或终点港时有固定海战。其他航段在三日内按 40%、60%、80% 的概率遭遇海怪，靠港后照常处理岛上活动。普通海战最多四波，海鸥会带来石块或炸弹。每天首战有对白，其余普通海战直接预警。

垃圾潮巨像约有 12,667 点生命，每 10 秒按当时的血量阶段召来 2、4 或 6 只旧敌，其中有会登船的翻舷蟹。用玉米枪攻击巨像，击退全部敌人并清理余弹后，在船尾收回残骸、拖回港口结算。

火力、连射、船体强化、快速维修、并行维修、维修范围共有六类成长。岛屿奖励提供最多三选一，集市消费本轮报酬。普通局内与永久升级各最多五级，第三日终点港的四项金币整备可以超过五级，价格随等级上涨。

| 操作 | 行为 |
| --- | --- |
| WASD / 方向键 | 移动 |
| 移动鼠标 | 瞄准 |
| 按住鼠标左键 | 连续射击 |
| 点击鼠标右键 | 挥萝卜刀伤敌、弹开友军，把石块和炸弹清出甲板 |
| 靠近破损处按住 E | 维修 |
| 终战后在船尾按 F | 回收残骸及系缆 |

每段海路事件前后各自由航行五秒，事件中船只、动画和游戏时间照常推进。炸弹落地后开始引信，爆炸损伤船体并弹开附近友军，不眩晕人物。移动、攻击、眩晕或松开 E 会中断维修，进度保留。异常退出后可从最近检查点恢复选路、奖励和升级；主动放弃或正常关闭丢弃本轮未结算收益。

## 开发与美术接入

- [main 场景](Assets/Ferryman/Scenes/main.unity)是完整航程入口，也是 Build Settings 的首场景。Windows 与 WebGL 构建命令见 [交接说明](Docs/Handoff.md)。
- 玩法在 [Scripts](Assets/Ferryman/Scripts)，分支与成长规则见 [群岛策划](Docs/ExpeditionDesign.md)，材质、贴图与挂点见 [美术接入说明](Docs/ArtIntegration.md)，验证范围见 [原型说明](Docs/PrototypeAudit.md)。
- 在 `Window → General → Test Runner` 打开 **PlayMode**，点击 **Run All** 执行回归。
