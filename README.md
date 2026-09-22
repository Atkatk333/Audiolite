# Audiolite

![Release](https://img.shields.io/github/v/release/Atkatk333/Audiolite)
![Downloads](https://img.shields.io/github/downloads/Atkatk333/Audiolite/total)
![License](https://img.shields.io/github/license/Atkatk333/Audiolite)

Windows 托盘音频输出切换器:点一下图标换一台输出设备。单文件 26 KB,无常驻依赖,私有内存约 10 MB。

## 下载

**[Audiolite.exe](https://github.com/Atkatk333/Audiolite/releases/latest)** —— 下载后直接双击运行,不需要配置文件、不需要同目录的其它文件、不需要安装任何运行时(.NET Framework 4.8.1 是 Windows 内置组件)。仅支持 Windows 10/11。

**首次运行会被 SmartScreen 拦一下。** 本程序未做代码签名,Windows 大概率弹出"已保护你的电脑 / 未知发布者",点 **更多信息 → 仍要运行** 即可。这是所有未签名个人工具的通例,与程序本身是否有恶意无关。

开机自启需自行添加:把 `Audiolite.exe --tray` 放进启动文件夹,或写入注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。

## 用法

| 操作 | 行为 |
|---|---|
| **左键** / 中键 | 切到上一台用过的设备;无历史时弹出菜单 |
| **右键** | 列出所有在线输出设备,当前设备打勾 |
| 每次切换 | 右下角显示目标设备名,1.6 秒后淡出 |

横幅不抢焦点:游戏里的无边框窗口不会被最小化。单实例:重复启动静默退出,不会多出托盘图标。

## 命令行

```
Audiolite.exe [--tray]      托盘模式(默认,无参数即进入)
Audiolite.exe --list        全部渲染端点及其状态
Audiolite.exe --menu        右键菜单会显示的内容
Audiolite.exe --props       端点属性原始值(排查命名用)
Audiolite.exe --set <ID>    直接设为指定设备
Audiolite.exe --version     打印版本号
```

## 设备命名与编号

Windows 的消歧前缀塞在括号里(`耳机 (2- 蓝牙耳机A)`),且只在设备接口描述冲突时才加,同类别下可能出现"只有 2、没有 1"。本工具自己编号:按**当前在线的同名设备**重排(`耳机 2 (蓝牙耳机A)`),同组只有一台、或排在最前时不编号。

排序键是端点的接入时间,取不到时退回安装时间。

## 为什么自研

只用得到 [SoundSwitch](https://github.com/Belphemur/SoundSwitch) 的"切换输出设备"这一项,而它后台常驻的成本远高于这一项。本机 `Get-Process` 实测对比,非估算:

| | SoundSwitch 7.2.1 | Audiolite |
|---|---|---|
| 私有内存 | 54.5 MB | **10.2 MB** |
| 线程 | 29 | **10** |
| 句柄 | 808 | **271** |
| 后台轮询 | 每 30 秒全量枚举进程 | **无**(事件驱动) |
| 开机开销 | 6 秒 CPU、82 万次注册表事件 | 无 |
| 遥测 | 有 | 无 |

其中开机阶段的轮询与注册表事件记录于 SoundSwitch 仓库 issue [#2296](https://github.com/Belphemur/SoundSwitch/issues/2296)。

## 构建

不需要安装任何东西。用 Windows 自带的 C# 编译器,零外部引用:

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe `
  -nologo -target:winexe -out:bin\Audiolite.exe Audiolite.cs
```

目标运行时是系统内置的 .NET Framework 4.8.1,不下载、不附带运行时。`gdiplus` / `DWrite` / `WinForms` 全程不加载——这是内存能压到 10 MB 的原因。

测试(27 条,覆盖点击事件判定、设备命名编号、淡出序列):

```powershell
csc.exe -nologo -target:exe -main:TestClicks -out:test.exe Audiolite.cs TestClicks.cs
.\test.exe
.\test.exe --leak     # 句柄增长检查
```

## 已知边界

- **接入时间属性是未公开的。** 排序用的 `{194ef948-…},2` 不在微软文档化的属性列表里。实测行为与"最近一次接入时间"一致(内置扬声器停在装机日、蓝牙耳机停在上次连接日),且已排除"上次设为默认时间"这一候选(设为默认不刷新它)。微软改语义的风险存在,但影响面只是菜单顺序,不影响切换正确性——真正用于切换的是 MMDevice ID。
- **编号会随连接情况变化。** 这是"按在线设备重排"的必然结果:拔掉一台,后面的编号会前移。
- **菜单不过滤虚拟音频设备。** 录屏、回环一类虚拟端点只要处于 active 就会出现在菜单里,需自行忽略。
- **异常落盘。** `WndProc` 里的异常不静默吞掉,会记到 exe 同目录的 `diag.txt`,便于事后定位。

## 许可证

MIT。详见 [LICENSE](LICENSE)。
