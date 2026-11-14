# Ferryman 项目约定

- 引擎版本以 `ProjectSettings/ProjectVersion.txt` 为准：Unity 2022.3.62f3c1。项目使用现有 2D URP 和旧 Input Manager，不为本原型增加输入或镜头依赖。
- 可运行入口是 `Assets/Ferryman/Scenes/Prototype.unity`，已加入 Build Settings。原模板场景保留，但不是启动入口。
- 业务代码、预制体和测试位于 `Assets/Ferryman`，脚本使用 `Ferryman` 命名空间。运行时代码与 PlayMode 测试分别使用 `Ferryman.Runtime` 和 `Ferryman.PlayModeTests` 程序集。
- 当前只实现玩家移动和船边界碰撞。敌人、船员、子弹、伤害系统保持空类，直到收到对应玩法需求。不要新增对象池、事件总线、属性系统或镜头控制。
- 船体固定。BoatDeck 用闭合 EdgeCollider2D 阻挡边缘，LineRenderer 与它共享顶点。不要改为覆盖整个甲板的实心碰撞箱。玩家使用无重力、锁旋转的动态刚体和完整 BoxCollider2D；零摩擦材质保证沿边滑动。
- 在 Update 读取输入，在 FixedUpdate 写入刚体速度。斜向输入限制为长度 1；玩家初始位置须在甲板内。不通过 Transform 位移绕过碰撞。
- 修改移动或边界后运行 `Assets/Ferryman/Tests/PlayMode` 中的测试；命令和范围见 README。测试以默认原型尺寸和速度为基准，调整默认参数时同步更新有意义的预期。
- 提交 Unity 资源时同时提交 `.meta`。不要提交 Library、UserSettings、日志、构建产物、凭据或 `.plastic`。
