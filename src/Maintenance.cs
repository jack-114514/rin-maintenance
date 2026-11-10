using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class Maintenance : Form {
    const string Root = @"C:\DeepSeek-QQ";
    readonly string Mode, DirectoryPath, ExtensionPath, PythonPath;
    readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    readonly System.Windows.Forms.Timer Clock = new System.Windows.Forms.Timer();
    readonly RichTextBox Log = new RichTextBox();
    readonly Label Status = new Label();
    readonly Label Detail = new Label();
    readonly ListView Items = new ListView();
    readonly UiButton StartButton = new UiButton(), StopButton = new UiButton();
    readonly List<Button> Actions = new List<Button>();
    bool EnabledWork = false, Busy = false, CloseAfter = false, EngineStarted = false;
    DateTime NextRun = DateTime.MaxValue;

    internal bool IsBusy { get { return Busy; } }
    internal bool IsEnabled { get { return EnabledWork; } }
    internal void PauseWork() { Pause(); }
    internal void StartWork(int delaySeconds = 0) { EnabledWork=true; NextRun=DateTime.Now.AddSeconds(delaySeconds); Lease(); RefreshState(); Append("已启用。保持窗口打开或最小化，每 60 秒执行一次。"); }
    internal void BeginEngine() {
        if(EngineStarted||Theme.PreviewMode)return;
        EngineStarted=true;Lease();RefreshState();Clock.Start();
        Append(Mode=="repair"?"故障修复就绪。可手动检查、修复麦麦或重连接入。":"进程保活就绪。原有脚本现在由本窗口控制。");
        Run(Mode=="repair"?"--manual --dry-run":"check");
    }

    public Maintenance(string mode) {
        AutoScaleMode=AutoScaleMode.None;
        Mode = mode;
        DirectoryPath = Path.Combine(Root, @"data\auto-repair");
        ExtensionPath = Path.Combine(Root, @"extensions\snowluma-idle-health");
        string[] environments = System.IO.Directory.GetDirectories(Path.Combine(Root,@"data\local-lab\modules\MaiBot\.onekey\environments"));
        if(environments.Length != 1) throw new Exception("麦麦 Python 环境不唯一，请检查安装目录。");
        PythonPath = Path.Combine(environments[0], "python.exe");
        System.IO.Directory.CreateDirectory(DirectoryPath);
        bool repair = Mode == "repair";
        Text = repair ? "小凛 · 故障修复" : "小凛 · 进程保活";
        Size = new Size(900,650); MinimumSize = new Size(780,560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI",10.8F);
        BackColor = Color.FromArgb(246,248,251);
        ShowInTaskbar = true;
        Icon = repair ? SystemIcons.Shield : SystemIcons.Application;
        var layout = new TableLayoutPanel {Dock=DockStyle.Fill, RowCount=5, ColumnCount=1, Padding=new Padding(20)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,164));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Controls.Add(layout);
        Status.Dock=DockStyle.Fill; Status.Font=new Font(Font.FontFamily,21.6F,FontStyle.Bold); Status.Text="已暂停 · 等待您启动";
        Status.ForeColor=Theme.Foreground; layout.Controls.Add(Status,0,0);
        Detail.Dock=DockStyle.Fill; Detail.Text="关闭窗口即停止本工具的自动维护。QQ 和麦麦继续运行。\n静默巡检计划任务已停用；此工具不会隐藏到后台。";
        Detail.ForeColor=Color.DimGray; layout.Controls.Add(Detail,0,1);
        Items.Dock=DockStyle.Fill; Items.View=View.Details; Items.FullRowSelect=true; Items.HeaderStyle=ColumnHeaderStyle.Nonclickable;
        Items.Columns.Add("功能",220); Items.Columns.Add("执行方式",335); Items.Columns.Add("状态",200);
        Items.Items.Add(new ListViewItem(new string[]{repair?"消息链路巡检":"QQ / 麦麦启动器保活","每 60 秒，连续失败确认后处理","已暂停"}));
        Items.Items.Add(new ListViewItem(new string[]{repair?"核心防卡监测":"原有网页健康检查",repair?"跟随本窗口开关；卡住时留证并恢复":"沿用原脚本；故障修复开启时避免重复处理","已暂停"}));
        Items.Items.Add(new ListViewItem(new string[]{"静默计划任务","RinBot-KeepAlive","已停用"}));
        Items.Items.Add(new ListViewItem(new string[]{"当前动作","无","等待操作"}));
        layout.Controls.Add(Items,0,2);
        var buttons = new FlowLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(0,12,0,0)};
        AddButton(buttons,StartButton,"启动自动维护",delegate { StartWork(); });
        AddButton(buttons,StopButton,"暂停维护",delegate { Pause(); Append("已暂停。不会再开始新的自动操作。"); });
        AddAction(buttons,"立即检查",delegate { Run(repair ? "--manual --dry-run" : "check"); });
        if(repair) {
            AddAction(buttons,"修复麦麦",delegate { Run("--manual --repair core"); });
            AddAction(buttons,"重连接入",delegate { Run("--manual --repair snow"); });
        } else {
            AddAction(buttons,"执行一次保活",delegate { Run("keepalive"); });
        }
        var open = new UiButton(); AddButton(buttons,open,"打开日志",delegate { Process.Start("explorer.exe",DirectoryPath); });
        layout.Controls.Add(buttons,0,3);
        Log.Dock=DockStyle.Fill; Log.ReadOnly=true; Log.BackColor=Color.FromArgb(23,30,42); Log.ForeColor=Color.FromArgb(216,230,240);
        Log.Font=new Font("Microsoft YaHei UI",10.8F); Log.BorderStyle=BorderStyle.None; layout.Controls.Add(Log,0,4);
        Clock.Interval=5000; Clock.Tick += delegate {
            Lease();
            if(EnabledWork && !Busy && DateTime.Now>=NextRun) Run(repair?"":"keepalive");
        };
        FormClosing += delegate(object sender, FormClosingEventArgs e) {
            Pause();
            if(Busy){CloseAfter=true; e.Cancel=true; Append("正在完成本次操作，结束后自动关闭；后续维护已暂停。");}
        };
        Shown += delegate { BeginEngine(); };
        Detail.Tag="muted"; ApplyPalette();
        Items.Resize+=delegate { int width=Math.Max(600,Items.ClientSize.Width); Items.Columns[0].Width=width*26/100; Items.Columns[1].Width=width*54/100; Items.Columns[2].Width=width-Items.Columns[0].Width-Items.Columns[1].Width; };
    }

    internal void ApplyPalette(){Theme.Apply(this);RefreshState();}

    void AddButton(FlowLayoutPanel panel,Button button,string text,Action action){
        button.Text=text; button.AutoSize=true; button.Height=42; button.Margin=new Padding(0,0,8,0); button.Click+=delegate{action();}; panel.Controls.Add(button);
    }
    void AddAction(FlowLayoutPanel panel,string text,Action action){var b=new UiButton();AddButton(panel,b,text,action);Actions.Add(b);}
    internal void LogStartup(string text){Append(text);}
    void Append(string text){Log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);Log.SelectionStart=Log.TextLength;Log.ScrollToCaret();if(Log.TextLength>100000)Log.Text=Log.Text.Substring(Log.TextLength-70000);}
    void Lease(){
        var lease=new Dictionary<string,object>{{"enabled",EnabledWork},{"pid",Process.GetCurrentProcess().Id},{"time",(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds}};
        string file=Path.Combine(DirectoryPath,"gui-"+Mode+".json"), temp=file+".tmp";
        File.WriteAllText(temp,Json.Serialize(lease),new UTF8Encoding(false));
        if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
    }
    void Pause(){EnabledWork=false;NextRun=DateTime.MaxValue;Lease();RefreshState();}
    void RefreshState(){
        StartButton.Enabled=!EnabledWork&&!Busy;StopButton.Enabled=EnabledWork;
        foreach(Button b in Actions)b.Enabled=!Busy;
        Status.Text=Busy?"正在执行 · 日志显示在下方":(EnabledWork?"自动维护运行中 · 每 60 秒检查":"已暂停 · 可手动执行");
        Status.ForeColor=EnabledWork?Theme.Active:Theme.Foreground;
        Items.Items[0].SubItems[2].Text=EnabledWork?"运行中":"已暂停";
        Items.Items[1].SubItems[2].Text=EnabledWork?"已启用":"已暂停";
        Items.Items[3].SubItems[2].Text=Busy?"执行中":"空闲";
    }
    async void Run(string arguments){
        if(Busy)return;
        Busy=true;RefreshState();
        Items.Items[3].SubItems[1].Text=arguments.Contains("--repair")?"手动修复":(arguments=="keepalive"?"进程保活":"检查运行状态");
        Append("开始："+Items.Items[3].SubItems[1].Text);
        try{
            string output=await Task.Run(delegate {
                var info=new ProcessStartInfo();
                if(Mode=="repair"){
                    info.FileName=PythonPath;
                    info.Arguments="\""+Path.Combine(ExtensionPath,"auto_repair.py")+"\" --root \""+Root+"\" "+arguments;
                }else{
                    info.FileName="powershell.exe";
                    info.Arguments="-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+Path.Combine(ExtensionPath,"keepalive-console.ps1")+"\" -Mode "+arguments;
                }
                info.WorkingDirectory=Root;info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;
                info.StandardOutputEncoding=Encoding.UTF8;info.StandardErrorEncoding=Encoding.UTF8;
                using(var process=Process.Start(info)){
                    Task<string> error=process.StandardError.ReadToEndAsync();
                    Task<string> outputTask=process.StandardOutput.ReadToEndAsync();
                    if(!process.WaitForExit(90000)){process.Kill();throw new Exception("执行超时，请查看日志。");}
                    string result=outputTask.Result;
                    string errors=error.Result;
                    if(process.ExitCode!=0)throw new Exception("执行失败（代码 "+process.ExitCode+"）。"+errors);
                    return result+errors;
                }
            });
            if(Mode=="repair"){
                var value=Json.Deserialize<Dictionary<string,object>>(output.Trim());
                string qq=Convert.ToString(value["qq"]);string reason=Convert.ToString(value["reason"]);
                string state=qq=="healthy"?"在线，收发链路正常":qq=="offline"?"需要重新登录":qq=="degraded"?"接收链路异常":"接入端不可达";
                Append("QQ："+state+"；麦麦主循环："+(Convert.ToBoolean(value["core_ok"])?"正常":"异常"));
                string result=reason=="healthy"?"正常，无需修复":reason=="cooldown"?"恢复冷却中，避免重复重启":reason=="hourly_limit"?"已达每小时恢复上限，请查看日志":reason=="needs_qq_login"?"请打开 QQ 完成登录":reason=="confirming_failure"?"等待连续失败确认":reason=="manual_repair"?"已请求手动修复，请稍后再次检查":"已按检测结果处理";
                Append("结果："+result);
            }else Append(output.Trim());
            Append("本次执行完成。");
        }catch(Exception ex){Append("错误："+ex.Message);}
        finally{
            Busy=false;NextRun=DateTime.Now.AddSeconds(60);Lease();RefreshState();
            Items.Items[3].SubItems[1].Text="无";
            if(CloseAfter)Close();
        }
    }

    [STAThread] static void Main(string[] args){
#if KEEPALIVE
        string mode="keepalive";
#else
        string mode=args.Length>0&&args[0]=="keepalive"?"keepalive":"repair";
#endif
        bool owner;
        using(var mutex=new Mutex(true,"Local\\RinMaintenance-"+mode,out owner)){
            if(!owner){MessageBox.Show("这个维护工具已经打开，请从任务栏切换到它。","小凛维护");return;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try{Application.Run(new Maintenance(mode));}catch(Exception ex){MessageBox.Show(ex.Message,"维护工具无法启动");}
        }
    }
}
