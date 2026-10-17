// ============================================================================
// TaskbarHost.cs —— 任务栏吸附（顶层窗口方案，与汽水音乐同机制）
//   不再 SetParent 到 Shell_TrayWnd：跨进程子窗口会切断 WebView2 的输入路由（实测证伪）。
//   改为顶层窗口（WS_EX_TOPMOST + WS_EX_NOREDIRECTIONBITMAP）浮在任务栏之上，
//   定时用屏幕坐标重新定位，任务栏移动 / explorer 重启后自愈。
//   窗口按 DIP 设计（248×40），实际像素 = DIP × (DPI/96)。
//   双显示模式：Taskbar=贴任务栏左侧（现状）；Island=顶部居中「灵动岛」黑胶囊，
//   hover 高度 40→88 DIP 弹性展开（SetHeightDip 插值动画），仅水平拖拽。
// ============================================================================

using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TrafficWidgetWeb;

[StructLayout(LayoutKind.Sequential)]
struct RECT
{
    public int Left, Top, Right, Bottom;
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

static class Native
{
    public const string TASKBAR_PRIMARY = "Shell_TrayWnd";
    public const string TASKBAR_SECONDARY = "Shell_SecondaryTrayWnd";

    public const int SWP_NOACTIVATE = 0x0010;
    public const int SWP_SHOWWINDOW = 0x0040;
    public const int SWP_NOZORDER = 0x0004;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string cls, string win);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, int flags);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);
}

/// <summary>挂件显示模式。</summary>
public enum WidgetMode { Taskbar, Island }

sealed class TaskbarHost
{
    readonly Form _form;
    int _wDip;                // 挂件宽度（DIP），由前端按内容测量后动态更新
    readonly int _hDip;

    double _scale = 1.0;      // DPI / 96
    int _taskW, _taskH;       // 任务栏像素尺寸
    RECT _tray;               // 任务栏屏幕矩形
    double _offsetDip = 0;    // Taskbar：距任务栏左边距离；Island：距顶部居中的水平偏移（DIP）
    double _hCurDip;          // 当前窗口高度（DIP），Island 展开动画的实时值

    // Island 高度动画参数：~150ms outCubic 插值
    System.Windows.Forms.Timer _animTimer;
    double _hFromDip, _hToDip;
    int _animTick;
    const int ANIM_TOTAL_MS = 150, ANIM_STEP_MS = 15;

    /// <summary>Island 收起态高度（DIP），与 CSS .island-row 的 32px 行高对齐。</summary>
    public const int ISLAND_COLLAPSE_DIP = 32;

    /// <summary>显示模式：任务栏吸附 / 顶部灵动岛。</summary>
    public WidgetMode Mode { get; set; } = WidgetMode.Taskbar;

    /// <summary>切换显示模式：停掉进行中的高度动画、复位偏移与高度后重新摆放。</summary>
    public void SetMode(WidgetMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        _offsetDip = 0;
        _animTimer?.Stop();                    // 防止动画 Timer 在切模式后继续改高度
        // 高度基准按模式复位：Island=32（贴顶胶囊），Taskbar=构造传入的 40
        _hFromDip = _hToDip = _hCurDip = mode == WidgetMode.Island ? ISLAND_COLLAPSE_DIP : _hDip;
        Apply();
    }

    public TaskbarHost(Form form, int wDip, int hDip)
    {
        _form = form;
        _wDip = wDip;
        _hDip = hDip;
        _hCurDip = hDip;
    }

    /// <summary>探测主任务栏句柄、矩形与 DPI。找不到时返回 false。</summary>
    public static bool Probe(out IntPtr hwnd, out RECT rect, out double scale)
    {
        hwnd = Native.FindWindow(Native.TASKBAR_PRIMARY, null);
        if (hwnd == IntPtr.Zero) hwnd = Native.FindWindow(Native.TASKBAR_SECONDARY, null);
        rect = default;
        scale = 1.0;
        if (hwnd == IntPtr.Zero) return false;
        if (!Native.GetWindowRect(hwnd, out rect)) return false;

        uint dpi = Native.GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        scale = dpi / 96.0;
        return true;
    }

    /// <summary>附着到任务栏并摆好位置。定时调用即可自愈（explorer 重启后重新贴合）。</summary>
    public void Attach()
    {
        if (_form == null || _form.IsDisposed || !_form.IsHandleCreated) return;
        if (!Probe(out _, out RECT r, out double scale)) return;

        _scale = scale;
        _tray = r;
        _taskW = r.Width;
        _taskH = r.Height;

        Apply();
    }

