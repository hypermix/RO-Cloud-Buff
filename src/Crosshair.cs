using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace RoBuff.App;
public sealed partial class CloudWindow
{
    Window? crosshairHint;TextBlock? crosshairLabel;DispatcherTimer? crosshairTimer;DateTimeOffset crosshairUntil;
    void StartCrosshair(){StopCrosshair();crosshairUntil=DateTimeOffset.UtcNow.AddSeconds(25);crosshairLabel=new TextBlock{Text="과녁을 라그나로크 창 위에 놓으세요",Foreground=Brushes.White,FontSize=12,Margin=new Thickness(10,8,10,8),TextWrapping=TextWrapping.Wrap};crosshairHint=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=new SolidColorBrush(Color.FromRgb(58,50,78)),Width=260,Height=48,Topmost=true,ShowActivated=false,ShowInTaskbar=false,IsHitTestVisible=false,Content=crosshairLabel};crosshairHint.Show();Mouse.OverrideCursor=Cursors.Cross;crosshairTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(60)};crosshairTimer.Tick+=(_,_)=>{if(DateTimeOffset.UtcNow>crosshairUntil){StopCrosshair();return;}GetCursorPos(out var point);SetCursor(LoadCursor(0,32515));var scale=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice??Matrix.Identity;var position=scale.Transform(new System.Windows.Point(point.X,point.Y));crosshairHint.Left=position.X+20;crosshairHint.Top=position.Y+20;string label;try{var game=WindowsInterop.Inspect(GetAncestor(WindowFromPoint(point),2));label=$"선택 가능 · {game.Title}\nPID {game.Pid} · 마우스를 놓아 선택";}catch{label="라그나로크 창 위로 이동하세요\n다른 창에서는 선택되지 않습니다.";}crosshairLabel.Text=label;Push("crosshair",new{label});};crosshairTimer.Start();}
    void StopCrosshair(){crosshairTimer?.Stop();crosshairTimer=null;crosshairHint?.Close();crosshairHint=null;Mouse.OverrideCursor=null;SetCursor(LoadCursor(0,32512));}
    [DllImport("user32.dll")]static extern nint LoadCursor(nint instance,int cursor);
    [DllImport("user32.dll")]static extern nint SetCursor(nint cursor);
}
