using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Theme {
    internal static bool Dark=true, SuppressClosePrompt=false, PreviewMode=false;
    internal static string PreferencesPath=@"C:\DeepSeek-QQ\data\auto-repair\ui-preferences.json";
    internal static Color Background {get{return Dark?Color.FromArgb(20,27,39):Color.FromArgb(245,247,250);}}
    internal static Color Surface {get{return Dark?Color.FromArgb(30,41,58):Color.White;}}
    internal static Color Raised {get{return Dark?Color.FromArgb(42,57,78):Color.FromArgb(230,237,246);}}
    internal static Color Foreground {get{return Dark?Color.FromArgb(242,246,252):Color.FromArgb(23,43,66);}}
    internal static Color Muted {get{return Dark?Color.FromArgb(183,198,218):Color.FromArgb(65,84,108);}}
    internal static Color Active {get{return Dark?Color.FromArgb(111,230,177):Color.FromArgb(6,112,67);}}
    internal static Color Accent {get{return Dark?Color.FromArgb(113,189,255):Color.FromArgb(22,103,170);}}
    internal static Color Border {get{return Dark?Color.FromArgb(99,122,152):Color.FromArgb(123,143,167);}}
    internal static Color DisabledBack {get{return Dark?Color.FromArgb(41,52,70):Color.FromArgb(226,233,241);}}
    internal static Color DisabledText {get{return Dark?Color.FromArgb(183,198,218):Color.FromArgb(77,96,122);}}
    internal static Color Primary {get{return Color.FromArgb(25,103,165);}}
    internal static Color Selected {get{return Dark?Color.FromArgb(49,77,113):Color.FromArgb(218,234,250);}}
    static readonly HashSet<ListView> StyledLists=new HashSet<ListView>();

    internal static void Load(){
        try{
            var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(PreferencesPath));
            Dark=data.ContainsKey("dark")?Convert.ToBoolean(data["dark"]):true;
            SuppressClosePrompt=data.ContainsKey("suppressClosePrompt")&&Convert.ToBoolean(data["suppressClosePrompt"]);
        }catch(IOException){}catch(ArgumentException){}catch(InvalidCastException){}catch(FormatException){}
    }
    internal static void Save(){
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath));
        var text=new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"dark",Dark},{"suppressClosePrompt",SuppressClosePrompt}});
        File.WriteAllText(PreferencesPath,text,new System.Text.UTF8Encoding(false));
    }
    internal static void RememberClose(bool remember,bool confirmed){
        if(remember&&confirmed){SuppressClosePrompt=true;Save();}
    }
    internal static void Apply(Control control){
        control.BackColor=Background;control.ForeColor=Foreground;
        if(control is Label && Convert.ToString(control.Tag)=="muted")control.ForeColor=Muted;
        if(control is RichTextBox){control.BackColor=Dark?Color.FromArgb(12,19,29):Color.FromArgb(239,244,250);control.ForeColor=Foreground;}
        var list=control as ListView;
        if(list!=null){
            list.BackColor=Surface;list.ForeColor=Foreground;list.OwnerDraw=true;list.HideSelection=false;list.BorderStyle=BorderStyle.None;
            list.ShowItemToolTips=true;
            if(StyledLists.Add(list)){
                list.DrawColumnHeader+=delegate(object sender,DrawListViewColumnHeaderEventArgs e){
                    using(var b=new SolidBrush(Raised))e.Graphics.FillRectangle(b,e.Bounds);
                    TextRenderer.DrawText(e.Graphics,e.Header.Text,list.Font,Rectangle.Inflate(e.Bounds,-9,0),Foreground,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                };
                list.DrawItem+=delegate(object sender,DrawListViewItemEventArgs e){};
                list.DrawSubItem+=delegate(object sender,DrawListViewSubItemEventArgs e){
                    using(var b=new SolidBrush(e.Item.Selected?Selected:Surface))e.Graphics.FillRectangle(b,e.Bounds);
                    TextRenderer.DrawText(e.Graphics,e.SubItem.Text,list.Font,Rectangle.Inflate(e.Bounds,-9,0),Foreground,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                };
            }
        }
        foreach(Control child in control.Controls)Apply(child);
        control.Invalidate();
    }
    internal static Form CreateCloseDialog(){
        var dialog=new Form {Text="关闭提示",Size=new Size(650,285),Font=new Font("Microsoft YaHei UI",10.8F),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false};
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=3};
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));dialog.Controls.Add(layout);
        var message=new Label {Dock=DockStyle.Fill,Text="关闭此程序之后，自动维护会停止，QQ 和麦麦会继续运行。\n\n如果需要自动维护继续运行，请保持窗口开启或最小化。"};
        layout.Controls.Add(message,0,0);
        var remember=new CheckBox {Name="RememberClose",Text="下次不再提醒",AutoSize=true,Dock=DockStyle.Left};
        layout.Controls.Add(remember,0,1);
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var cancel=new UiButton {Text="返回继续运行",Width=170,Height=42,DialogResult=DialogResult.No};
        var exit=new UiButton {Text="关闭并停止维护",Width=185,Height=42,DialogResult=DialogResult.Yes,PrimaryAction=true};
        buttons.Controls.Add(cancel);buttons.Controls.Add(exit);layout.Controls.Add(buttons,0,2);
        dialog.CancelButton=cancel;dialog.AcceptButton=cancel;Apply(dialog);
        return dialog;
    }
    internal static DialogResult ConfirmClose(IWin32Window owner){
        if(SuppressClosePrompt)return DialogResult.Yes;
        using(var dialog=CreateCloseDialog()){
            var answer=dialog.ShowDialog(owner);
            var check=(CheckBox)dialog.Controls.Find("RememberClose",true)[0];
            RememberClose(check.Checked,answer==DialogResult.Yes);
            return answer;
        }
    }
}

