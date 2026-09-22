# Audiolite

![Release](https://img.shields.io/github/v/release/Atkatk333/Audiolite)
![Downloads](https://img.shields.io/github/downloads/Atkatk333/Audiolite/total)
![License](https://img.shields.io/github/license/Atkatk333/Audiolite)

Windows 托盘音频输出切换器:点一下图标换一台输出设备。单文件 26 KB,无常驻依赖,私有内存约 10 MB。

## 下载

**[Audiolite.exe](https://github.com/Atkatk333/Audiolite/releases/latest)** —— 下载后直接双击运行,不需要配置文件、不需要同目录的其它文件、不需要安装任何运行时(.NET Framework 4.8.1 是 Windows 内置组件)。只会在 exe 旁边写两个自己的文件:`state.txt`(记住上一台设备,左键回切靠它)和出错时才产生的 `diag.txt`。仅支持 Windows 10/11。

**首次运行会被 SmartScreen 拦一下。** 本程序未做代码签名,Windows 大概率弹出"已保护你的电脑 / 未知发布者",点 **更多信息 → 仍要运行** 即可。这是所有未签名个人工具的通例,与程序本身是否有恶意无关。

开机自启需自行添加:把 `Audiolite.exe --tray` 放进启动文件夹,或写入注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。

## 用法

| 操作 | 行为 |
|---|---|
| **左键** / 中键 | 切到上一台用过的设备;无历史时弹出菜单 |
| **右键** | 列出所有在线输出设备,当前设备打勾 |
| 每次切换 | 右下角显示目标设备名,1.6 秒后淡出;切换失败时显示"切换失败",不报假成功 |

横幅不抢焦点,也不会挡住点击:游戏里的无边框窗口不会被最小化,落在右下角那块区域的点击会穿透到下面的窗口。单实例:重复启动静默退出,不会多出托盘图标。

鼠标悬停在图标上显示当前设备,你用 Windows 音量浮窗切换它也跟着变;"上一台"这笔账按真实默认设备对账,不会因为外部切换而卡住。

## 命令行

```
Audiolite.exe                 托盘模式:左键回切上一台,右键列出在线设备
Audiolite.exe --tray          与无参数完全等价(自启配置里用哪个都行)
Audiolite.exe --list          全部渲染端点(含未插入、已禁用)及其状态
Audiolite.exe --menu          右键菜单会显示的内容
Audiolite.exe --props         端点属性原始值(排查命名用)
Audiolite.exe --set <ID>      直接设为指定设备(真的会改声音输出)
Audiolite.exe --bannertest <文本>  只画一次切换横幅,5 秒后自动退出
Audiolite.exe --version       打印版本号
Audiolite.exe --help          以上全部
```

未知参数、缺参数一律输出到 stderr 并返回 2,不会静默起一个托盘。

## 设备命名与编号

Windows 的消歧前缀塞在括号里(`耳机 (2- 蓝牙耳机A)`),且只在设备接口描述冲突时才加,同类别下可能出现"只有 2、没有 1"。本工具自己编号:按**当前在线的同名设备**重排(`耳机 2 (蓝牙耳机A)`),同组只有一台、或排在最前时不编号。

排序键是端点的接入时间,取不到时退回安装时间。

## 为什么自研

只用得到 [SoundSwitch](https://github.com/Belphemur/SoundSwitch) 的"切换输出设备"这一项,而它后台常驻的成本远高于这一项。本机 `Get-Process` 实测对比,非估算:

| | SoundSwitch 7.2.1 | Audiolite 刚启动 | Audiolite 连跑 6 小时 |
|---|---|---|---|
| 私有内存 | 54.5 MB | **10.2 MB** | 17.9 MB |
| 线程 | 29 | **10** | 13 |
| 句柄 | 808 | **271** | 407 |
| 后台轮询 | 定时全量枚举进程,源码默认 **2 秒**一次 | 无(事件驱动) | 无 |
| 开机开销 | 6 秒 CPU、82 万次注册表事件 | 无 | 无 |
| 网络 | 有遥测 | 零网络导入 | 零网络导入 |

口径说明,三条数据来源各不一样,别混着引用:

- SoundSwitch 那列是 2026-09-21 卸载前在本机量的,已无法复测。
- 轮询间隔引自上游源码 `SoundSwitch.Audio.Manager/ProcessMonitor.cs` 的默认参数 `intervalMs = 2000`(`AppModel.cs` 以无参方式构造它);开机注册表事件数是另一件事,来自其仓库 issue [#2296](https://github.com/Belphemur/SoundSwitch/issues/2296)。
- "连跑 6 小时"那列是 2026-09-22 在 Win11 22631 上量的。私有内存确实会随运行时间上涨,但**不是活跃泄漏**:空闲 25 秒两次采样的内存/句柄/线程三项零增长,180 次枚举句柄 +0/+1。涨上来的部分多半不是本程序引入的——67 个已加载模块里含第三方输入法整条链(`DWrite` / `d2d1` / `TextShaping` / `CrashRpt1500` 等),所以"零网络"这个断言只对本程序自己成立,对被人塞进进程的代码不成立。

## 构建

不需要安装任何东西,用 Windows 自带的 C# 编译器。`-noconfig` 会跳过编译器自带的那份默认引用清单,加上它还能编过,说明真的零外部引用:

```powershell
mkdir bin          # git 不跟踪空目录,全新 clone 得先建
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe `
  -nologo -noconfig -target:winexe -out:bin\Audiolite.exe Audiolite.cs
```

目标运行时是系统内置的 .NET Framework 4.8.1,不下载、不附带运行时。`gdiplus` / `WinForms` 全程不加载——这是内存能压到 10 MB 的原因。

测试(46 条,覆盖点击事件判定、命名编号、状态位映射、切换记账、淡出序列;条数以 `test.exe` 输出末尾为准):

```powershell
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc -nologo -noconfig -r:System.dll -target:exe -main:TestClicks -out:test.exe Audiolite.cs TestClicks.cs
.\test.exe
.\test.exe --leak     # 真机自检:180 次枚举后句柄增长须在 +12 内;且 active 视图不许混进别的状态、全量视图须与直接问 COM 的计数一致
```

编号与掩码语义这两处过不了纯函数测试(前者要求喂真到达时间,后者整个在 COM 调用里),所以用变异测试卡住:把排序键换成安装时间、把菜单的过滤去掉、把全量掩码从 0xF 退回 7,现在都会让测试失败。

## 已知边界

- **接入时间属性是未公开的。** 排序用的 `{194ef948-…},2` 不在微软文档化的属性列表里。实测行为与"最近一次接入时间"一致(内置扬声器停在装机日、蓝牙耳机停在上次连接日),且已排除"上次设为默认时间"这一候选(设为默认不刷新它)。**本机 16 个端点里只有 7 个带这个属性**,取不到的那些按安装时间排——回退路径才是多数路径。微软改语义的风险存在,但影响面只是菜单顺序,不影响切换正确性——真正用于切换的是 MMDevice ID。
- **切换用的是未公开的 `IPolicyConfig`。** 微软没有文档化这个接口,SDK 头文件里也找不到(`audiopolicy.h` 只有 `IAudioPolicy*`)。虚表槽位靠多个独立实现相互印证,并由行为验证:每次调用后回读默认设备,不一致就判失败。真正用于切换的是 MMDevice ID,那部分是公开 API。
- **只切"控制台"和"多媒体"两个角色,不切"通信"。** 这和 Windows 自带音量浮窗的行为一致。显式按通信角色取设备的程序(Teams、YY 一类)输出不会跟着切。
- **编号会随连接情况变化。** 这是"按在线设备重排"的必然结果:拔掉一台,后面的编号会前移。
- **菜单不过滤虚拟音频设备。** 录屏、回环一类虚拟端点只要处于 active 就会出现在菜单里,需自行忽略。
- **异常落盘。** `WndProc` 里的异常不静默吞掉,会记到 exe 同目录的 `diag.txt`,便于事后定位。

## 许可证

MIT。详见 [LICENSE](LICENSE)。
