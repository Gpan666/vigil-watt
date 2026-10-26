# 显示器身份与名称（任务栏贴片选屏）

> 贴片要钉在某一块屏上，就必须回答两个问题：**这块屏现在叫什么**（给用户看）和**这块屏还是不是上次那块**（给程序判断）。
> 2026-10-20 之前两者都用了 `\\.\DISPLAYn`，它只是「当前显示配置里的序号」，于是出现了两个症状：
> 设置页只能显示「主显示器 / 显示器 2」；切到「仅电脑屏幕」后贴片直接消失且不回来。

## 1. 三套 API 的实测对照

同一台机器（Dell XPS + 外接 DELL S2719DC，显示模式 = 仅第二屏），2026-10-20 探针结果：

| 来源 | 给出什么 | 实测值 |
|---|---|---|
| `MONITORINFOEX.szDevice`（`EnumDisplayMonitors`） | `\\.\DISPLAYn`，**当前配置内的序号** | `\\.\DISPLAY1`。同一块屏换一次显示模式就可能变成别的号 |
| `EnumDisplayDevices(NULL, i)` | 适配器名 | `Intel(R) Iris(R) Xe Graphics`，不是显示器 |
| `EnumDisplayDevices(适配器, 0, EDD_GET_DEVICE_INTERFACE_NAME)` | 子设备 `DeviceString` = EDID 名，`DeviceID` = 设备接口路径 | 活动屏给 `Generic PnP Monitor`；**未激活**的内置屏反而给 `Dell XPS AUO5B2D Display` |
| `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` + `GET_TARGET_NAME` | `monitorFriendlyDeviceName` + `monitorDevicePath` | `DELL S2719DC` + `\\?\DISPLAY#DEL4189#4&194fdf6a&0&UID8259#{e6f07b5f-…}` |
| `QueryDisplayConfig(QDC_ALL_PATHS)` | 未激活 target 的名字 | **空串**，拿不到 |

结论：

- **名字**只有 QDC 的活动路径可靠；`EnumDisplayDevices` 对活动屏经常只给 `Generic PnP Monitor`，别用它当显示名。
- **身份**只有 `monitorDevicePath` 可用：它由 EDID + 适配器实例组成，跨显示模式不变。
- **未激活的屏**两条路都拿不到名字（QDC 空串、EnumDisplayDevices 只有设备路径对得上时才给名字），所以名字必须在用户选中时**记下来**。

## 2. 存储契约（`AppSettings`）

| 键 | 内容 | 说明 |
|---|---|---|
| `TaskbarBatteryScreen` | `monitorDevicePath`；空或 `:primary:` = 跟随主屏 | 只用于匹配，不用于显示 |
| `TaskbarBatteryScreenLabel` | 友好名快照（`DELL S2719DC`） | 只用于显示；那块屏没插时设置页还能写出它的名字 |

老配置里的 `\\.\DISPLAYn` 仍可读：匹配不上身份时按 GDI 名兜底一次（见 §3 第 3 步）。

## 3. 解析链（`Services/MonitorSelection.cs`，纯函数、有单测）

`Resolve(screens, wanted)` 依次尝试：

1. 空或 `:primary:` → 主屏。
2. 身份精确匹配（忽略大小写）。
3. 老配置 `\\.\DISPLAYn` → 按 GDI 名匹配一次。
4. 型号段兜底：`#DEL4189#` 相同即认为是同一台显示器换了接口；**两块同型号时不猜**，直接放弃。
5. 都失败 → **降级到主屏**，并给出一条原因（`note`），由调用方去重后记日志。

第 5 步是「切显示模式后贴片不回来」的修复点：旧代码在这一步 `Detach()`，贴片就整块消失，配置还指向一个已经不存在的名字。

## 4. 设置页

- 列表项 = `友好名 · 宽×高`，主屏追加 `（主显示器）`；同名的屏追加 `（2）`。多屏时末尾多一项「跟随主显示器」。
- 配置的屏当前不在 → **仍然列出它**，文案 `<名字>（未连接）`，`Tag` 保持原身份。名字来源：`TaskbarBatteryScreenLabel` → `MonitorCatalog.FriendlyNameOf(路径)`（走 `EnumDisplayDevices`，未激活屏也能查到）→ `显示器 n`。
  这样保存时不会把它静默改回主屏。
- 右侧有刷新按钮：页面 `NavigationCacheMode = Required`，插拔显示器后不会自动重扫，只能手动触发。

## 5. 任务栏

- 主任务栏 `Shell_TrayWnd` 会跟随主屏；副屏任务栏是 `Shell_SecondaryTrayWnd`。
- 目标屏没有任务栏（关了「在所有显示器上显示任务栏」）→ 改贴主屏，别让贴片消失。
- 协调层 `TaskbarPillManager` 每 1 秒 `Reconcile` 一次；稳态走缓存快速路径，不发 `SetWindowPos`。

## 6. 坑

- **别用 `\\.\DISPLAYn` 当身份**，也别用它拼显示名。
- QDC 对未激活 target 返回空名字；`EnumDisplayDevices` 对活动屏常返回 `Generic PnP Monitor`。两者都别单独信。
- 降级日志必须去重（`NoteOnce`）：`Reconcile` 每秒一次，不去重会刷满日志。
- QDC 结构体长度要与原生一致（实测 `PATH_INFO=72`、`MODE_INFO=64`、`TARGET_DEVICE_NAME=420`、`SOURCE_DEVICE_NAME=84`），长度错了 `QueryDisplayConfig` 直接返回错误码。

## 7. 怎么验证

1. 启动后看日志：`任务栏贴片：贴到 <友好名>（\\.\DISPLAYn）`。
2. 把 `settings.json` 的 `TaskbarBatteryScreen` 改成一个不存在的路径 → 日志应出现 `配置的屏幕当前不在，临时贴到 <主屏名>`，且贴片仍在主屏上。
3. 真机切显示模式（仅电脑屏幕 ↔ 仅第二屏 ↔ 扩展）各一次，贴片应在 **2 秒内**跟到新任务栏（重建去抖要 2 拍，见 `runtime-pitfalls.md` §16.12）；设置页刷新按钮后列表应反映当前接法。
