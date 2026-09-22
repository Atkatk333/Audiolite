using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Audiolite")]
[assembly: AssemblyProduct("Audiolite")]
[assembly: AssemblyVersion("0.5.0.0")]
[assembly: AssemblyFileVersion("0.5.0.0")]

// 0 依赖托盘版:只用 user32 / gdi32 / shell32,不引用 WinForms 与 System.Drawing,
// 因此 gdiplus / DWrite 全程不加载。图标是构建期生成的 speaker.ico。
internal static class Audiolite
{
    // ---------------------------------------------------------------- 音频 COM

    const int eRender = 0;
    const int eConsole = 0, eMultimedia = 1;
    const uint Active = 1, Unplugged = 2;
    const uint AllStates = 7;

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
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint newState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
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

    // SetDefaultEndpoint 必须在第 11 槽。
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
    }

    static IMMDeviceEnumerator en;
    static IPolicyConfig policy;

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
        try { return pv.vt == 64 ? DateTime.FromFileTime(pv.p.ToInt64()) : DateTime.MaxValue; }
        finally { PropVariantClear(ref pv); }
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
        if (en == null) en = (IMMDeviceEnumerator)Activator.CreateInstance(typeof(CMMDeviceEnumerator));
        return Render(Active).Count;
    }

    // 枚举出来的 COM 接口都是 RCW,不显式释放就要等 GC 终结器才还句柄
    // (实测 60 次枚举堆到 885 个句柄,强制 GC 才回落到 287)。
    static void Rel(object com)
    {
        if (com != null) Marshal.ReleaseComObject(com);
    }

    static List<Entry> All()
    {
        var list = new List<Entry>();
        IMMDeviceCollection coll;
        if (en.EnumAudioEndpoints(eRender, AllStates, out coll) != 0) return list;
        uint n;
        coll.GetCount(out n);
        for (uint i = 0; i < n; i++)
        {
            IMMDevice dev;
            if (coll.Item(i, out dev) != 0) continue;
            string id; dev.GetId(out id);
            uint state; dev.GetState(out state);
            string category = "", desc = "";
            DateTime install = DateTime.MaxValue, arrive = DateTime.MaxValue;
            IPropertyStore ps;
            if (dev.OpenPropertyStore(0, out ps) == 0)
            {
                category = Str(ps, DevProp, 2);
                desc = StripWinPrefix(Str(ps, DescProp, 6));
                install = FileTime(ps, InstallProp, 100);
                arrive = FileTime(ps, ArriveProp, 2);
                Rel(ps);
            }
            Rel(dev);
            if (category.Length == 0 && desc.Length == 0) continue;
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
        Rel(coll);
        return list;
    }

    // 编号和排列都只按"这一批要显示出来的设备"算:连几台就编到几,
    // 谁先接入谁排前面,不留空洞。
    static List<Entry> Render(uint stateMask)
    {
        var list = new List<Entry>();
        foreach (Entry e in All())
            if ((e.State & stateMask) != 0) list.Add(e);
        list.Sort(ByOrder);
        AssignRanks(list);
        foreach (Entry e in list) e.Name = Label(e.Category, e.Desc, e.Rank, e.Total);
        return list;
    }

    static string DefaultId(int role)
    {
        IMMDevice dev;
        if (en.GetDefaultAudioEndpoint(eRender, role, out dev) != 0) return null;
        string id;
        dev.GetId(out id);
        Rel(dev);
        return id;
    }

    // 记忆落盘:否则每次重启后左键都只能退回菜单。
    static string StatePath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "state.txt");
    }

    static void LoadLast()
    {
        try
        {
            if (File.Exists(StatePath()))
            {
                string s = File.ReadAllText(StatePath()).Trim();
                if (s.Length > 0) lastId = s;
            }
        }
        catch (Exception) { }
    }

    static void SaveLast()
    {
        try { File.WriteAllText(StatePath(), lastId ?? ""); }
        catch (Exception) { }
    }

    internal enum TrayAction { None, Toggle, Menu }

    internal static TrayAction Classify(uint low, int now)
    {
        TrayAction a = TrayAction.None;
        if (low == WM_LBUTTONUP || low == WM_MBUTTONUP) a = TrayAction.Toggle;
        else if (low == WM_RBUTTONUP) a = TrayAction.Menu;

        // 移动/按下/双击一律不占用去抖窗口:它们与抬起只差 0~1ms,
        // 一旦在此处记下时间戳,真正的抬起就会被下面的去抖吞掉。
        if (a == TrayAction.None) return TrayAction.None;

        // 视觉双击会被 shell 拆成 抬起+双击+抬起,去抖让它只算一次切换。
        if (now - lastClickTick < 250) return TrayAction.None;
        lastClickTick = now;
        return a;
    }

    internal static void ResetClicks() { lastClickTick = 0; }

    static Entry Last()
    {
        if (lastId == null) return null;
        return Render(Active).Find(e => string.Equals(e.Id, lastId, StringComparison.OrdinalIgnoreCase));
    }

    // 切到 target,并把"离开的那台"记成上一台,供左键回切。
    static void ApplySwitch(Entry target)
    {
        string old = DefaultId(eConsole);
        if (old != null && !string.Equals(old, target.Id, StringComparison.OrdinalIgnoreCase))
        {
            lastId = old;
            SaveLast();
        }
        SetDefault(target);
        ShowBanner(target.Name);
    }

    static void SetDefault(Entry target)
    {
        if (policy == null) policy = (IPolicyConfig)new PolicyConfigClient();
        policy.SetDefaultEndpoint(target.Id, eConsole);
        policy.SetDefaultEndpoint(target.Id, eMultimedia);
    }

    // ---------------------------------------------------------------- Win32

    const uint WM_DESTROY = 0x0002, WM_TIMER = 0x0113, WM_PAINT = 0x000F;
    const uint WM_TRAY = 0x0401, WM_REFRESH = 0x0402;
    const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_MBUTTONUP = 0x0208;
    static int lastClickTick;

    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const uint NIFY_VERSION_4 = 4;

    const uint WS_POPUP = 0x80000000;
    const uint WS_EX_TOPMOST = 0x0008;
    const uint WS_EX_TOOLWINDOW = 0x0080;
    const uint WS_EX_LAYERED = 0x00080000;
    const uint WS_EX_NOACTIVATE = 0x08000000;

    const uint CS_HREDRAW = 0x0002, CS_VREDRAW = 0x0001;

    const int SW_SHOWNOACTIVATE = 4, SW_HIDE = 0;
    const int DT_CENTER = 0x0001, DT_VCENTER = 0x0004, DT_SINGLELINE = 0x0020;
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
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
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

    delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
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
    [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr cb);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr h, IntPtr id);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr id, string text);
    [DllImport("user32.dll")] static extern bool CheckMenuItem(IntPtr menu, uint id, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("shell32.dll")] static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW pnid);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconExW(string file, int index, IntPtr[] large, IntPtr[] small, uint count);

    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    // FillRect 在 user32,不在 gdi32——放错就 EntryPointNotFoundException,
    // 异常被 catch 后消息落回 DefWindowProc,窗口被擦成空白。
    [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref RECT r, IntPtr brush);
    [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int len, out SIZE size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawTextW(IntPtr dc, string text, int len, ref RECT r, uint flags);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontW(int height, int width, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

    // ---------------------------------------------------------------- 状态

    static NOTIFYICONDATAW nid;
    static IntPtr trayHwnd, bannerHwnd, hIcon;
    static WndProc trayProc, bannerProc;
    static string lastId;
    static string bannerText = "";
    static byte bannerAlpha = 255;
    static bool bannerShown;
    static int lastBannerW, lastBannerH;
    static float dpiScale = 1f;

    sealed class Watcher : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string id, uint newState) { Ping(); return 0; }
        public int OnDeviceAdded(string id) { Ping(); return 0; }
        public int OnDeviceRemoved(string id) { Ping(); return 0; }
        public int OnDefaultDeviceChanged(int flow, int role, string id) { if (flow == eRender) Ping(); return 0; }
        public int OnPropertyValueChanged(string id, PropertyKey key) { return 0; }
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
                switch (Classify((uint)(lParam.ToInt64() & 0xFFFF), Environment.TickCount))
                {
                    case TrayAction.Toggle:
                        Entry prev = Last();
                        if (prev != null) ApplySwitch(prev);
                        else ShowMenu();
                        break;
                    case TrayAction.Menu:
                        ShowMenu();
                        break;
                }
                return IntPtr.Zero;
            }
            if (msg == WM_REFRESH) { UpdateTip(); return IntPtr.Zero; }
            if (msg == WM_DESTROY) { PostQuitMessage(0); return IntPtr.Zero; }
        }
        catch (Exception e) { Diag("TRAY WNDPROC " + MsgName(msg) + " threw: " + e.GetType().Name + ": " + e.Message); }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    static void UpdateTip()
    {
        string current = DefaultId(eConsole);
        Entry active = Render(Active).Find(e => string.Equals(e.Id, current, StringComparison.OrdinalIgnoreCase));
        nid.uFlags = NIF_TIP;
        nid.szTip = active == null ? "切换音频输出" : Trim(active.Name, 60);
        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    static void ShowMenu()
    {
        List<Entry> targets = Render(Active);
        string current = DefaultId(eConsole);
        IntPtr menu = CreatePopupMenu();
        for (int i = 0; i < targets.Count; i++)
        {
            AppendMenuW(menu, MF_STRING, new IntPtr(i + 1), Trim(targets[i].Name, 60));
            if (string.Equals(targets[i].Id, current, StringComparison.OrdinalIgnoreCase))
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
        else if (cmd >= 1 && cmd <= targets.Count) ApplySwitch(targets[cmd - 1]);
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
            if (msg == WM_PAINT) { PaintBanner(hWnd); return IntPtr.Zero; }
            if (msg == WM_TIMER)
            {
                if (wParam.ToInt32() == TIMER_HIDE)
                {
                    KillTimer(hWnd, wParam);
                    SetTimer(hWnd, new IntPtr(TIMER_FADE), 40, IntPtr.Zero);
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
        RECT text = new RECT { left = 0, top = 0, right = rc.right, bottom = rc.bottom };
        DrawTextW(dc, bannerText, -1, ref text, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        SelectObject(dc, font);
        EndPaint(hWnd, ref ps);
    }

    static void Diag(string s)
    {
        // 只记录异常路径:这次 FillRect/DrawTextW 归错 DLL,就是被静默 catch 藏住的。
        try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diag.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine); }
        catch (Exception) { }
    }

    static void ShowBanner(string text)
    {
        bannerText = text;
        if (bannerHwnd == IntPtr.Zero) { Diag("bannerHwnd is NULL - window creation failed"); return; }

        POINT p;
        GetCursorPos(out p);
        IntPtr mon = MonitorFromPoint(p, 0);
        MONITORINFO mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        GetMonitorInfoW(mon, ref mi);
        RECT work = mi.rcWork;

        IntPtr dc = GetDC(bannerHwnd);
        IntPtr oldFont = SelectObject(dc, BannerFont());
        SIZE sz;
        GetTextExtentPoint32W(dc, text, text.Length, out sz);
        SelectObject(dc, oldFont);
        ReleaseDC(bannerHwnd, dc);

        int pad = (int)(18 * dpiScale);
        int w = sz.cx + pad * 2;
        int h = sz.cy + pad * 2;
        int x = work.right - w - (int)(24 * dpiScale);
        int y = work.bottom - h - (int)(24 * dpiScale);

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
        if (!bannerSticky) SetTimer(bannerHwnd, new IntPtr(TIMER_HIDE), BannerMs, IntPtr.Zero);
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
        RegisterClassExW(ref wc);

        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(bannerProc);
        wc.lpszClassName = "AudioliteBanner";
        // 缺这两个标志时,窗口改尺寸只重绘新暴露区域,旧文字像素留在原地 -> 残影。
        wc.style = CS_HREDRAW | CS_VREDRAW;
        wc.hbrBackground = IntPtr.Zero;
        RegisterClassExW(ref wc);
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
        Diag("BANNERTEST dpi=" + dpi + " scale=" + dpiScale + " screen=" + ScreenW() + "x" + ScreenH());
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
            WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
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
            if (en.GetDevice(e.Id, out dev) != 0) continue;
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
            Console.WriteLine("{0}\t{1}", e.Id == def ? "*" : " ", e.Name);
        return 0;
    }

    static int List()
    {
        string def = DefaultId(eConsole);
        foreach (Entry e in Render(AllStates))
        {
            string st = e.State == Active ? "active" : (e.State == Unplugged ? "unplugged" : "not-present");
            Console.WriteLine("{0}\t{1}\t{2}\t{3}", e.Id == def ? "*" : " ", st, e.Id, e.Name);
        }
        return 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateMutexW(IntPtr attr, bool owner, string name);
    [DllImport("kernel32.dll")] static extern uint GetLastError();
    const uint ERROR_ALREADY_EXISTS = 183;

    static IntPtr mutex;

    // 托盘模式只允许一个实例:否则自启 + 手动双击会起两个图标,
    // 两个进程各自持有托盘图标并互相抢设备变更回调。
    // CLI 模式(--list/--menu/--set)不受限,方便运行时诊断。
    static bool AlreadyRunning()
    {
        mutex = CreateMutexW(IntPtr.Zero, false, "Local\\AudioliteTray");
        return GetLastError() == ERROR_ALREADY_EXISTS;
    }

    static int Main(string[] args)
    {
        SetProcessDPIAware();
        en = (IMMDeviceEnumerator)Activator.CreateInstance(typeof(CMMDeviceEnumerator));

        if (args.Length > 0 && args[0] == "--list") return List();
        if (args.Length > 0 && args[0] == "--version")
        {
            Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version.ToString());
            return 0;
        }
        if (args.Length > 0 && args[0] == "--props") return Props();
        if (args.Length > 0 && args[0] == "--menu") return Menu();
        if (args.Length == 2 && args[0] == "--bannertest") return BannerTest(args[1]);
        if (args.Length == 2 && args[0] == "--set")
        {
            foreach (Entry e in Render(AllStates))
                if (string.Equals(e.Id, args[1], StringComparison.OrdinalIgnoreCase)) { SetDefault(e); return 0; }
            return 1;
        }
        if (AlreadyRunning()) return 0;
        return RunTray();
    }

    static int RunTray()
    {
        uint dpi = GetDpiForSystem();
        dpiScale = dpi == 0 ? 1f : dpi / 96f;

        IntPtr hInst = RegisterClasses();

        // 必须是真实的顶层窗口(而非 HWND_MESSAGE),否则无法成为前台窗口,
        // 弹出的菜单收不到"点击别处"的关闭通知,会脱离托盘面板赖在屏幕上。
        trayHwnd = CreateWindowExW(WS_EX_TOOLWINDOW, "AudioliteTray", "Audiolite", WS_POPUP,
            -32000, -32000, 0, 0, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        CreateBannerWindow(hInst);

        // 图标回退到上一版:直接取系统音量程序的图标。
        IntPtr[] big = new IntPtr[1];
        IntPtr[] small = new IntPtr[1];
        ExtractIconExW(@"C:\Windows\System32\SndVol.exe", 0, big, small, 1);
        hIcon = big[0] != IntPtr.Zero ? big[0] : small[0];


        nid = new NOTIFYICONDATAW();
        nid.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATAW));
        nid.hWnd = trayHwnd;
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        nid.uCallbackMessage = WM_TRAY;
        nid.hIcon = hIcon;
        nid.szTip = "切换音频输出";
        Shell_NotifyIconW(NIM_ADD, ref nid);
        // 不设 uVersion:经典布局下 lParam 低 16 位保证是鼠标消息。
        // 设成 NOTIFYICON_VERSION_4 后该位置变成鼠标坐标,事件类型挪到 wParam 高位。

        watcher = new Watcher();
        en.RegisterEndpointNotificationCallback(watcher);
        LoadLast();
        UpdateTip();

        MSG m;
        while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref m);
            DispatchMessageW(ref m);
        }

        nid.uFlags = 0;
        Shell_NotifyIconW(NIM_DELETE, ref nid);
        en.UnregisterEndpointNotificationCallback(watcher);
        if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
        if (bannerFont != IntPtr.Zero) DeleteObject(bannerFont);
        return 0;
    }
}
