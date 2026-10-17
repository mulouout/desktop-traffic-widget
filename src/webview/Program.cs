// ============================================================================
// Program.cs —— 桌面流量监控插件（任务栏挂件）
//
// 技术路线：WinForms 薄壳 + WebView2，把汽水音乐任务栏挂件的原始 HTML/CSS 原样搬进来，
//   实现外观的像素级复刻；C# 负责流量采集、任务栏吸附、透明与拖拽。
//
// 外观规格（DIP，与汽水音乐 taskbarWidget variant1 完全一致）：
//   挂件 248×40，容器圆角 8px，padding-inline 22px/12px，
//   封面位 28×28 圆角 4px，右侧按钮位 32×32，关闭位右上内缩 4px。
//   贴靠任务栏时近乎全透明，仅 hover 时浮现极淡底与描边。
// ============================================================================

using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TrafficWidgetWeb;

/// <summary>不抢焦点的无边框窗口（任务栏挂件不能把任务栏/桌面的焦点抢走）。</summary>
sealed class WidgetForm : Form
{
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000   // WS_EX_NOACTIVATE：点击不激活
                        | 0x00000008   // WS_EX_TOPMOST：浮在任务栏之上
                        | 0x00000080   // WS_EX_TOOLWINDOW：不进 Alt+Tab
                        | 0x00200000;  // WS_EX_NOREDIRECTIONBITMAP：DComp 直接合成（透明关键，勿与 LAYERED/色键混用）
            return cp;
        }
    }
}

static class Program
{
    // ---- 外观尺寸（DIP），对应原版 variant1 ----
    const int WIDGET_W_DIP = 248;
    const int WIDGET_H_DIP = 40;

    const int NET_INTERVAL_MS = 500;    // 采样 / 推送间隔
    const int ATTACH_INTERVAL_MS = 2000;// 任务栏重附着检查间隔

    // Island（灵动岛）模式：展开高度（DIP），收起用 TaskbarHost.ISLAND_COLLAPSE_DIP（32）
    const int ISLAND_EXPAND_H_DIP = 80;
    // 显示模式持久化：exe 同级 settings.json（绿色便携，不碰注册表/C 盘）
    static readonly string SETTINGS_PATH = Path.Combine(AppContext.BaseDirectory, "settings.json");

    // 开机自启：HKCU\...\Run（每用户，无需管理员权限）
    const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RUN_VAL = "TrafficWidget";

    static WidgetForm _form;
    static WebView2 _web;
    static Mutex _mutex;
    static NetMonitor _net;
    static TaskbarHost _host;
    static ContextMenuStrip _menu;
    static bool _contentReady;

    [STAThread]
    static void Main()
    {
        // 单实例：同一用户下只允许一个挂件
        _mutex = new Mutex(true, "TrafficWidget_" + Environment.UserName, out bool createdNew);
        if (!createdNew) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 先探测任务栏，让窗口一开始就落在正确的显示器/DPI 上下文里
        bool probed = TaskbarHost.Probe(out IntPtr taskbar, out RECT tr, out double scale);

        // 初始显示模式：读上次持久化的选择（island=灵动岛 / taskbar=任务栏吸附）
        WidgetMode mode = LoadMode();

        int w = (int)Math.Round(WIDGET_W_DIP * scale);
        // 初始窗口高按模式取基准：Island=32（贴顶胶囊），Taskbar=40
        int h = (int)Math.Round((mode == WidgetMode.Island ? TaskbarHost.ISLAND_COLLAPSE_DIP : WIDGET_H_DIP) * scale);
        // Island：直接顶部居中贴屏幕顶边；Taskbar：左侧贴边、垂直居中于任务栏（与汽水音乐同位置）
        var start = mode == WidgetMode.Island
            ? new Point((Screen.PrimaryScreen.Bounds.Width - w) / 2, 0)
            : probed
                ? new Point(tr.Left, tr.Top + Math.Max(0, (tr.Height - h) / 2))
                : new Point(40, 40);

        var form = new WidgetForm
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = start,
            Size = new Size(w, h),
            BackColor = Color.Black,
            Text = "流量监控",
        };
        // 不设 TransparencyKey / WS_EX_LAYERED：透明由 NOREDIRECTIONBITMAP + DComp 合成实现
        _form = form;

