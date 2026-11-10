using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

class UiTests {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static double L(Color c){double[] a={c.R/255.0,c.G/255.0,c.B/255.0};for(int i=0;i<3;i++)a[i]=a[i]<=.04045?a[i]/12.92:Math.Pow((a[i]+.055)/1.055,2.4);return .2126*a[0]+.7152*a[1]+.0722*a[2];}
    static double Contrast(Color a,Color b){return(Math.Max(L(a),L(b))+.05)/(Math.Min(L(a),L(b))+.05);}
    static void ContrastCheck(Color a,Color b,string label){double r=Contrast(a,b);Check(r>=4.5,label+" contrast="+r);Console.WriteLine(label+" contrast="+r.ToString("F2"));}
    static object Field(object value,string name){return value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
    static void LayoutCheck(Control root){
        foreach(Control c in root.Controls){
            var button=c as UiButton;
            if(button!=null){var text=TextRenderer.MeasureText(button.Text,button.Font);Check(button.Width>=text.Width+12,"Button text clips: "+button.Text);Check(button.Height>=text.Height+6,"Button height clips");}
            LayoutCheck(c);
        }
    }
    [STAThread] static void Main(){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Theme.PreviewMode=true;Theme.PreferencesPath=Path.Combine(Path.GetTempPath(),"rin-ui-tests-"+Guid.NewGuid().ToString("N")+".json");
        string output=@"C:\DeepSeek-QQ\data\auto-repair\ui-qa";Directory.CreateDirectory(output);
        try{
            Theme.SuppressClosePrompt=false;Theme.RememberClose(true,false);Check(!Theme.SuppressClosePrompt&&!File.Exists(Theme.PreferencesPath),"Cancel must not persist opt-out");
            Theme.RememberClose(true,true);Theme.SuppressClosePrompt=false;Theme.Load();Check(Theme.SuppressClosePrompt,"Confirmed opt-out must persist");Theme.SuppressClosePrompt=false;
            foreach(bool dark in new bool[]{true,false}){
                Theme.Dark=dark;string name=dark?"dark":"light";
                ContrastCheck(Theme.Foreground,Theme.Background,name+" text");
                ContrastCheck(Theme.Muted,Theme.Background,name+" secondary");
                ContrastCheck(Color.White,Theme.Primary,name+" primary button");
                ContrastCheck(Theme.DisabledText,Theme.DisabledBack,name+" disabled button");
                using(var form=new RinManager()){
                    form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-4000,-4000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();form.PerformLayout();form.ApplyPalette();
                    Check(Math.Abs(form.Font.Size-10.8F)<.01,"Base font not 1.2x");
                    var repair=(Maintenance)Field(form,"Repair");var keep=(Maintenance)Field(form,"Keepalive");
                    Check(Math.Abs(((Label)Field(repair,"Status")).Font.Size-21.6F)<.01,"Status font");
                    Check(Math.Abs(((RichTextBox)Field(repair,"Log")).Font.Size-10.8F)<.01,"Log font");
                    ((RichTextBox)Field(repair,"Log")).AppendText("16:20:01  QQ：在线，收发链路正常；麦麦主循环：正常\n16:20:01  结果：正常，无需修复\n16:20:01  本次执行完成。");
                    form.PerformLayout();LayoutCheck(form);
                    using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(output,name+"-manager.png"));}
                    var button=(UiButton)Field(form,"StartAll");
                    foreach(string state in new string[]{"normal","hover","pressed","disabled"}){
                        button.Enabled=state!="disabled";button.Hovered=state=="hover";button.Pressed=state=="pressed";
                        using(var image=new Bitmap(button.Width,button.Height)){button.DrawToBitmap(image,new Rectangle(Point.Empty,button.Size));image.Save(Path.Combine(output,name+"-button-"+state+".png"));}
                    }
                    Check(!repair.IsEnabled&&!keep.IsEnabled,"Preview started real maintenance");
                }
                using(var dialog=Theme.CreateCloseDialog()){
                    dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-4000,-4000);dialog.Show();Application.DoEvents();dialog.PerformLayout();LayoutCheck(dialog);
                    Check(dialog.Controls.Find("RememberClose",true).Length==1,"Missing reminder checkbox");
                    using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,dialog.Size));bitmap.Save(Path.Combine(output,name+"-close.png"));}
                }
            }
            Console.WriteLine("PASS: themes, all button states, font scale, layout and close preference persistence");
        }finally{if(File.Exists(Theme.PreferencesPath))File.Delete(Theme.PreferencesPath);}
    }
}
