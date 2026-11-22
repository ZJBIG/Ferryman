# Ferryman · 摆渡人

在岛屿之间护送乘客。船只沿航线自动前进，你在甲板上射击敌人、清除障碍、修补破损，与船员一起把乘客送到目的地。

## 打开试玩

- **策划、美术和试玩者（Windows 64 位）**　[下载 Windows 试玩包](https://github.com/ZJBIG/Ferryman/releases/download/v0.1-playtest.1/Ferryman-Windows-x64.zip)，解压整个 ZIP，双击 `Ferryman.exe`。无需安装 Unity。
- **通过 Unity 试玩**　克隆仓库或从 GitHub 的 `Code → Download ZIP` 下载源码。在 Unity Hub 中添加项目根目录，使用 **Unity 2022.3.62f3c1** 打开，双击 [main 场景](Assets/Ferryman/Scenes/main.unity)，点击顶部 ▶ Play。

![甲板上的物品与维修](Docs/Images/Gameplay.png)

## 玩法与操作

在出发岛选择乘客，点击「护送启航」。守住甲板、逐波打退敌人、接送顺路乘客，抵达终点后结算报酬。战斗船员会自动攻击，维护船员会绕障并修补破损。

| 操作 | 行为 |
| --- | --- |
| WASD / 方向键 | 移动 |
| 移动鼠标 | 瞄准 |
| 按住鼠标左键 | 连续射击 |
| 点击鼠标右键 | 近战，把石块和炸弹踢出船外 |
| 靠近破损处按住 E | 维修 |

事件道具从画面上方落下，敌人也会投掷炸弹。引信在落地后开始倒计时。移动、攻击、眩晕或松开 E 会中断维修，进度保留。异常退出后可从最近的航段恢复。主动退出会丢弃本轮未结算收益。

## 开发与美术接入

- [main 场景](Assets/Ferryman/Scenes/main.unity)是完整航程入口，也是 Build Settings 的首场景。Windows 试玩包通过 `File → Build Settings` 选择 Windows 64 位构建。
- 玩法在 [Scripts](Assets/Ferryman/Scripts)，材质、贴图与挂点见 [美术接入说明](Docs/ArtIntegration.md)，策划规则与验证范围见 [原型说明](Docs/PrototypeAudit.md)。
- 在 `Window → General → Test Runner` 打开 **PlayMode**，点击 **Run All** 执行回归。
