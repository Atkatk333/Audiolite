using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Audiolite")]
[assembly: AssemblyProduct("Audiolite")]
[assembly: AssemblyCompany("Atkatk333")]
[assembly: AssemblyCopyright("Copyright \u00A9 2026 Atkatk333")]
[assembly: AssemblyVersion("0.5.0.0")]
[assembly: AssemblyFileVersion("0.5.0.0")]

// 0 依赖托盘版:只用 user32 / gdi32 / shell32,不引用 WinForms 与 System.Drawing,
// 因此 gdiplus 全程不加载。托盘图标取系统音量程序里的图标。
internal static class Audiolite
{
    // ---------------------------------------------------------------- 音频 COM

    const int eRender = 0;
    const int eConsole = 0, eMultimedia = 1;
    // 取值是 mmdeviceapi.h 的 EDEVICE_STATE_TYPE:2 是 DISABLED、8 才是 UNPLUGGED,
    // 全量掩码是 0xF。
    internal const uint Active = 1, Disabled = 2, NotPresent = 4, Unplugged = 8;
    internal const uint AllStates = 0xF;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class CMMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient cb);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient cb);
    }

    // 顺序严格照 mmdeviceapi.h;错一个槽位原生端就会调到别的方法上。
    // [ComImport] 不能省:这是本文件唯一需要被"实现"而不是被调用的接口,少了它
    // CCW 不对外声明这个 IID(实测 QI 返回 E_NOINTERFACE),而 mmdevapi 注册时只存
    // 裸指针、不做 QI 也不报错,于是回调落到 ToString/Equals 槽位上,永远不响。
    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint newState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropertyKey key);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint ctx, IntPtr activationParams, out IntPtr iface);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    // 非公开接口,SDK 头文件里没有(audiopolicy.h 只有 IAud*Polic*)。槽位由下面
    // 这个声明顺序决定:SetDefaultEndpoint 是第 11 个方法(不算 IUnknown 那 3 个,
    // 落在 vtable 第 13 格)。顺序与 AudioConfig / DesktopManager / SoundSwitch 等
    // 独立实现一致,并由行为验证 —— 调用后回读默认设备确实变了(见 TrySwitch)。
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, bool def, IntPtr a);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a, IntPtr b);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, bool def, IntPtr a, IntPtr b);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, bool fx, IntPtr a, IntPtr b);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, bool fx, IntPtr a, IntPtr b);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, bool visible);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigClient { }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant
    {
        public ushort vt; public ushort r1, r2, r3;
        public IntPtr p; public int d1, d2;
    }

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

    internal class Entry
    {
        public string Id;
        public string Name;
        public string Category;
        public string Desc;
        public DateTime Install;
        public DateTime Arrive;
        public int Rank;
        public int Total;
        public uint State;

        // 排序键:优先用接入时间,拿不到时退回安装时间,保证顺序总是确定的。
        public DateTime Order { get { return Arrive == DateTime.MaxValue ? Install : Arrive; } }

        // 本机的接入时间属性只覆盖一部分端点,所以"顺序看着不对"有两种完全不同的
        // 成因。--list 里不写明这点,排查时就只能猜。
        internal string SortKind { get { return Arrive == DateTime.MaxValue ? "install" : "arrive"; } }
    }

    // 惰性创建:启动阶段音频服务还没就绪时,不该在 CLI 诊断分支之前就抛异常。
    static IMMDeviceEnumerator en;
    static IPolicyConfig policy;

    static IMMDeviceEnumerator En()
    {
        if (en == null) en = (IMMDeviceEnumerator)Activator.CreateInstance(typeof(CMMDeviceEnumerator));
        return en;
    }

    static readonly Guid DevProp = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
    static readonly Guid DescProp = new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc");
    static readonly Guid InstallProp = new Guid("83da6326-97a6-4088-9453-a1923f573b29");
    // 未公开属性:无官方名称。实测表现为"端点最近一次接入的时间"(本机证据:
    // 有线耳机 11:47 = 今天插上,蓝牙耳机A 17:26 = 刚刚连接)。语义属推断,非文档保证。
    static readonly Guid ArriveProp = new Guid("194ef948-7cdb-403e-9f47-19418f7b24fd");

    static string Str(IPropertyStore store, Guid fmtid, uint pid)
    {
        PropertyKey key = new PropertyKey { fmtid = fmtid, pid = pid };
        PropVariant pv;
        if (store.GetValue(ref key, out pv) != 0) return "";
        try { return pv.vt == 31 ? (Marshal.PtrToStringUni(pv.p) ?? "") : ""; }
        finally { PropVariantClear(ref pv); }
    }

    static DateTime FileTime(IPropertyStore store, Guid fmtid, uint pid)
    {
        PropertyKey key = new PropertyKey { fmtid = fmtid, pid = pid };
        PropVariant pv;
        if (store.GetValue(ref key, out pv) != 0) return DateTime.MaxValue;
        try { return pv.vt == 64 ? SafeFileTime(pv.p.ToInt64()) : DateTime.MaxValue; }
        finally { PropVariantClear(ref pv); }
    }

    // 属性存在但值为 0 时 FILETIME 是 1601-01-01,那是个合法排序键,会把这台机器
    // 顶到组内第一并且不编号;越界值则让 FromFileTime 直接抛异常。
    internal static DateTime SafeFileTime(long ticks)
    {
        if (ticks <= 0 || ticks > DateTime.MaxValue.ToFileTime()) return DateTime.MaxValue;
        return DateTime.FromFileTime(ticks);
    }

    // Windows 自带的 "2-" 前缀只反映设备接口描述冲突,与"同类别有几台"无关
    // (真机上 4 个"耳机"里只有 1 个被编号),所以先丢掉它,自己按安装顺序编号。
    internal static string StripWinPrefix(string desc)
    {
        int dash = desc.IndexOf("- ", StringComparison.Ordinal);
        if (dash <= 0) return desc;
        for (int i = 0; i < dash; i++)
            if (desc[i] < '0' || desc[i] > '9') return desc;
        string rest = desc.Substring(dash + 2);
        return rest.Length == 0 ? desc : rest;
    }

    // 同类别内按接入先后编号:最早一台不编号,其余 2、3、4……
    internal static string Label(string category, string desc, int rank, int total)
    {
        if (category.Length == 0) return desc;
        if (desc.Length == 0) return category;
        if (total < 2 || rank <= 1) return category + " (" + desc + ")";
        return category + " " + rank + " (" + desc + ")";
    }

    internal static int ByOrder(Entry x, Entry y)
    {
        int c = x.Order.CompareTo(y.Order);
        if (c != 0) return c;
        c = x.Install.CompareTo(y.Install);
        return c != 0 ? c : string.Compare(x.Id, y.Id, StringComparison.Ordinal);
    }

    internal static void AssignRanks(List<Entry> shown)
    {
        foreach (Entry a in shown)
        {
            List<Entry> group = shown.FindAll(e => string.Equals(e.Category, a.Category, StringComparison.Ordinal));
            group.Sort(ByOrder);
            a.Total = group.Count;
            a.Rank = group.FindIndex(e => e.Id == a.Id) + 1;
        }
    }


    // 只负责枚举出原始字段,不编号、不拼名字。
    // 编号在 Render() 里按"这一批要显示出来的设备"算。
    // 供回归测试调用的枚举入口:走的是和菜单/提示完全相同的枚举路径。
    internal static int EnumOnce()
    {
        return Render(Active).Count;
    }

    // 枚举出来的 COM 接口都是 RCW,不显式释放就要等 GC 终结器才还句柄
    // (实测 60 次枚举堆到 885 个句柄,强制 GC 才回落到 287)。
    static void Rel(object com)
    {
        if (com != null) Marshal.ReleaseComObject(com);
    }

    // 属性库打不开多半是第三方进程内属性插件抛的,不是端点本身的问题。原先这种设备
    // 被直接丢掉:既不进菜单也不进 --list,零提示 —— 比多一行裸 ID 难查得多。
    // 每台只报一次(这条在枚举热路径上,不去重会把 diag.txt 写爆)。
    // 用 List 不用 HashSet:后者在 System.Core 里,引了就不再是零外部引用。
    static readonly List<string> nameless = new List<string>();

    static List<Entry> All(uint stateMask)
    {
        var list = new List<Entry>();
        IMMDeviceCollection coll;
        if (En().EnumAudioEndpoints(eRender, stateMask, out coll) != 0) return list;
        try
        {
            uint n;
            coll.GetCount(out n);
            for (uint i = 0; i < n; i++)
            {
                IMMDevice dev;
                if (coll.Item(i, out dev) != 0) continue;
                try
                {
                    string id; dev.GetId(out id);
                    uint state; dev.GetState(out state);
                    string category = "", desc = "";
                    DateTime install = DateTime.MaxValue, arrive = DateTime.MaxValue;
                    IPropertyStore ps;
                    int hrStore = dev.OpenPropertyStore(0, out ps);
                    if (hrStore == 0)
                    {
                        category = Str(ps, DevProp, 2);
                        desc = StripWinPrefix(Str(ps, DescProp, 6));
                        install = FileTime(ps, InstallProp, 100);
                        arrive = FileTime(ps, ArriveProp, 2);
                        Rel(ps);
                    }
                    // 描述为空就一定得有可辨认的东西:ID 丑,但两台都显示"耳机"就分不出来了。
                    if (desc.Length == 0)
                    {
                        if (!nameless.Contains(id))
                        {
                            nameless.Add(id);
                            Diag("no name for " + id + " (OpenPropertyStore hr=0x" + hrStore.ToString("X8") + "), showing raw ID");
                        }
                        desc = id;
                    }
                    list.Add(new Entry
                    {
                        Id = id,
                        State = state,
                        Category = category,
                        Desc = desc,
                        Install = install,
                        Arrive = arrive
                    });
                }
                finally { Rel(dev); }
            }
        }
        finally { Rel(coll); }
        return list;
    }

    // 编号和排列都只按"这一批要显示出来的设备"算:连几台就编到几,
    // 谁先接入谁排前面,不留空洞。
    internal static List<Entry> Number(List<Entry> batch)
    {
        batch.Sort(ByOrder);
        AssignRanks(batch);
        foreach (Entry e in batch) e.Name = Label(e.Category, e.Desc, e.Rank, e.Total);
        return batch;
    }

    // 掩码直接下传给 EnumAudioEndpoints:菜单只要 3 台 active 时,就不该为了
    // 另外 7 台去开属性库(每次都要跨进第三方音频属性插件一趟)。
    internal static List<Entry> Render(uint stateMask)
    {
        return Number(All(stateMask));
    }

    // 不经本程序任何加工、直接问 COM 的端点数:给测试当独立参照。
    // 掩码在这里是写死的字面量,所以"AllStates 被改回 7"这类退化瞒不过去。
    internal static int CountRaw(uint stateMask)
    {
        IMMDeviceCollection coll;
        if (En().EnumAudioEndpoints(eRender, stateMask, out coll) != 0) return -1;
        uint n;
        coll.GetCount(out n);
        Rel(coll);
        return (int)n;
    }

    static string DefaultId(int role)
    {
        IMMDevice dev;
        if (En().GetDefaultAudioEndpoint(eRender, role, out dev) != 0) return null;
        string id;
        dev.GetId(out id);
        Rel(dev);
        return id;
    }

    // exe 旁边优先(绿色程序的本分);不可写时退到 %LOCALAPPDATA%\Audiolite。
    // state.txt 与 diag.txt 共用这一份顺序,否则"日志写不进同一处"这种坑会自己长出来。
    static IEnumerable<string> DataDirs()
    {
        yield return AppDomain.CurrentDomain.BaseDirectory;
        string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local)) yield return Path.Combine(local, "Audiolite");
    }

    static string diagDir;   // 只缓存"证明写得动"的那个目录;失败不缓存,否则目录后来变得可写就再也写不进

    // 记账:两处都写不进时不抛出去 —— 记不下来事情,不该把调用方一起带下水。
    // 选址必须用真正的写入去试:只问"目录能不能创建"是错的 —— 一个存在但只读的
    // 目录(Program Files、U 盘、网络盘,或 diag.txt 被占住)永远返回"能",
    // 于是日志被静默丢掉。实测过这个形状:回退不生效,两处都没有 diag.txt。
    static void Diag(string s)
    {
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine;
        if (diagDir != null && TryWrite(diagDir, line)) return;
        foreach (string dir in DataDirs())
        {
            if (TryWrite(dir, line)) { diagDir = dir; return; }
        }
    }

    static bool TryWrite(string dir, string line)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "diag.txt"), line);
            return true;
        }
        catch (Exception) { return false; }
    }

    // 记忆落盘:否则每次重启后左键都只能退回菜单。
    // 读:取两处里最新的那份 —— 只按顺序取第一份,会在 exe 目录转为只读后
    // 永远读出旧值,新历史静默回退。
    // 写:两处都写。只写"第一个能写的"会让两份文件从此分道扬镳。
    // "确实没有上一台"必须能写进文件。原先写空串,而读取端跳过空文件 ——
    // 清掉的记忆会被另一处那份旧的非空值在 mtime 竞赛里顶回来,下次启动无声复活。
    const string NoLast = "none";

    static void LoadLast()
    {
        string newest = null;
        bool found = false;
        DateTime stamp = DateTime.MinValue;
        foreach (string dir in DataDirs())
        {
            string p = Path.Combine(dir, "state.txt");
            try
            {
                if (!File.Exists(p)) continue;
                string s = File.ReadAllText(p).Trim();
                if (s.Length == 0) continue;
                DateTime t = File.GetLastWriteTimeUtc(p);
                if (t <= stamp) continue;
                stamp = t;
                newest = s == NoLast ? null : s;
                found = true;
            }
            catch (Exception e) { Diag("LoadLast " + p + " -> " + e.GetType().Name + ": " + e.Message); }
        }
        if (found) lastId = newest;
    }

    // 托盘与 --set 是两个进程,固定名的 state.txt.tmp 会互相顶掉。
    static readonly string tmpTag = Guid.NewGuid().ToString("N").Substring(0, 8);

    static void SaveLast()
    {
        string text = lastId ?? NoLast;
        foreach (string dir in DataDirs())
        {
            string p = Path.Combine(dir, "state.txt");
            string tmp = p + "." + tmpTag + ".tmp";
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, text);
                if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
                SweepTmp(dir, tmp);
            }
            catch (Exception e)
            {
                Diag("SaveLast " + p + " -> " + e.GetType().Name + ": " + e.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
            }
        }
    }

    // 随机 tmp 名换来"不互相顶掉",代价是崩溃留下的文件没人收;每次成功写完顺手扫掉。
    static void SweepTmp(string dir, string mine)
    {
        try
        {
            foreach (string f in Directory.GetFiles(dir, "state.txt.*.tmp"))
            {
                if (string.Equals(f, mine, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(f); } catch (Exception) { }
            }
        }
        catch (Exception) { }
    }

    internal enum TrayAction { None, Toggle, Menu }

    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

    internal static TrayAction Classify(uint low, ulong now)
    {
        TrayAction a = TrayAction.None;
        if (low == WM_LBUTTONUP || low == WM_MBUTTONUP) a = TrayAction.Toggle;
        else if (low == WM_RBUTTONUP) a = TrayAction.Menu;

        // 移动/按下/双击一律不占用去抖窗口:它们与抬起只差 0~1ms,
        // 一旦在此处记下时间戳,真正的抬起就会被下面的去抖吞掉。
        if (a == TrayAction.None) return TrayAction.None;

        // 视觉双击会被 shell 拆成 抬起+双击+抬起,去抖让它只算一次切换。
        // 用 64 位tick:32 位的 Environment.TickCount 约 24.8 天回绕一次,
        // 回绕瞬间 now - lastClickTick 变负,那之后的首次点击会被吞掉。
        if (now - lastClickTick < 250) return TrayAction.None;
        lastClickTick = now;
        return a;
    }

    internal static void ResetClicks() { lastClickTick = 0; }

    static string seenDefault;

    internal static bool SameId(string a, string b)
    {
        return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // 外部换了设备(Windows 音量浮窗、某个应用独占切换)时,"上一台"必须跟着改,
    // 否则它会停在当前这台设备上,左键从此变成一次"弹成功横幅的空操作",
    // 而且没有任何自愈路径。返回应当记住的那一台;不改时原样返回。
    internal static string NextLast(string remembered, string seen, string now)
    {
        if (now == null) return remembered;
        if (seen != null && !SameId(seen, now)) return seen;
        // 记住的那台就是当前这台:这笔账没有意义,清掉它,左键退回菜单。
        // 继承来的 state.txt 完全可能是这种形状(v0.5 的写法会留下它),不清掉的话
        // 左键就永远只是弹菜单,而 diag.txt 里一个字都没有。
        if (SameId(remembered, now)) return null;
        return remembered;
    }

    static Entry Last()
    {
        if (lastId == null) return null;
        return Render(Active).Find(e => SameId(e.Id, lastId));
    }

    internal enum SwitchOutcome { Switched, AlreadyCurrent, Failed }

    // 判定的全部 inputs 就是这四个值,提成纯函数:C3 那条回归("两个 HRESULT 都
    // 成功、默认设备其实没换")否则只能在真音频硬件上测。
    internal static SwitchOutcome Decide(string current, string targetId, bool hrOk, string readBack)
    {
        if (SameId(current, targetId)) return SwitchOutcome.AlreadyCurrent;
        if (!hrOk || !SameId(readBack, targetId)) return SwitchOutcome.Failed;
        return SwitchOutcome.Switched;
    }

    // 执行切换并记账,不碰 UI:命令行和托盘共用这一份,别各写一套账。
    static SwitchOutcome TrySwitch(Entry target, out string current)
    {
        current = DefaultId(eConsole);
        if (SameId(current, target.Id)) return SwitchOutcome.AlreadyCurrent;   // 先判再动,一次写入都不发

        int hrConsole, hrMultimedia;
        bool hrOk = SetDefault(target, out hrConsole, out hrMultimedia);

        // 回读而不是相信返回值:AudioEndpointBuilder 重启、端点在枚举之后消失,
        // 都会让"两个 HRESULT 都成功但默认设备其实没换"成为可能。
        string now = DefaultId(eConsole);
        if (now == null) now = DefaultId(eConsole);   // 写成功后一次瞬时 RPC 失败不该直接判失败
        SwitchOutcome o = Decide(current, target.Id, hrOk, now);
        if (o == SwitchOutcome.Failed)
            Diag("switch " + target.Id + " hr=" + Hex(hrConsole) + "/" + Hex(hrMultimedia)
                 + " nowDefault=" + (now ?? "null(未确认)"));
        else if (o == SwitchOutcome.Switched)
        {
            // 记账只在确认成功之后:失败时把 lastId 写成当前设备,就把左键锁死了。
            if (current != null) { lastId = current; SaveLast(); }
            seenDefault = target.Id;
        }
        return o;
    }

    static string Hex(int hr) { return "0x" + hr.ToString("X8"); }

    static void ApplySwitch(Entry target, bool fallBackToMenu)
    {
        string current;
        SwitchOutcome o = TrySwitch(target, out current);
        if (o == SwitchOutcome.AlreadyCurrent)
        {
            // 目标已经是当前默认。这时候弹一张成功横幅就是骗人。
            if (fallBackToMenu) ShowMenu();
            else ShowBanner("已经是:" + target.Name);
            return;
        }
        ShowBanner(o == SwitchOutcome.Switched ? target.Name : "切换失败:" + target.Name);
    }

    static bool SetDefault(Entry target, out int hrConsole, out int hrMultimedia)
    {
        if (policy == null) policy = (IPolicyConfig)new PolicyConfigClient();
        hrConsole = policy.SetDefaultEndpoint(target.Id, eConsole);
        hrMultimedia = policy.SetDefaultEndpoint(target.Id, eMultimedia);
        return hrConsole == 0 && hrMultimedia == 0;
    }

    // ---------------------------------------------------------------- Win32

    const uint WM_DESTROY = 0x0002, WM_TIMER = 0x0113, WM_PAINT = 0x000F;
    const uint WM_TRAY = 0x0401, WM_REFRESH = 0x0402;
    const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_MBUTTONUP = 0x0208;
    static ulong lastClickTick;

    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const uint NIFY_VERSION_4 = 4;

    const uint WS_POPUP = 0x80000000;
    const uint WS_EX_TOPMOST = 0x0008;
    const uint WS_EX_TOOLWINDOW = 0x0080;
    const uint WS_EX_LAYERED = 0x00080000;
    const uint WS_EX_NOACTIVATE = 0x08000000;
    const uint WS_EX_TRANSPARENT = 0x00000020;

    const uint WM_WINDOWPOSCHANGING = 0x0046, WM_ACTIVATE = 0x0006;
    const uint SWP_NOACTIVATE = 0x0010;

    const uint CS_HREDRAW = 0x0002, CS_VREDRAW = 0x0001;

    const int SW_SHOWNOACTIVATE = 4, SW_HIDE = 0;
    const int DT_CENTER = 0x0001, DT_VCENTER = 0x0004, DT_SINGLELINE = 0x0020;
    const int DT_LEFT = 0x0000, DT_CALCRECT = 0x0400, DT_END_ELLIPSIS = 0x8000;
    const int TRANSPARENT = 1;
    const uint LWA_ALPHA = 2;
    const uint RDW_INVALIDATE = 0x1, RDW_UPDATENOW = 0x100, RDW_ALLCHILDREN = 0x80;
    const int TIMER_HIDE = 1, TIMER_FADE = 2;
    const uint TPM_RIGHTBUTTON = 0x0002, TPM_RETURNCMD = 0x0100;
    const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_CHECKED = 8;
    const int MENU_QUIT = 9999;
    const int BannerMs = 1600;
    const int BannerRadius = 12;
    const byte BannerAlpha = 240;
    const uint BANNER_BG = 0x201C1C;   // COLORREF BBGGRR = RGB(28,28,32)

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hWnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore; public bool fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgb; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEXW
    {
        public uint cbSize; public uint style; public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATAW
    {
        public int cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPOS { public IntPtr hwnd; public IntPtr hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] static extern IntPtr LoadIconW(IntPtr hInst, IntPtr name);
    const int IDI_APPLICATION = 32512;
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern int GetMessageW(out MSG m, IntPtr hWnd, uint first, uint last);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int h2, bool repaint);
    [DllImport("user32.dll")] static extern bool SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h, IntPtr lprc, IntPtr hrgn, uint flags);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr cb);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr h, IntPtr id);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetMonitorInfoW(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr id, string text);
    [DllImport("user32.dll")] static extern bool CheckMenuItem(IntPtr menu, uint id, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("shell32.dll", SetLastError = true)] static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW pnid);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint ExtractIconExW(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, SM_CXSMICON = 49;

    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    // FillRect 在 user32,不在 gdi32——放错就 EntryPointNotFoundException,
    // 异常被 catch 后消息落回 DefWindowProc,窗口被擦成空白。
    [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref RECT r, IntPtr brush);
    [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawTextW(IntPtr dc, string text, int len, ref RECT r, uint flags);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontW(int height, int width, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

    // ---------------------------------------------------------------- 状态

    static NOTIFYICONDATAW nid;
    static IntPtr trayHwnd, bannerHwnd, hIcon;
    static bool iconOwned;
    static WndProc trayProc, bannerProc;
    static uint wmTaskbarCreated;
    static string lastId;
    static string bannerText = "";
    static byte bannerAlpha = 255;
    static bool bannerShown;
    static bool bannerClamped;
    static string lastBannerGeom = "";
    static int lastBannerW, lastBannerH;
    static float dpiScale = 1f;

    sealed class Watcher : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string id, uint newState) { Ping(); return 0; }
        public int OnDeviceAdded(string id) { Ping(); return 0; }
        public int OnDeviceRemoved(string id) { Ping(); return 0; }
        public int OnDefaultDeviceChanged(int flow, int role, string id) { if (flow == eRender) Ping(); return 0; }
        // IDL 里 key 是 REFPROPERTYKEY(指针),按值声明会收到垃圾数据;这条回调
        // 现在只是忽略,所以错了无害 —— 但那是"这行不读参数"换来的无害。
        public int OnPropertyValueChanged(string id, ref PropertyKey key) { return 0; }
        static void Ping() { if (trayHwnd != IntPtr.Zero) PostMessageW(trayHwnd, WM_REFRESH, IntPtr.Zero, IntPtr.Zero); }
    }

    static Watcher watcher;

    // ---------------------------------------------------------------- 托盘窗口

    static IntPtr TrayWnd(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_TRAY)
            {
                // TrackPopupMenuEx 是模态的,但它照样派发 WM_TRAY。菜单开着时再点一次
                // 会当场改设备,而菜单里那批 targets/勾选项是打开瞬间的快照,松手后
                // 会按已失效的下标去派发。
                if (inMenu) return IntPtr.Zero;
                switch (Classify((uint)(lParam.ToInt64() & 0xFFFF), GetTickCount64()))
                {
                    case TrayAction.Toggle:
                        Entry prev = Last();
                        if (prev != null) ApplySwitch(prev, true);
                        else ShowMenu();
                        break;
                    case TrayAction.Menu:
                        ShowMenu();
                        break;
                }
                return IntPtr.Zero;
            }
            if (msg == WM_REFRESH) { OnRefresh(); return IntPtr.Zero; }
            // explorer 重启会把所有托盘图标清掉,并广播这条消息;不重注册的话
            // 图标就没了,而进程还活着——再双击会被单实例互斥静默挡掉。
            if (wmTaskbarCreated != 0 && msg == wmTaskbarCreated) { AddTrayIcon(); UpdateTip(); return IntPtr.Zero; }
            if (msg == WM_DESTROY) { PostQuitMessage(0); return IntPtr.Zero; }
        }
        catch (Exception e) { Diag("TRAY WNDPROC " + MsgName(msg) + " threw: " + e.GetType().Name + ": " + e.Message); }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // 设备事件的唯一落点:先把历史对正,再刷 tooltip。
    static void OnRefresh()
    {
        string now = DefaultId(eConsole);
        string next = NextLast(lastId, seenDefault, now);
        if (!string.Equals(next, lastId, StringComparison.Ordinal))
        {
            if (next == null) Diag("discarding history: remembered device is the current default (" + now + ")");
            lastId = next;
            SaveLast();
        }
        if (now != null) seenDefault = now;
        UpdateTip();
    }

    static void UpdateTip()
    {
        string current = DefaultId(eConsole);
        Entry active = Render(Active).Find(e => string.Equals(e.Id, current, StringComparison.OrdinalIgnoreCase));
        nid.uFlags = NIF_TIP;
        nid.szTip = active == null ? "切换音频输出" : Trim(active.Name, 60);
        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    // 菜单文本里单个 & 是助记符(B&O 会显示成 BO 并吃掉一次 Alt),要翻倍。
    // 必须在 Trim 之后做,否则截断可能把成对的 && 劈开。
    internal static string MenuEsc(string s) { return s.Replace("&", "&&"); }

    // TrackPopupMenuEx 自己跑一个模态消息循环:菜单开着时的第二次托盘点击会
    // 嵌套进第二个菜单,250ms 去抖挡不住这个(它挡的是同一次点击的重复抬起)。
    static bool inMenu;

    static void ShowMenu()
    {
        if (inMenu) return;
        inMenu = true;
        try { TrackMenu(); }
        finally { inMenu = false; }
    }

    static void TrackMenu()
    {
        List<Entry> targets = Render(Active);
        string current = DefaultId(eConsole);
        IntPtr menu = CreatePopupMenu();
        for (int i = 0; i < targets.Count; i++)
        {
            AppendMenuW(menu, MF_STRING, new IntPtr(i + 1), MenuEsc(Trim(targets[i].Name, 60)));
            if (SameId(targets[i].Id, current))
                CheckMenuItem(menu, (uint)(i + 1), MF_CHECKED);
        }
        AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);
        AppendMenuW(menu, MF_STRING, new IntPtr(MENU_QUIT), "退出");

        POINT p;
        GetCursorPos(out p);
        SetForegroundWindow(trayHwnd);
        int cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, p.x, p.y, trayHwnd, IntPtr.Zero);
        PostMessageW(trayHwnd, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd == MENU_QUIT) PostMessageW(trayHwnd, WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
        else if (cmd >= 1 && cmd <= targets.Count) ApplySwitch(targets[cmd - 1], false);
    }

    // ---------------------------------------------------------------- 横幅

    static string MsgName(uint msg)
    {
        if (msg == WM_PAINT) return "WM_PAINT";
        if (msg == WM_TIMER) return "WM_TIMER";
        if (msg == WM_DESTROY) return "WM_DESTROY";
        return "0x" + msg.ToString("X4");
    }

    // 淡出的下一步:当前 alpha -> 下一帧 alpha。0 表示该隐藏了。
    // 只能单调变暗:在 SW_HIDE 之前把 alpha 调回满值,会让即将消失的
    // 横幅闪回全亮一下(DWM 赶得上合成就能看见)。
    internal static byte NextFade(byte current)
    {
        return current <= 24 ? (byte)0 : (byte)(current - 24);
    }

    static IntPtr BannerWnd(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            // 不抢焦点要四层,缺任何一层都会在某个改几何的瞬间漏出去:
            // WS_EX_NOACTIVATE + SW_SHOWNOACTIVATE 管住显示时,
            // 下面两条管住"改位置/被激活"时。
            if (msg == WM_WINDOWPOSCHANGING)
            {
                WINDOWPOS wp = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
                wp.flags |= SWP_NOACTIVATE;
                Marshal.StructureToPtr(wp, lParam, false);
                return IntPtr.Zero;
            }
            if (msg == WM_ACTIVATE) return IntPtr.Zero;
            if (msg == WM_PAINT) { PaintBanner(hWnd); return IntPtr.Zero; }
            if (msg == WM_TIMER)
            {
                if (wParam.ToInt32() == TIMER_HIDE)
                {
                    KillTimer(hWnd, wParam);
                    // 诊断模式没有标题栏、又是 WS_POPUP,收不到 Alt+F4:让它自己退出,
                    // 否则 --bannertest 会留下一个只能杀进程的常驻窗口。
                    if (bannerSticky) { PostQuitMessage(0); return IntPtr.Zero; }
                    // 定不到时器就立刻藏起来:一条永远不会消失的置顶横幅,比闪一下更糟。
                    if (SetTimer(hWnd, new IntPtr(TIMER_FADE), 40, IntPtr.Zero) == IntPtr.Zero)
                    {
                        Diag("fade SetTimer failed, gle=" + Marshal.GetLastWin32Error() + "; hiding at once");
                        HideBanner();
                    }
                }
                else if (wParam.ToInt32() == TIMER_FADE)
                {
                    bannerAlpha = NextFade(bannerAlpha);
                    SetLayeredWindowAttributes(hWnd, 0, bannerAlpha, LWA_ALPHA);
                    if (bannerAlpha == 0)
                    {
                        KillTimer(hWnd, wParam);
                        ShowWindow(hWnd, SW_HIDE);
                        bannerShown = false;
                    }
                }
                return IntPtr.Zero;
            }
        }
        catch (Exception e) { Diag("BANNER WNDPROC " + MsgName(msg) + " threw: " + e.GetType().Name + ": " + e.Message); }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    static void PaintBanner(IntPtr hWnd)
    {
        PAINTSTRUCT ps;
        IntPtr dc = BeginPaint(hWnd, out ps);
        RECT rc;
        GetClientRect(hWnd, out rc);

        // 圆角由 SetWindowRgn 裁剪,这里整块填充即可,不再需要色键抠角。
        IntPtr bg = CreateSolidBrush(BANNER_BG);
        FillRect(dc, ref rc, bg);
        DeleteObject(bg);

        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, 0xFFFFFF);

        IntPtr font = SelectObject(dc, BannerFont());
        RECT probe = new RECT();
        DrawTextW(dc, bannerText, -1, ref probe, DT_LEFT | DT_SINGLELINE | DT_CALCRECT);
        // 夹取过就一定"装不下",那是设计而非故障;只在没夹的时候报裁切。
        if (!bannerClamped && (probe.right - probe.left > rc.right || probe.bottom - probe.top > rc.bottom))
            Diag("BANNER CLIPPED text=" + (probe.right - probe.left) + "x" + (probe.bottom - probe.top)
                 + " client=" + rc.right + "x" + rc.bottom + " \"" + bannerText + "\"");
        RECT text = new RECT { left = 0, top = 0, right = rc.right, bottom = rc.bottom };
        DrawTextW(dc, bannerText, -1, ref text, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        SelectObject(dc, font);
        EndPaint(hWnd, ref ps);
    }

    // 藏掉横幅并清状态。定不到定时器时的兜底 —— 一条永不消失的置顶不透明块,
    // 比完全没有反馈更糟。
    static void HideBanner()
    {
        ShowWindow(bannerHwnd, SW_HIDE);
        bannerShown = false;
    }

    static void ShowBanner(string text)
    {
        // 文本可能是 I8 退化出来的 62 字符裸 ID,不截会顶出一条横跨屏幕的横幅。
        bannerText = Trim(text, 60);
        if (bannerHwnd == IntPtr.Zero) { Diag("bannerHwnd is NULL - window creation failed"); return; }

        POINT p;
        GetCursorPos(out p);
        IntPtr mon = MonitorFromPoint(p, 0);
        MONITORINFO mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        RECT work;
        if (!GetMonitorInfoW(mon, ref mi) || mi.rcWork.right <= mi.rcWork.left)
        {
            // 失败时 mi 整块是全零,算出来是负坐标 —— 横幅会跑到屏幕外,
            // 表现为"切换了但什么都没弹"。退回主屏尺寸,至少看得见。
            Diag("GetMonitorInfo failed, gle=" + Marshal.GetLastWin32Error() + "; using primary screen");
            work = new RECT { left = 0, top = 0, right = GetSystemMetrics(SM_CXSCREEN), bottom = GetSystemMetrics(SM_CYSCREEN) };
        }
        else work = mi.rcWork;   // 必须在调用之后读:调用前读会拿到全零的 mi(踩过一次)

        // 量的必须就是要画的那一串:截断作用在 bannerText 上,测量若还用未截断的
        // text,窗口会按长文字开、按短文字画,而下面那条裁切自检对被截的那类
        // 输入永远不响。
        IntPtr dc = GetDC(bannerHwnd);
        IntPtr oldFont = SelectObject(dc, BannerFont());
        RECT need = new RECT();
        DrawTextW(dc, bannerText, -1, ref need, DT_LEFT | DT_SINGLELINE | DT_CALCRECT);
        SelectObject(dc, oldFont);
        ReleaseDC(bannerHwnd, dc);

        int pad = (int)(18 * dpiScale);
        int needW = (need.right - need.left) + pad * 2;
        int h = (need.bottom - need.top) + pad * 2;
        // 夹取按像素,不按字符数:60 个汉字在 150% 下就是 1700 多 px,字符界根本兜不住。
        // 超过工作区 60% 就夹,绘制侧用 DT_END_ELLIPSIS 收尾 —— 截断由同一个 API 负责,
        // 不会出现"按像素夹了、按字符画"的错位。
        int maxW = (int)((work.right - work.left) * 0.6);
        bannerClamped = maxW > pad * 4 && needW > maxW;
        int w = bannerClamped ? maxW : needW;
        int x = work.right - w - (int)(24 * dpiScale);
        int y = work.bottom - h - (int)(24 * dpiScale);
        lastBannerGeom = "workW=" + (work.right - work.left) + " workH=" + (work.bottom - work.top)
                         + " need=" + needW + " max=" + maxW + " clamped=" + bannerClamped
                         + " at=" + x + "," + y;

        // 残影根因:在窗口可见的状态下改尺寸,改完到同步重绘之间,DWM 作为独立
        // 合成线程可能把"新几何 + 旧像素"这一帧贴出来。所以尺寸没变就完全不动
        // 几何(耳机/扬声器这类等宽切换根本不进这个分支);必须变时先隐藏。
        if (w != lastBannerW || h != lastBannerH)
        {
            if (bannerShown) ShowWindow(bannerHwnd, SW_HIDE);
            int radius = (int)(BannerRadius * dpiScale) * 2;
            SetWindowRgn(bannerHwnd, CreateRoundRectRgn(0, 0, w, h, radius, radius), true);
            lastBannerW = w;
            lastBannerH = h;
        }
        MoveWindow(bannerHwnd, x, y, w, h, false);
        bannerShown = true;
        bannerAlpha = BannerAlpha;
        SetLayeredWindowAttributes(bannerHwnd, 0, bannerAlpha, LWA_ALPHA);
        ShowWindow(bannerHwnd, SW_SHOWNOACTIVATE);
        // 同步重绘:异步 InvalidateRect 要等消息队列空闲,切完会有延迟感。
        RedrawWindow(bannerHwnd, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);

        KillTimer(bannerHwnd, new IntPtr(TIMER_HIDE));
        KillTimer(bannerHwnd, new IntPtr(TIMER_FADE));
        if (!bannerSticky && SetTimer(bannerHwnd, new IntPtr(TIMER_HIDE), BannerMs, IntPtr.Zero) == IntPtr.Zero)
        {
            // 淡出的唯一触发者就是这个定时器,它定不到 = 置顶不透明块留到下一次切换。
            Diag("banner hide SetTimer failed, gle=" + Marshal.GetLastWin32Error() + "; 立刻转淡出");
            if (SetTimer(bannerHwnd, new IntPtr(TIMER_FADE), 40, IntPtr.Zero) == IntPtr.Zero)
            {
                Diag("banner fade SetTimer failed, gle=" + Marshal.GetLastWin32Error() + "; 直接隐藏");
                HideBanner();
            }
        }
    }

    // 托盘图标:槽位尺寸随 DPI 变,16 槽用 16 帧才不发虚;两个帧都拿不到时
    // 宁可退回系统应用图标,也不能挂一个"能点但看不见"的空句柄上去。
    static IntPtr LoadTrayIcon()
    {
        string root = Environment.GetEnvironmentVariable("SystemRoot");
        if (string.IsNullOrEmpty(root)) root = @"C:\Windows";
        IntPtr[] big = new IntPtr[1];
        IntPtr[] small = new IntPtr[1];
        uint got = ExtractIconExW(Path.Combine(root, @"System32\SndVol.exe"), 0, big, small, 1);
        if (got == 0) Diag("icon: ExtractIconExW found no frame in SndVol.exe");

        bool wantSmall = GetSystemMetrics(SM_CXSMICON) <= 16;
        IntPtr h = wantSmall ? small[0] : big[0];
        IntPtr unused = wantSmall ? big[0] : small[0];
        if (h == IntPtr.Zero) h = unused;
        else if (unused != IntPtr.Zero) DestroyIcon(unused);

        if (h == IntPtr.Zero)
        {
            // 共享句柄,退出时不能 DestroyIcon。
            h = LoadIconW(IntPtr.Zero, new IntPtr(IDI_APPLICATION));
            iconOwned = false;
            Diag("icon: fell back to IDI_APPLICATION");
            return h;
        }
        iconOwned = true;
        return h;
    }

    static IntPtr RegisterClasses()
    {
        IntPtr hInst = GetModuleHandleW(null);
        trayProc = TrayWnd;
        bannerProc = BannerWnd;

        WNDCLASSEXW wc = new WNDCLASSEXW();
        wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEXW));
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(trayProc);
        wc.hInstance = hInst;
        wc.lpszClassName = "AudioliteTray";
        if (RegisterClassExW(ref wc) == 0) Diag("RegisterClassExW AudioliteTray failed, gle=" + Marshal.GetLastWin32Error());

        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(bannerProc);
        wc.lpszClassName = "AudioliteBanner";
        // 缺这两个标志时,窗口改尺寸只重绘新暴露区域,旧文字像素留在原地 -> 残影。
        wc.style = CS_HREDRAW | CS_VREDRAW;
        wc.hbrBackground = IntPtr.Zero;
        if (RegisterClassExW(ref wc) == 0) Diag("RegisterClassExW AudioliteBanner failed, gle=" + Marshal.GetLastWin32Error());
        return hInst;
    }

    static bool bannerSticky;

    // 诊断用:让横幅常驻不消失,好被截图观察渲染质量。
    static int BannerTest(string text)
    {
        uint dpi = GetDpiForSystem();
        dpiScale = dpi == 0 ? 1f : dpi / 96f;
        CreateBannerWindow(RegisterClasses());
        bannerSticky = true;
        ShowBanner(text);
        if (SetTimer(bannerHwnd, new IntPtr(TIMER_HIDE), 5000, IntPtr.Zero) == IntPtr.Zero)
        {
            // 定不到就藏掉直接退出:这个模式没有标题栏,兜底的 5 秒自杀是唯一的出口。
            Diag("bannertest SetTimer failed, gle=" + Marshal.GetLastWin32Error() + "; exiting");
            HideBanner();
            return 3;
        }
        Diag("BANNERTEST dpi=" + dpi + " scale=" + dpiScale + " screen=" + ScreenW() + "x" + ScreenH()
             + " " + lastBannerGeom + " window=" + lastBannerW + "x" + lastBannerH
             + " text=\"" + text + "\"");
        MSG m;
        while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref m);
            DispatchMessageW(ref m);
        }
        return 0;
    }

    static int ScreenW()
    {
        RECT r; GetWindowRect(GetDesktopWindow(), out r); return r.right - r.left;
    }

    static int ScreenH()
    {
        RECT r; GetWindowRect(GetDesktopWindow(), out r); return r.bottom - r.top;
    }

    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();

    static void CreateBannerWindow(IntPtr hInst)
    {
        bannerHwnd = CreateWindowExW(
            // WS_EX_TRANSPARENT:不加它,横幅显示的 1.6 秒里落在右下角那块矩形上
            // 的点击会被一个顶层窗口吃掉——无边框窗口游戏时那块正是游戏画面。
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            "AudioliteBanner", null, WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
    }

    static IntPtr bannerFont;

    static IntPtr BannerFont()
    {
        if (bannerFont == IntPtr.Zero)
            bannerFont = CreateFontW(-(int)(13 * dpiScale * 96 / 72), 0, 0, 0, 700, 0, 0, 0,
                134 /*GB2312_CHARSET*/, 0, 0, 5 /*CLEARTYPE_QUALITY*/, 0, "Microsoft YaHei");
        return bannerFont;
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

    // ---------------------------------------------------------------- 入口

    static string Trim(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max);
    }

    // 诊断:把端点属性库里所有 FILETIME(64) 类型的时间戳打出来,
    // 用来判断"按接入顺序编号"到底有没有可用数据源。
    static int Props()
    {
        foreach (Entry e in Render(AllStates))
        {
            Console.WriteLine("=== " + e.Name + "  [state=" + e.State + "]");
            IMMDevice dev;
            if (En().GetDevice(e.Id, out dev) != 0) continue;
            IPropertyStore ps;
            if (dev.OpenPropertyStore(0, out ps) != 0) { Rel(dev); continue; }
            uint c;
            ps.GetCount(out c);
            for (uint i = 0; i < c; i++)
            {
                PropertyKey key;
                if (ps.GetAt(i, out key) != 0) continue;
                PropVariant pv;
                if (ps.GetValue(ref key, out pv) != 0) continue;
                ushort vt = pv.vt;
                try
                {
                    if (vt == 64)
                        Console.WriteLine("    TIME " + key.fmtid + "," + key.pid + " = "
                            + DateTime.FromFileTime(pv.p.ToInt64()).ToString("yyyy-MM-dd HH:mm:ss"));
                    else if (vt == 31 && (key.pid == 2 || key.pid == 6))
                        Console.WriteLine("    STR  " + key.fmtid + "," + key.pid + " = "
                            + (Marshal.PtrToStringUni(pv.p) ?? ""));
                }
                finally { PropVariantClear(ref pv); }
            }
            Rel(ps);
            Rel(dev);
        }
        return 0;
    }

    // 打印右键菜单实际会显示的内容(只含 active 端点,编号按这一批算)。
    static int Menu()
    {
        string def = DefaultId(eConsole);
        foreach (Entry e in Render(Active))
            Console.WriteLine("{0}\t{1}", SameId(e.Id, def) ? "*" : " ", e.Name);
        return 0;
    }

    internal static string StateName(uint state)
    {
        if (state == Active) return "active";
        if (state == Disabled) return "disabled";
        if (state == NotPresent) return "not-present";
        if (state == Unplugged) return "unplugged";
        return "0x" + state.ToString("X");
    }

    static int List()
    {
        string def = DefaultId(eConsole);
        Console.WriteLine("#\tstate\t排序键\tID\t菜单名");
        foreach (Entry e in Render(AllStates))
            Console.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}", SameId(e.Id, def) ? "*" : " ", StateName(e.State),
                              e.SortKind, e.Id, e.Name);
        return 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateMutexW(IntPtr attr, bool owner, string name);
    const uint ERROR_ALREADY_EXISTS = 183;

    static IntPtr mutex;

    // 托盘模式只允许一个实例:否则自启 + 手动双击会起两个图标,
    // 两个进程各自持有托盘图标并互相抢设备变更回调。
    // CLI 模式(--list/--menu/--set)不受限,方便运行时诊断。
    static bool AlreadyRunning()
    {
        mutex = CreateMutexW(IntPtr.Zero, false, "Local\\AudioliteTray");
        // 裸 GetLastError() P/Invoke 不保证还是这次调用留下的错误码,必须走
        // SetLastError=true + Marshal.GetLastWin32Error() 这条约定。
        return Marshal.GetLastWin32Error() == ERROR_ALREADY_EXISTS;
    }

    static int Main(string[] args)
    {
        // 启动期抛出来的一律落盘:这是 GUI 子系统进程,默认只会弹一个裸的 .NET
        // 错误框,每次登录弹一次,而 diag.txt 里什么都不会留。
        try { return Dispatch(args); }
        catch (Exception e)
        {
            Diag("STARTUP " + e.GetType().Name + ": " + e.Message);
            return 3;
        }
    }

    static int Dispatch(string[] args)
    {
        SetProcessDPIAware();

        // 显式分派,不再靠"谁都不匹配就落到最后"。原先 --tray 在源码里根本没出现,
        // README 教的自启参数只是碰巧能用;打错字也会静默起一个托盘。
        if (args.Length == 0) return AlreadyRunning() ? 0 : RunTray();

        switch (args[0])
        {
            // 多余的参数一律拒:静默忽略等于让"--list --set x"这种写法看起来成功了。
            case "--tray":  return args.Length == 1 ? (AlreadyRunning() ? 0 : RunTray()) : Usage("--tray 不接受额外参数");
            case "--list":  return args.Length == 1 ? List()  : Usage("--list 不接受额外参数");
            case "--menu":  return args.Length == 1 ? Menu()  : Usage("--menu 不接受额外参数");
            case "--props": return args.Length == 1 ? Props() : Usage("--props 不接受额外参数");
            case "--help":  return args.Length == 1 ? (Help0()) : Usage("--help 不接受额外参数");
            case "--version":
                if (args.Length != 1) return Usage("--version 不接受额外参数");
                Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version.ToString());
                return 0;
            case "--set": return args.Length == 2 ? Set(args[1]) : Usage("--set 需要一个 ID");
            case "--bannertest": return args.Length == 2 ? BannerTest(args[1]) : Usage("--bannertest 需要一个文本");
        }
        return Usage("未知参数 " + args[0]);
    }

    static int Help0() { Help(); return 0; }

    static int Usage(string why)
    {
        Console.Error.WriteLine("Audiolite: " + why);
        Help();
        return 2;
    }

    static void Help()
    {
        Console.WriteLine("用法: Audiolite [--tray] [选项]");
        Console.WriteLine("  (无参数)     托盘模式,左键回切上一台、右键列设备(单实例)");
        Console.WriteLine("  --list       全部渲染端点及其状态,当前默认标 *");
        Console.WriteLine("  --menu       右键菜单会显示的内容");
        Console.WriteLine("  --props      端点属性原始值(排查命名用)");
        Console.WriteLine("  --set <ID>   直接设为指定设备(会真的改声音输出)");
        Console.WriteLine("  --bannertest <文本>  只画一次横幅,5 秒后自动退出");
        Console.WriteLine("  --version    打印版本号");
    }

    static int Set(string id)
    {
        // 先读盘:CLI 进程内存里的 lastId 是空的,不读就会拿"空"去覆盖托盘刚写好的历史。
        LoadLast();
        foreach (Entry e in Render(AllStates))
        {
            if (!SameId(e.Id, id)) continue;
            string current;
            SwitchOutcome o = TrySwitch(e, out current);
            if (o == SwitchOutcome.Failed)
            {
                Console.Error.WriteLine("切换失败,细节见 diag.txt:" + e.Name);
                return 1;
            }
            Console.WriteLine((o == SwitchOutcome.AlreadyCurrent ? "已经是: " : "已切换: ") + e.Name);
            return 0;
        }
        Console.Error.WriteLine("找不到端点 ID " + id + ",完整列表见 --list");
        return 2;
    }

    static void AddTrayIcon()
    {
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        nid.szTip = "切换音频输出";
        if (!Shell_NotifyIconW(NIM_ADD, ref nid))
            Diag("Shell_NotifyIcon NIM_ADD failed, gle=" + Marshal.GetLastWin32Error());
        // 不设 uVersion:经典布局下 lParam 低 16 位保证是鼠标消息。
        // 设成 NOTIFYICON_VERSION_4 后该位置变成鼠标坐标,事件类型挪到 wParam 高位。
    }

    static int RunTray()
    {
        uint dpi = GetDpiForSystem();
        dpiScale = dpi == 0 ? 1f : dpi / 96f;
        if (dpi == 0) Diag("GetDpiForSystem returned 0, banner falls back to 100%");

        wmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        if (wmTaskbarCreated == 0) Diag("RegisterWindowMessage(TaskbarCreated) failed, gle=" + Marshal.GetLastWin32Error());

        IntPtr hInst = RegisterClasses();

        // 必须是真实的顶层窗口(而非 HWND_MESSAGE),否则无法成为前台窗口,
        // 弹出的菜单收不到"点击别处"的关闭通知,会脱离托盘面板赖在屏幕上。
        trayHwnd = CreateWindowExW(WS_EX_TOOLWINDOW, "AudioliteTray", "Audiolite", WS_POPUP,
            -32000, -32000, 0, 0, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        if (trayHwnd == IntPtr.Zero)
        {
            Diag("tray CreateWindowExW failed, gle=" + Marshal.GetLastWin32Error() + "; aborting");
            return 3;
        }

        CreateBannerWindow(hInst);

        hIcon = LoadTrayIcon();
        nid = new NOTIFYICONDATAW();
        nid.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATAW));
        nid.hWnd = trayHwnd;
        nid.uCallbackMessage = WM_TRAY;
        nid.hIcon = hIcon;
        AddTrayIcon();

        watcher = new Watcher();
        int reg = En().RegisterEndpointNotificationCallback(watcher);
        if (reg != 0) Diag("RegisterEndpointNotificationCallback -> 0x" + reg.ToString("X8"));
        LoadLast();
        OnRefresh();   // 顺带把 seenDefault 种成当前默认,外部切换才有参照

        MSG m;
        while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref m);
            DispatchMessageW(ref m);
        }

        nid.uFlags = 0;
        Shell_NotifyIconW(NIM_DELETE, ref nid);
        int unreg = En().UnregisterEndpointNotificationCallback(watcher);
        if (unreg != 0) Diag("UnregisterEndpointNotificationCallback -> 0x" + unreg.ToString("X8"));
        if (iconOwned && hIcon != IntPtr.Zero) DestroyIcon(hIcon);
        if (bannerFont != IntPtr.Zero) DeleteObject(bannerFont);
        return 0;
    }
}