class UiButton : Button {
    internal bool PrimaryAction=false;
    internal bool Hovered=false, Pressed=false;
    public UiButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;MinimumSize=new Size(0,40);}
    public override Size GetPreferredSize(Size proposedSize){return new Size(TextRenderer.MeasureText(Text,Font).Width+28,Math.Max(42,Font.Height+20));}
    protected override void OnMouseEnter(EventArgs e){Hovered=true;base.OnMouseEnter(e);Invalidate();}
    protected override void OnMouseLeave(EventArgs e){Hovered=false;Pressed=false;base.OnMouseLeave(e);Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e){Pressed=e.Button==MouseButtons.Left;base.OnMouseDown(e);Invalidate();}
    protected override void OnMouseUp(MouseEventArgs e){Pressed=false;base.OnMouseUp(e);Invalidate();}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        Color back=!Enabled?Theme.DisabledBack:PrimaryAction?Theme.Primary:Pressed?(Theme.Dark?Color.FromArgb(31,46,65):Color.FromArgb(201,215,233)):Hovered?(Theme.Dark?Color.FromArgb(58,79,106):Color.FromArgb(213,228,245)):Theme.Raised;
        Color fore=!Enabled?Theme.DisabledText:PrimaryAction?Color.White:Theme.Foreground;
        using(var brush=new SolidBrush(back))e.Graphics.FillRectangle(brush,ClientRectangle);
        using(var pen=new Pen(Focused?Theme.Accent:Theme.Border,Focused?2:1))e.Graphics.DrawRectangle(pen,1,1,Width-3,Height-3);
        TextRenderer.DrawText(e.Graphics,Text,Font,Rectangle.Inflate(ClientRectangle,-8,-2),fore,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    }
}

class UiTabs : TabControl {
    public UiTabs(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);SizeMode=TabSizeMode.Fixed;ItemSize=new Size(205,36);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(Theme.Background);
        for(int i=0;i<TabCount;i++){
            var rect=GetTabRect(i);bool selected=i==SelectedIndex;
            using(var b=new SolidBrush(selected?Theme.Surface:Theme.Raised))e.Graphics.FillRectangle(b,rect);
            TextRenderer.DrawText(e.Graphics,TabPages[i].Text,Font,rect,selected?Theme.Foreground:Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(selected)using(var pen=new Pen(Theme.Accent,3))e.Graphics.DrawLine(pen,rect.Left,rect.Bottom-2,rect.Right,rect.Bottom-2);
        }
    }
}
