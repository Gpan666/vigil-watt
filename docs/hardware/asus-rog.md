# ROG Strix Scope II 96 Wireless / Omni

## 1. 范围与来源

本地实测设备：ROG Strix Scope II 96 Wireless，ROG Omni 接收器 USB `0B05:1ACE`。
[华硕官方 Omni FAQ](https://rog.asus.com/support/faq/1050878/) 明确列出这一兼容键盘。
接收器配对列表实测报告键盘型号 ID `1AB0`，通道 report ID `02`，类型 `04`。

协议事实参考：

- [G-Helper 键盘基类](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Keyboard/AsusKeyboard.cs)：
  电量查询 `12 01`、电量偏移 6、充电偏移 9。
- [G-Helper Scope II 96 / Omni 型号](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Keyboard/Models/StrixScopeII.cs)：
  `MI_02&Col02`、report ID `02`、64 字节报告。
- [rigstats Omni 配对协议](https://github.com/dvalfrid/rigstats/blob/main/sensor-sidecar/Control/Lighting/OmniMouse.cs)：
  接收器 `A0 00` 只读配对列表及每项的 PID / report ID / 类型。
- [rigstats 键盘型号与电量](https://github.com/dvalfrid/rigstats/blob/main/sensor-sidecar/Control/Lighting/AsusKeyboardDevice.cs)：
  Scope II 96 型号 ID 为 `1AAE/1AAF/1AB0`。

G-Helper 为 GPL-3.0 项目；rigstats 为 MIT 项目。本次仅据协议事实自行实现，
没有复制其实现代码。不接入灯效、配对修改、按键或固件控制。

## 2. 接口和请求

只认领 usage `FF00:0001`、输入/输出均 64 字节、report ID `02`、
路径 `MI_02&Col02` 的候选通道。
首次探测额外打开**同一物理接收器**的 report ID `01` 通道核实配对型号，
禁止把另一只接收器的身份套过来。

| 用途 | 请求（补零到 64 字节） | 响应判据 |
|---|---|---|
| 接收器配对表 | `01 A0 00` | 同头部，[3] 项数；[5] 起每项 4 字节：PID 小端、report ID、设备类型 |
| 键盘电量 | `02 12 01` | 同头部，[6] 电量 0–100，[9] 充电 0/1 |

配对表必须在通道 2 找到且仅找到一款已知 Scope II 96，类型为键盘 4；
否则不向键盘通道继续查询，也不误标接收器或其他型号。
接收器键由接口 collection 与实例尾部归一化取得；不同物理接收器不能串用身份。

初始化模型核实需要一次接收器身份查询和一次键盘电量查询，分别走两个已核实的接口。
之后每轮只发一条电量查询；不加入 15 秒写入发现。每个请求回复窗口 700 ms，
跳过其他事件或不匹配的回应，不进行未知命令扫描。没有响应走基类失败退避。

## 3. 实机证据

2026-10-25：
- 配对表头 `01 A0 00 01 00 B0 1A 02 04`：Scope 型号 1AB0 在 report 2。
- 电量回复头 `02 12 01 00 00 00 5F 02 01 00 14 5F BC 10`：**95%**，未充电。
- 其余字节补零到 64 字节。

默认 `PluginEnabled["Asus"]=false`，设置页品牌名「华硕 / ASUS ROG」。
不读取键盘输入，也不将接收器的鼠标通道认领为键盘。

未实测：物理插拔、自然休眠/唤醒、实际充电切换、低电量、有线或蓝牙模式、
其他键盘/固件、多只 Omni 同时连接。单测边界不等于这些场景已做硬件验证。

## 4. 插件集成验证

实际插件读取键盘 95% / Keyboard / Discharging；默认关闭不认领，
启用后的型号核实、轮询、独立 SQLite 历史与 Detach 释放均通过。
构建与回归检查见 [适配审查](../reviews/archive/2026-10-25-asus-razer-adaptation-review.md)。
