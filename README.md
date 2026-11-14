# Ferryman · 摆渡人

Unity 2D 最小原型：船体和镜头固定，玩家在红色矩形甲板内移动，边界阻挡玩家的完整碰撞箱。以横向活动空间为主，纵向活动空间较窄。

## 运行

1. 使用 **Unity 2022.3.62f3c1** 打开仓库根目录，等待资源导入完成。
2. 打开 `Assets/Ferryman/Scenes/Prototype.unity`，Game 视图选择 **16:9**。
3. 点击 Play，并点击 Game 视图使其获得键盘焦点。
4. 使用 **WASD / 方向键** 移动；可斜向移动，斜向速度与单轴速度相同。松开按键即停止。

红框内为可移动区域，黄色轮廓为固定船体，蓝色人物为占位图。玩家不能进入船头或走出红框。默认甲板宽 12、高 3，玩家碰撞箱宽 0.6、高 1.2，移动速度为每秒 4 个 Unity 单位。

## 代码与资源

```text
Assets/Ferryman/
├── Art/                 占位船体、人物和白色基础贴图
├── Materials/           渲染材质与零摩擦物理材质
├── Prefabs/
│   ├── Boat.prefab      固定船体、甲板和红色碰撞边界
│   └── Player.prefab    玩家显示、刚体和碰撞箱
├── Scenes/
│   └── Prototype.unity 可直接运行的原型场景
├── Scripts/
│   ├── Characters/     PlayerCharacter、EnemyCharacter、CrewMember
│   ├── Combat/         Projectile、DamageSystem
│   └── World/          BoatDeck
└── Tests/PlayMode/      在实际原型场景中运行的移动和碰撞测试
```

| 类 | 当前职责 |
| --- | --- |
| `PlayerCharacter` | 在 Update 读取已有 Horizontal / Vertical 输入，在 FixedUpdate 设置 2D 刚体速度；限制斜向速度，关闭重力和旋转，启用连续碰撞 |
| `BoatDeck` | 根据宽高生成闭合 EdgeCollider2D，并用相同顶点绘制红框；矩形内部没有实心障碍 |
| `EnemyCharacter` | 空类，未挂载到场景 |
| `CrewMember` | 空类，未挂载到场景 |
| `Projectile` | 空类，未挂载到场景 |
| `DamageSystem` | 空类，未挂载到场景 |

碰撞由 Unity 原生 2D 物理处理。玩家使用动态 Rigidbody2D 和 BoxCollider2D，船边界是静态闭合边线。零摩擦材质使玩家抵住边界时仍可沿边滑动。红框在编辑和运行时都能看到，不依赖 Gizmos。

Inspector 中可调整 Player 的 `Move Speed`，以及 Boat 下 Deck Boundary 的 `Width` / `Height`。玩家初始位置必须在边界内部，且碰撞箱必须小于甲板；本原型不实现出生点修正。边界尺寸和红框由同一个组件维护，船体占位图的尺寸保持固定。

未实现生命值、攻击、射击、敌人移动、伤害事件、对象池、独立输入事件系统或镜头移动。保留原项目的 2D / URP 配置和模板场景，没有引入新依赖。

## 验证

在 Unity 菜单中打开 `Window → General → Test Runner`，切换 **PlayMode**，点击 **Run All**。

测试覆盖无输入静止、移动距离、松开输入、斜向等速、四边和四角阻挡、沿边滑动与离开边界、船和镜头固定，以及红框与实际碰撞顶点一致。测试使用实际 `Prototype` 场景和 Unity 2D 物理模拟，直接调用玩家移动方法；键盘绑定沿用 `ProjectSettings/InputManager.asset`。

命令行验证示例（运行前关闭使用此项目的 Unity Editor）：

```powershell
$unityEditor = 'D:\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
$projectDirectory = (Get-Location).Path
New-Item -ItemType Directory -Path .local -Force | Out-Null
& $unityEditor -batchmode -projectPath $projectDirectory -runTests -testPlatform PlayMode -testResults "$projectDirectory/.local/playmode-results.xml" -logFile "$projectDirectory/.local/playmode.log"
```

根据本机安装位置调整编辑器路径。测试完成后 Unity 自动退出，不额外传入 `-quit`。

## 仓库约定

提交 `Assets`（含全部 `.meta`）、`Packages` 和 `ProjectSettings`。`Library`、日志、构建输出、IDE 缓存、本机设置和 Unity Version Control 元数据由 `.gitignore` 排除。新增脚本使用 `Ferryman` 命名空间，按角色、战斗或世界职责放入现有目录。
