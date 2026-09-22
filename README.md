# Audiolite

Windows 托盘音频输出切换器。单文件、无常驻依赖、约 10 MB 私有内存。

## 为什么造这个

原本用 [SoundSwitch](https://github.com/Belphemur/SoundSwitch),但只用它一个功能,而它常驻的成本是:

| | SoundSwitch 7.2.1 | Audiolite |
|---|---|---|
| 私有内存 | 54.5 MB | **10.2 MB** |
| 线程 | 29 | **10** |
| 句柄 | 808 | **271** |
| 后台轮询 | 每 30 秒全量枚举进程 | **无**(事件驱动) |
| 开机开销 | 6 秒 CPU、82 万次注册表事件 | 无 |
| 遥测 | 有 | 无 |

上表为本机 `Get-Process` 实测值,非估算。SoundSwitch 的轮询与开机行为记录在其仓库 issue [#2296](https://github.com/Belphemur/SoundSwitch/issues/2296),维护者明确拒绝优化。

## 构建

不需要安装任何东西。用 Windows 自带的 C# 编译器,零外部引用:

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe `
  -nologo -target:winexe -out:bin\Audiolite.exe Audiolite.cs
```

目标运行时是系统内置的 .NET Framework 4.8.1,不下载、不附带运行时。
`gdiplus` / `DWrite` / `WinForms` 全程不加载——这是内存能压到 10 MB 的原因。

跑测试(27 条,覆盖点击事件判定、设备命名编号、淡出序列):

```powershell
csc.exe -nologo -target:exe -main:TestClicks -out:test.exe Audiolite.cs TestClicks.cs
.\test.exe
.\test.exe --leak     # 句柄增长检查
```

## 用法

把 `Audiolite.exe --tray` 放进启动文件夹或注册表 `Run` 键。

| 操作 | 行为 |
|---|---|
| **左键** / 中键 | 切到上一台用过的设备。无历史时弹出菜单 |
| **右键** | 列出所有在线输出设备,当前设备打勾 |
| 每次切换 | 右下角弹横幅显示目标设备,1.6 秒后淡出 |

单实例:重复启动会静默退出,不会多出托盘图标。

### 设备命名

Windows 自己把消歧前缀塞在括号里(`耳机 (2- 蓝牙耳机A)`),本工具改成按**当前在线的同名设备**重排(`耳机 2 (蓝牙耳机A)`)。同组只有一台、或排在最前时不编号。

排序键是端点的接入时间,拿不到时退回安装时间。

## 下载与运行

Release 里的 `Audiolite.exe` 是**单文件绿色程序**,约 26 KB,下载后直接双击即可运行:
不需要配置文件、不需要同目录的其它文件、不需要安装运行时
(.NET Framework 4.8.1 是 Windows 内置组件)。

**首次运行会有 SmartScreen 拦截。** 本程序未做代码签名,Windows 大概率弹出
"已保护你的电脑 / 未知发布者",需点 **更多信息 → 仍要运行**。这是所有未签名
个人工具的通例,不是恶意软件提示。

开机自启需自行添加:把 `Audiolite.exe --tray` 加入启动文件夹,或写入注册表
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。

## 命令行

```
Audiolite.exe --list        全部渲染端点(含断开设备)
Audiolite.exe --menu        右键菜单会显示的内容
Audiolite.exe --props       端点属性原始值(排查命名用)
Audiolite.exe --set <ID>    直接设为指定设备
```

## 已知边界

- **接入时间属性是未公开的。** 排序用的 `{194ef948-…},2` 不在微软文档化的属性列表里。实测行为与"最近一次接入时间"一致(内置扬声器停在装机日、蓝牙耳机停在上次连接日),且已排除"上次设为默认时间"这一候选(设为默认不刷新它)。微软改语义的风险存在,但影响面只是菜单顺序,不影响切换正确性——真正用于切换的是 MMDevice ID。
- **编号会随连接情况变化。** 这是"按在线设备重排"的必然结果:拔掉一台,后面的编号会前移。
- **异常写盘。** `WndProc` 里的异常会记到 exe 同目录的 `diag.txt`。这是有意的:本项目一个 bug 曾被静默 `catch` 藏了很久。

## 许可证

MIT。
