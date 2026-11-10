using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

class RinManager : Form {
    readonly Maintenance Repair = new Maintenance("repair");
    readonly Maintenance Keepalive = new Maintenance("keepalive");
    readonly Label Summary = new Label();
    readonly TabPage RepairTab = new TabPage("故障修复 · 已暂停");
    readonly TabPage KeepaliveTab = new TabPage("进程保活 · 已暂停");
    readonly System.Windows.Forms.Timer Monitor = new System.Windows.Forms.Timer();
    readonly UiButton StartAll = new UiButton();
    readonly UiButton ThemeSwitch = new UiButton();
    readonly UiButton StartupSwitch = new UiButton();
    readonly ToolTip StartupTip = new ToolTip();
    bool ExitConfirmed=false, AwaitingExit=false;

    internal RinManager(bool startAtLogon = false) {
        AutoScaleMode=AutoScaleMode.None;
        Text="小凛管家";
        Size=new Size(1180,850);MinimumSize=new Size(1080,780);
        StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Microsoft YaHei UI",10.8F);
        BackColor=Color.FromArgb(246,248,251);
        Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(14)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,78));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(layout);
        var header=new Panel {Dock=DockStyle.Fill};layout.Controls.Add(header,0,0);
        var title=new Label {Text="小凛管家",Font=new Font(Font.FontFamily,27.6F,FontStyle.Bold),ForeColor=Theme.Foreground,Dock=DockStyle.Left,Width=240,TextAlign=ContentAlignment.MiddleLeft};
        header.Controls.Add(title);
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Right,Width=630,Padding=new Padding(0,10,0,0)};
        header.Controls.Add(buttons);
        StartAll.Text="开启所有自动维护";StartAll.Width=270;StartAll.Height=48;StartAll.PrimaryAction=true;
        StartAll.Font=new Font(Font.FontFamily,14.4F,FontStyle.Bold);StartAll.BackColor=Color.FromArgb(30,115,183);StartAll.ForeColor=Color.White;StartAll.FlatStyle=FlatStyle.Flat;
        StartAll.Click+=delegate {Repair.StartWork();Keepalive.StartWork();RefreshSummary();};buttons.Controls.Add(StartAll);
        var pauseAll=new UiButton {Text="暂停所有",Width=145,Height=48};
        pauseAll.Click+=delegate {Repair.PauseWork();Keepalive.PauseWork();RefreshSummary();};buttons.Controls.Add(pauseAll);
        ThemeSwitch.Width=160;ThemeSwitch.Height=48;
        ThemeSwitch.Click+=delegate {Theme.Dark=!Theme.Dark;Theme.Save();ApplyPalette();};buttons.Controls.Add(ThemeSwitch);
        Summary.Tag="muted";
        var statusRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        Summary.Dock = DockStyle.Fill;
        Summary.TextAlign = ContentAlignment.MiddleLeft;
        statusRow.Controls.Add(Summary, 0, 0);
        StartupSwitch.Dock = DockStyle.Fill;
        StartupSwitch.Margin = new Padding(4, 3, 4, 3);
        StartupSwitch.Text = "开机自启 · 关";
        StartupSwitch.AccessibleName = "开机自启";
        StartupTip.SetToolTip(StartupSwitch, "登录 Windows 时打开 QQ 和麦麦，并开启自动修复和进程保活。");
        StartupSwitch.Click += ToggleStartup;
        statusRow.Controls.Add(StartupSwitch, 1, 0);
        layout.Controls.Add(statusRow, 0, 1);
        var tabs=new UiTabs {Dock=DockStyle.Fill};tabs.TabPages.Add(RepairTab);tabs.TabPages.Add(KeepaliveTab);layout.Controls.Add(tabs,0,2);
        Embed(Repair,RepairTab);Embed(Keepalive,KeepaliveTab);
        Monitor.Interval=1000;
        Monitor.Tick+=delegate {
            RefreshSummary();
            if(AwaitingExit&&!Repair.IsBusy&&!Keepalive.IsBusy)Close();
        };
        Shown += delegate {
            Repair.BeginEngine();
            Keepalive.BeginEngine();
            if (startAtLogon && !Theme.PreviewMode)
                StartAtLogon();
            Monitor.Start();
            RefreshSummary();
        };
        ApplyPalette();
        if (!Theme.PreviewMode)
            RefreshStartup();
        Disposed += delegate { StartupTip.Dispose(); };
        FormClosing+=delegate(object sender,FormClosingEventArgs e){
            if(!ExitConfirmed && e.CloseReason==CloseReason.UserClosing){
                var answer=Theme.ConfirmClose(this);
                if(answer!=DialogResult.Yes){e.Cancel=true;return;}
                ExitConfirmed=true;
            }
            Repair.PauseWork();Keepalive.PauseWork();
            if(Repair.IsBusy||Keepalive.IsBusy){AwaitingExit=true;e.Cancel=true;Summary.Text="已暂停后续维护，当前操作完成后自动关闭。";}
        };
    }

    void StartAtLogon()
    {
        const string qq = @"C:\Program Files\Tencent\QQNT\QQ.exe";
        const string launcher = @"C:\DeepSeek-QQ\apps\MaiBot-OneKey\MaiBot OneKey.exe";
        Repair.LogStartup("登录自启已开启自动修复。");
        LaunchAtLogon(qq, "QQ");
        LaunchAtLogon(launcher, "麦麦启动器");
        Keepalive.LogStartup("麦麦启动器负责启动引擎和 SnowLuma。QQ 登录后会自动接入已配置的账号。");
        // Give the native launcher time to start both services before attempting repairs.
        Repair.StartWork(60);
        Keepalive.StartWork(60);
    }

    void LaunchAtLogon(string executable, string name)
    {
        try
        {
            if (IsRunningInThisSession(executable))
            {
                Keepalive.LogStartup(name + "已在当前会话运行。");
                return;
            }

            using (Process.Start(new ProcessStartInfo(executable) {
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = false
            })) { }
            Keepalive.LogStartup("已启动" + name + "。");
        }
        catch (Exception ex)
        {
            Keepalive.LogStartup(name + "启动失败，进程保活会继续检查。" + ex.Message);
        }
    }

    static bool IsRunningInThisSession(string executable)
    {
        int sessionId;
        using (var current = Process.GetCurrentProcess())
            sessionId = current.SessionId;
        bool running = false;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == sessionId &&
                        string.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        running = true;
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }
        return running;
    }

    internal void ApplyPalette(){Theme.Apply(this);Repair.ApplyPalette();Keepalive.ApplyPalette();ThemeSwitch.Text=Theme.Dark?"切换浅色模式":"切换深色模式";}

    void RefreshStartup()
    {
        try
        {
            StartupSwitch.Text = StartupRegistration.IsEnabled() ? "开机自启 · 开" : "开机自启 · 关";
        }
        catch (Exception)
        {
            StartupSwitch.Text = "开机自启 · 不可用";
        }
    }

    void ToggleStartup(object sender, EventArgs e)
    {
        try
        {
            StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法更改开机自启设置。\n" + ex.Message,
                "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshStartup();
    }

    void Embed(Maintenance page,TabPage tab){page.TopLevel=false;page.FormBorderStyle=FormBorderStyle.None;page.ShowInTaskbar=false;page.Dock=DockStyle.Fill;tab.Controls.Add(page);page.Show();}
    void RefreshSummary(){
        int count=(Repair.IsEnabled?1:0)+(Keepalive.IsEnabled?1:0);
        if(!AwaitingExit)Summary.Text=count==0?"所有自动维护已暂停。点击顶部按钮一键开启，或在下方分别管理。":"已开启 "+count+" / 2 项自动维护。保持窗口开启或最小化，维护就会继续运行。";
        RepairTab.Text="故障修复 · "+(Repair.IsEnabled?"运行中":"已暂停");
        KeepaliveTab.Text="进程保活 · "+(Keepalive.IsEnabled?"运行中":"已暂停");
        StartAll.Enabled=count<2&&!AwaitingExit;
        StartupSwitch.Enabled=!AwaitingExit;
    }

    [STAThread] static void Main(string[] args){
        bool startAtLogon = Array.IndexOf(args, StartupRegistration.LogonArgument) >= 0;
        bool owner;
        using(var singleton=new Mutex(true,"Local\\RinMaintenance-Manager",out owner)){
            if(!owner){if (!startAtLogon) MessageBox.Show("小凛管家已经打开，请从任务栏切换到它。","小凛管家");return;}
            foreach(string mode in new string[]{"repair","keepalive"}){
                try{using(var old=Mutex.OpenExisting("Local\\RinMaintenance-"+mode)){MessageBox.Show("请先关闭两个旧版维护窗口，再打开小凛管家。","避免重复巡检");return;}}
                catch(WaitHandleCannotBeOpenedException){}
            }
            Theme.Load();
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try{Application.Run(new RinManager(startAtLogon));}catch(Exception ex){MessageBox.Show(ex.Message,"小凛管家无法启动");}
        }
    }
}