        _net = new NetMonitor();
        _host = new TaskbarHost(form, WIDGET_W_DIP, WIDGET_H_DIP);
        if (mode != WidgetMode.Taskbar) _host.SetMode(mode);   // 走 SetMode 复位高度基准（Island=32）

        // 右键原生菜单
        _menu = new ContextMenuStrip();
        _menu.Items.Add("重置统计", null, (s, e) => _net.Reset());

        // 显示模式：互斥勾选，点击即切换 + 持久化 + 通知前端换肤
        var modeMenu = new ToolStripMenuItem("显示模式");
        var miTaskbar = new ToolStripMenuItem("任务栏吸附") { Checked = mode == WidgetMode.Taskbar };
        var miIsland = new ToolStripMenuItem("灵动岛（顶部）") { Checked = mode == WidgetMode.Island };
        miTaskbar.Click += (s, e) => SwitchMode(WidgetMode.Taskbar, miTaskbar, miIsland);
        miIsland.Click += (s, e) => SwitchMode(WidgetMode.Island, miTaskbar, miIsland);
        modeMenu.DropDownItems.Add(miTaskbar);
        modeMenu.DropDownItems.Add(miIsland);
        _menu.Items.Add(modeMenu);

        var autoItem = new ToolStripMenuItem("开机自启") { Checked = IsAutoStart() };
        autoItem.Click += (s, e) =>
        {
            bool on = !autoItem.Checked;
            SetAutoStart(on);
            autoItem.Checked = on;
        };
        _menu.Items.Add(autoItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (s, e) => Shutdown());

        var web = new WebView2 { Dock = DockStyle.Fill };
        _web = web;
        form.Controls.Add(web);

        // 采样并推送
        var netTimer = new System.Windows.Forms.Timer { Interval = NET_INTERVAL_MS };
        netTimer.Tick += (s, e) =>
        {
            _net.Sample();
            PushData();
        };

        // 任务栏重附着（explorer 重启后自愈）
        var attachTimer = new System.Windows.Forms.Timer { Interval = ATTACH_INTERVAL_MS };
        attachTimer.Tick += (s, e) => _host.Attach();

        form.Shown += async (s, e) =>
        {
            await InitWebViewAsync();
            _host.Attach();
            netTimer.Start();
            attachTimer.Start();
            PushTheme();
        };

        form.FormClosed += (s, e) => _menu.Dispose();

