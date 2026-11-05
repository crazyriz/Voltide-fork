<p align="center">
  <img src="docs/icon.png" width="112" alt="Voltide">
</p>
<h1 align="center">Voltide</h1>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-000000?logo=windows&logoColor=white" alt="Platform">
  <img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4" alt=".NET">
  <img src="https://img.shields.io/badge/size-8.32MB-74dd7c" alt="Size">
  <img src="https://img.shields.io/badge/license-MIT-74dd7c" alt="License">
</p>

主要功能&內容自行去原案看

## 修改內容

- 新增Wlmouse Beast Mini
- 取消A9Air改為F1V3Ult+(貌似通訊協議一樣)
- 新增LunaFury Type33 Evo
- 更改主畫面顯示資訊(小字)
- 更改Tray刷新&停留機制
- 更改主畫面手動刷新限制


## 使用方法

1. 从 [Releases](../../releases) 下载 `Voltide.exe`。
2. 运行。以托盤開啟。
3. 左键点击托盘图标打开面板，右键查看设备列表。

无需安装、无后台服务、无需管理员权限。

![托盘菜单](docs/tray-menu.png)

*托盘菜单列出每台设备及其当前电量，并提供刷新间隔与开机自启开关。*

### 如何理解面板

| 显示状态 | 含义 |
|---|---|
| 彩色百分比 + 已填充的闪电 | 设备在线并正常上报 |
| 灰色百分比 + 空的闪电 | 设备休眠；所示数值为最后一次读数 |
| `--` 与「设备休眠中」 | 设备处于空闲状态，且从未上报过 |

### 设置

- **刷新间隔** —— 面板底部的三个选项（`15s / 30s / 90s`），默认 90 秒。面板隐藏到托盘后，刷新放慢至 5 分钟。
- **开机自启** —— 托盘菜单中的开关。写入当前用户的启动项，无需提权。

## 系统要求

- Windows 10 1903 或更高版本，或 Windows 11
- .NET Framework 4.8（上述系统已内置）

## 构建

```powershell
# 编译（使用 Windows 自带的 C# 编译器）
powershell -ExecutionPolicy Bypass -File build.ps1

# 生成可分发的单文件版本
powershell -ExecutionPolicy Bypass -File pack.ps1 -Target dist
```

三款字体以清单资源的形式嵌入可执行文件，因此产物为单个文件。

## 协议说明

这些协议均非厂商公开。各帧格式已写在对应适配器的文件头注释中。

- **罗技** —— HID++ 2.0。特性 `0x1004` 的功能 1 返回百分比；老款接收器回退到特性 `0x1000`。
- **迈丛** —— 报告 `0x13` 上的 20 字节帧。命令 `0x4A`，第 5 字节为百分比，第 19 字节为前序字节的模 256 校验和。
- **ROG** —— 向接收器的厂商集合（用途页 `0xFF00`）写入 64 字节帧 `02 12 01`。回复的第 6 字节为百分比，第 10 字节为设备上配置的低电量阈值。

有一处值得记录的陷阱：罗技 `0x1004` 的**功能 0** 返回的是电压而非电量——设备空闲时它恒定为 3.85 V，与真实电量无关。返回百分比的是功能 1（`0x1E`）。早期版本读取功能 0，这正是 Voltide 显示的数值与 G HUB 不一致的原因。

## 已知限制

- 从未上报过电量的设备在休眠时无法被查询唤醒，将显示 `--`，直至被使用。
- ROG 接收器同时服务于其他外设。若其配对的是鼠标而非键盘，Voltide 仍会将其标记为键盘。
- 共享同一接收器的无线设备通过接收器上报，而非各自独立上报。
- 同品牌但不同型号的设备会被识别为现有型号。适配层目前按厂商与接口特征匹配，尚未区分具体型号。
- 迈丛与狼蛛共用 USB 标识（`3554:FA09`）。目前仅适配迈丛的查询协议，狼蛛键盘可能被识别为迈丛型号，读数也可能不正确。两者需要通过监听设备主动上报来区分，尚未实现。
- XInput 手柄只能读到**四档**电量（满 100% / 中 50% / 低 20% / 空 5%），不是精确百分比。卡片详情会标注 `XInput 四档`，避免误读为实测值。

## 许可

MIT，详见 [LICENSE](LICENSE)。

设计与开发：[Joko](https://github.com/