    /// <summary>按当前模式摆放窗口（屏幕坐标，保持置顶）。Taskbar=贴任务栏，Island=贴屏幕顶居中。</summary>
    void Apply()
    {
        // 窗口销毁后动画 Timer 可能还有一帧回调，先挡住避免 ObjectDisposedException
        if (_form == null || _form.IsDisposed || !_form.IsHandleCreated) return;
        int w = (int)Math.Round(_wDip * _scale);
        int h = (int)Math.Round(_hCurDip * _scale);
        int x, y;

        if (Mode == WidgetMode.Island)
        {
            var b = Screen.PrimaryScreen.Bounds;
            x = b.Left + (b.Width - w) / 2 + (int)Math.Round(_offsetDip * _scale);
            ClampIsland(x, w, out x);
            y = b.Top;                                        // 贴屏幕顶边，与刘海无缝
        }
        else
        {
            ClampOffset();
            x = _tray.Left + (int)Math.Round(_offsetDip * _scale);
            y = _tray.Top + Math.Max(0, (_taskH - h) / 2);    // 垂直居中于任务栏
        }

        Native.SetWindowPos(_form.Handle, Native.HWND_TOPMOST, x, y, w, h,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    /// <summary>Island：窗口不许拖出主屏左右边界。</summary>
    void ClampIsland(int x, int w, out int clamped)
    {
        var b = Screen.PrimaryScreen.Bounds;
        clamped = x;
        if (clamped < b.Left) clamped = b.Left;
        if (clamped + w > b.Right) clamped = b.Right - w;
    }

    void ClampOffset()
    {
        if (_taskW <= 0) return;
        double maxDip = (_taskW / _scale) - _wDip;
        if (maxDip < 0) maxDip = 0;
        if (_offsetDip < 0) _offsetDip = 0;
        if (_offsetDip > maxDip) _offsetDip = maxDip;
    }

    /// <summary>
    /// 拖拽：入参为 CSS 像素（即 DIP）的屏幕位移，仅沿任务栏水平移动。
    /// </summary>
    public void DragBy(double dxDip, double dyDip)
    {
        _offsetDip += dxDip;   // 两模式均仅水平方向生效（Island 的 y 恒贴顶）
        Apply();
    }

    /// <summary>Island 展开/收起：目标高度（DIP）动画插值；Taskbar 模式固定高度，直接忽略。</summary>
    public void SetHeightDip(double hDip)
    {
        if (Mode != WidgetMode.Island) return;
        if (!double.IsFinite(hDip)) return;    // 前端异常上报防护
        int v = (int)Math.Round(hDip);
        if (v < ISLAND_COLLAPSE_DIP) v = ISLAND_COLLAPSE_DIP;
        if (v > 120) v = 120;
        if (v == (int)Math.Round(_hToDip) && _animTimer != null) return;
        _hFromDip = _hCurDip; _hToDip = v; _animTick = 0;

        if (_animTimer == null)
        {
            _animTimer = new System.Windows.Forms.Timer { Interval = ANIM_STEP_MS };
            _animTimer.Tick += (s, e) =>
            {
                _animTick++;
                double p = Math.Min(1.0, _animTick * ANIM_STEP_MS / (double)ANIM_TOTAL_MS);
                double ease = 1 - Math.Pow(1 - p, 3);            // outCubic：起步快、落点柔
                _hCurDip = _hFromDip + (_hToDip - _hFromDip) * ease;
                Apply();
                if (p >= 1) { _hCurDip = _hToDip; _animTimer.Stop(); Apply(); }
            };
        }
        _animTimer.Stop();
        _animTimer.Start();
    }

    /// <summary>内容自适应宽度：前端测量后上报（DIP），钳制在合理范围并重新摆放窗口。</summary>
    public void SetWidthDip(double wDip)
    {
        if (!double.IsFinite(wDip)) return;    // 前端异常上报防护
        int v = (int)Math.Round(wDip);
        if (v < 108) v = 108;    // 下限取汽水音乐 nano 宽度
        if (v > 720) v = 720;    // 上限防止异常内容把窗口拉爆
        if (v == _wDip) return;
        _wDip = v;
        Apply();
    }
}