        Application.Run(form);
    }

    static async Task InitWebViewAsync()
    {
        // WebView2 用户数据目录放在 exe 同级，避免落到 C 盘
        string dataDir = Path.Combine(AppContext.BaseDirectory, "_webview2");
        Directory.CreateDirectory(dataDir);

        // 剥掉 WebView2 宿主窗口的 GDI 重定向表面，让 Chromium 的 DComp 视觉直接合成到屏幕
        long exStyle = Native.GetWindowLongPtr(_web.Handle, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(_web.Handle, Native.GWL_EXSTYLE,
            (IntPtr)(exStyle | Native.WS_EX_NOREDIRECTIONBITMAP));

        CoreWebView2Environment env;
        try
        {
            env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            // Win11 自带 WebView2 运行时；万一缺失，给出明确提示而不是静默崩溃
            MessageBox.Show(
                "无法初始化 WebView2 运行时，请先安装 Microsoft Edge WebView2 Runtime。\n\n" + ex.Message,
                "流量监控", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Shutdown();
            return;
        }

        var st = _web.CoreWebView2.Settings;
        st.AreDefaultContextMenusEnabled = false;
        st.IsZoomControlEnabled = false;
        st.IsStatusBarEnabled = false;
        st.AreDevToolsEnabled = false;
        st.IsPinchZoomEnabled = false;
        st.IsSwipeNavigationEnabled = false;

        _web.CoreWebView2.WebMessageReceived += OnWebMessage;
        // 让挂件背景真正透出任务栏（配合顶层 NOREDIRECTIONBITMAP 窗口）
        _web.DefaultBackgroundColor = Color.Transparent;
        _web.CoreWebView2.NavigateToString(WidgetHtml.Html);
    }

    static void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string type;
        double dx = 0, dy = 0, wd = 0;
        bool on = false;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            type = root.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            // 逐字段按类型取值：单个字段类型异常不连累整条消息被丢弃
            dx = GetNum(root, "dx"); dy = GetNum(root, "dy"); wd = GetNum(root, "w");
            if (root.TryGetProperty("on", out var vo))
                on = vo.ValueKind == JsonValueKind.True;
        }
        catch { return; }

        switch (type)
        {
            case "ready":
                _contentReady = true;
                PushTheme();
                PushMode();
                PushData();
                break;

            case "drag":
                _host.DragBy(dx, dy);
                break;

            case "resize":
                _host.SetWidthDip(wd);
                break;

            case "expand":
                // Island 模式 hover 展开/收起（on 已在上方统一解析）
                if (_host.Mode == WidgetMode.Island)
                    _host.SetHeightDip(on ? ISLAND_EXPAND_H_DIP : TaskbarHost.ISLAND_COLLAPSE_DIP);
                break;

            case "menu":
                _form.BeginInvoke(new Action(() => _menu.Show(Cursor.Position)));
                break;

            case "close":
                _form.BeginInvoke(new Action(Shutdown));
                break;
        }
    }

    /// <summary>从 JSON 根节点安全取数值字段：缺失或类型不符返回 0。</summary>
    static double GetNum(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    static void PushData()
    {
        if (!_contentReady || _web.CoreWebView2 == null) return;
        string json = JsonSerializer.Serialize(new
        {
            t = "data",
            dl = _net.Dl,
            ul = _net.Ul,
            dlTot = _net.DlTotal,
            ulTot = _net.UlTotal,
            pk = _net.PeakDl,
        });
        try { _web.CoreWebView2.PostWebMessageAsJson(json); } catch { }
    }

    static void PushMode()
    {
        if (!_contentReady || _web?.CoreWebView2 == null) return;
        try { _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { t = "mode", island = _host.Mode == WidgetMode.Island })); } catch { }
    }

    /// <summary>切换显示模式：宿主重定位 + 前端换肤 + 持久化 + 菜单勾选同步。</summary>
    static void SwitchMode(WidgetMode m, ToolStripMenuItem miTaskbar, ToolStripMenuItem miIsland)
    {
        _host.SetMode(m);
        miTaskbar.Checked = m == WidgetMode.Taskbar;
        miIsland.Checked = m == WidgetMode.Island;
        SaveMode(m);
        PushMode();
    }

    static WidgetMode LoadMode()
    {
        try
        {
            string s = File.ReadAllText(SETTINGS_PATH);
            using var doc = JsonDocument.Parse(s);
            if (doc.RootElement.TryGetProperty("mode", out var v) && v.GetString() == "island")
                return WidgetMode.Island;
        }
        catch { }
        return WidgetMode.Taskbar;
    }

    static void SaveMode(WidgetMode m)
    {
        try
        {
            File.WriteAllText(SETTINGS_PATH, "{\"mode\":\"" + (m == WidgetMode.Island ? "island" : "taskbar") + "\"}");
        }
        catch { }
    }

    static void PushTheme()
    {
        if (!_contentReady || _web.CoreWebView2 == null) return;
        bool light = false;
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = (k?.GetValue("SystemUsesLightTheme") as int?) == 1;   // 任务栏跟随此项
        }
        catch { }
        try { _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { t = "theme", light })); } catch { }
    }

    /// <summary>是否已配置开机自启（注册表 Run 项存在即视为已开启）。</summary>
    static bool IsAutoStart()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RUN_KEY);
            return k?.GetValue(RUN_VAL) != null;
        }
        catch { return false; }
    }

    /// <summary>
    /// 开关开机自启：写/删 HKCU Run 值。
    /// 路径取 Environment.ProcessPath——单文件发布下 Assembly.Location 为空，只有它才是真实 exe 路径；
    /// 值整体加引号，防止路径含空格时被 Run 拆参。
    /// </summary>
    static void SetAutoStart(bool on)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RUN_KEY);
            if (on)
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                k.SetValue(RUN_VAL, "\"" + exe + "\"");
            }
            else
            {
                k.DeleteValue(RUN_VAL, false);
            }
        }
        catch { }
    }

    static void Shutdown()
    {
        try { _web?.Dispose(); } catch { }
        try { _form?.Close(); } catch { }
        Application.Exit();
    }
}
