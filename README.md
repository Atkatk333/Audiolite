# Audiolite

![Release](https://img.shields.io/github/v/release/Atkatk333/Audiolite)
![Downloads](https://img.shields.io/github/downloads/Atkatk333/Audiolite/total)
![License](https://img.shields.io/github/license/Atkatk333/Audiolite)

Windows 托盘音频输出切换器,点图标切换输出设备。单个 exe 文件,无外部依赖,常驻内存约 10 MB。

## 下载

[Audiolite.exe](https://github.com/Atkatk333/Audiolite/releases/latest),双击即可运行。无需配置文件,无需安装运行时(.NET Framework 4.8.1 由系统提供)。支持 Windows 10/11。

程序写入两个文件:`state.txt` 保存上一台设备(左键回切使用),`diag.txt` 仅在出错时产生。exe 所在目录不可写时,改写到 `%LOCALAPPDATA%\Audiolite\`。`state.txt` 两处都写,读取时取修改时间较新的一份;`diag.txt` 只写第一个可写的目录。

未做代码签名,首次运行会触发 SmartScreen 提示,选择 更多信息 → 仍要运行 即可。

开机自启需自行添加:将 `Audiolite.exe --tray` 放入启动文件夹,或写入注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。

## 用法

| 操作 | 行为 |
|---|---|
| 左键 / 中键 | 切换到上一台用过的设备;无历史记录时打开菜单 |
| 右键 | 列出全部在线输出设备,当前设备打勾 |
| 每次切换 | 右下角显示目标设备名,1.6 秒后淡出;切换失败时显示失败提示 |

横幅不获取焦点,不接收鼠标点击,不会最小化无边框窗口。

托盘图标的悬停文字显示当前设备,并随设备变更事件更新。单实例:重复启动时直接退出。

## 造轮子的初衷

原先使用 [SoundSwitch](https://github.com/Belphemur/SoundSwitch),只需要切换输出设备这一项功能,而它后台常驻的开销与这项功能不成比例。下表为本机 `Get-Process` 实测值。

| | SoundSwitch 7.2.1 | Audiolite 启动后 | Audiolite 连续运行 6 小时 |
|---|---|---|---|
| 私有内存 | 54.5 MB | 10.2 MB | 17.9 MB |
| 线程 | 29 | 10 | 13 |
| 句柄 | 808 | 271 | 407 |
| 后台轮询 | 定时全量枚举进程,源码默认间隔 2 秒 | 无,事件驱动 | 无 |
| 开机开销 | 6 秒 CPU,82 万次注册表事件 | 无 | 无 |
| 网络 | 有遥测 | 无网络导入 | 无网络导入 |

数据来源与限制:

- SoundSwitch 一列测于 2026-09-21,当晚该软件已卸载,无法复测。
- 轮询间隔来自上游源码 `SoundSwitch.Audio.Manager/ProcessMonitor.cs` 的默认参数 `intervalMs = 2000`,由 `AppModel.cs` 以无参方式构造。开机注册表事件数来自其仓库 issue [#2296](https://github.com/Belphemur/SoundSwitch/issues/2296),与轮询无关。
- Audiolite 两列分别测于 2026-09-21 与 2026-09-22(Win11 22631),对应程序集版本 0.5.0.0。0.5.1 及之后需重测。
- 私有内存随运行时间上涨,但不是活跃泄漏:空闲 25 秒两次采样,内存/句柄/线程均无变化;180 次枚举后句柄增量 +0/+1。增量主要来自第三方模块:该进程加载了 67 个模块,其中包括输入法的 `DWrite`、`d2d1`、`TextShaping`、`CrashRpt1500` 等。程序自身无网络导入,第三方模块的行为不在此列。

## 命令行

```
Audiolite.exe                 托盘模式
Audiolite.exe --tray          与无参数等价
Audiolite.exe --list          全部渲染端点及状态、排序键、ID、菜单名
Audiolite.exe --menu          右键菜单的内容
Audiolite.exe --props         端点属性原始值
Audiolite.exe --set <ID>      设为指定设备
Audiolite.exe --bannertest <文本>  显示一次横幅,5 秒后退出
Audiolite.exe --version       版本号
Audiolite.exe --help          用法说明
```

未识别或缺少参数的输入输出到 stderr,返回码 2,不会进入托盘模式。

## 设备命名与编号

Windows 的消歧前缀加在括号内(`耳机 (2- 蓝牙耳机A)`),仅在设备接口描述冲突时出现,同一类别下可能只有 2 而没有 1。本程序改为按当前在线的同类别设备重新编号(`耳机 2 (蓝牙耳机A)`),组内只有一台或排在首位时不编号。

排序键取端点的接入时间,取不到时退回安装时间。

## 构建

使用系统自带的 C# 编译器。`-noconfig` 跳过编译器的默认引用清单,加上该参数仍可通过编译,即无外部引用。

```powershell
mkdir bin
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe `
  -nologo -noconfig -target:winexe -out:bin\Audiolite.exe Audiolite.cs
```

目标运行时为系统内置的 .NET Framework 4.8.1。`gdiplus` 与 `WinForms` 不会被加载。

测试:

```powershell
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc -nologo -noconfig -r:System.dll -target:exe -main:TestClicks -out:test.exe Audiolite.cs TestClicks.cs
.\test.exe
.\test.exe --leak
```

测试覆盖点击事件判定、设备命名与编号、状态位映射、切换结果判定、横幅几何、状态文件读写、淡出序列。`--leak` 额外做两项本机检查:180 次枚举后的句柄增量须在 +12 以内;active 视图不得包含其他状态的端点,全量视图的计数须与直接向 COM 查询的结果一致。

纯函数测试无法覆盖的部分:掩码语义只在 `EnumAudioEndpoints` 的参数上生效,依赖本机检查;横幅坐标越界只记录到 `diag.txt`;切换成功后写入 `lastId` 这一行赋值没有断言,覆盖它需要真实切换一次输出设备。

## 已知边界

- 排序使用的接入时间属性 `{194ef948-…},2` 未见于微软文档。实测行为与最近一次接入时间一致,并已排除"上次设为默认时间"这一候选(设为默认不刷新该值)。本机 16 个端点中只有 7 个带有该属性,其余按安装时间排序,回退路径是多数路径,`--list` 第 3 列标明每台实际使用的键。若微软改变该属性语义,影响限于菜单顺序,不影响切换正确性,切换使用 MMDevice ID。
- 切换默认设备使用未公开的 `IPolicyConfig` 接口,SDK 头文件中没有该接口(`audiopolicy.h` 只有 `IAudioPolicy*`)。虚表顺序依据多个独立实现,并通过调用后回读默认设备加以验证,回读不一致即判定失败。
- 只设置控制台与多媒体两个角色,不设置通信角色,与 Windows 音量面板行为相同。按通信角色取设备的程序(Teams、YY 等)输出不会跟随切换。
- 编号随连接情况变化:断开一台后,后续设备的编号前移。
- 菜单不区分虚拟音频设备,处于 active 状态的虚拟端点会一并列出。
- `WndProc` 异常、切换失败、属性库读取失败均记录到 `diag.txt`,不静默丢弃。正常运行时不产生该文件。

## 许可证

MIT,见 [LICENSE](LICENSE)。
