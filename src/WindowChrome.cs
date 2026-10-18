using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
namespace RoBuff.App;
public sealed partial class CloudWindow
{
    Button? maximizeButton;
    TextBlock? loadingText;
    Border? loadingPanel;
    bool magnetic;
    Button? magneticButton;
    Button? pinButton;
    void UpdatePin(){if(pinButton is null)return;pinButton.ToolTip="항상 위에 표시: "+(Topmost?"켜짐":"꺼짐");pinButton.Foreground=new SolidColorBrush(Topmost?Color.FromRgb(208,181,250):Color.FromRgb(135,129,147));System.Windows.Automation.AutomationProperties.SetName(pinButton,(string)pinButton.ToolTip);}
    void TogglePin(){Topmost=!Topmost;UpdatePin();Push("windowPin",new{enabled=Topmost});if(saved is not null){saved["pin"]=Topmost;Save(saved);}}
    bool movingWindow;
    const int MagneticSnapDistance=44;
    ImageSource CloudArtwork(){
        var group=new DrawingGroup();
        var ink=new SolidColorBrush(Color.FromRgb(189,169,236));var background=new SolidColorBrush(Color.FromRgb(41,38,49));
        group.Children.Add(new GeometryDrawing(background,null,new RectangleGeometry(new Rect(2,2,60,60),15,15)));
        group.Children.Add(new GeometryDrawing(ink,null,Geometry.Parse("M25 19V46H34C48 46 49 32 39 31C47 27 42 19 34 19H25Z")));
        group.Children.Add(new GeometryDrawing(background,null,Geometry.Parse("M31 25H34C38 25 38 29 34 29H31ZM31 35H35C40 35 40 40 35 40H31Z")));
        group.Children.Add(new GeometryDrawing(null,new Pen(ink,5){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},Geometry.Parse("M24 46H18C11 46 8 42 8 37C8 31 12 27 18 27C18 22 21 19 25 19")));
        var mark=new DrawingGroup{Transform=new TranslateTransform(6,-1)};while(group.Children.Count>1){var part=group.Children[1];group.Children.RemoveAt(1);mark.Children.Add(part);}group.Children.Add(mark);
        group.Freeze();return new DrawingImage(group);
    }
    void UpdateMagnetic(){if(magneticButton is null)return;magneticButton.ToolTip="모니터 가장자리에 붙이기: "+(magnetic?"켜짐":"꺼짐");magneticButton.Foreground=new SolidColorBrush(magnetic?Color.FromRgb(208,181,250):Color.FromRgb(135,129,147));System.Windows.Automation.AutomationProperties.SetName(magneticButton,(string)magneticButton.ToolTip);}
    void ShowApp(){web.Visibility=Visibility.Visible;if(loadingPanel is not null)loadingPanel.Visibility=Visibility.Collapsed;}
    void LoadingStatus(string text){if(loadingText is not null)loadingText.Text=text;}
    void ConfigureChrome(){
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=34,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(0),UseAeroCaptionButtons=false});
        Icon=CloudArtwork();
        // Reserve native non-client hit areas outside the WebView child HWND.
        var layout=new Grid{Margin=new Thickness(6)};layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(34)});layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        var bar=new Grid{Background=new SolidColorBrush(Color.FromRgb(37,36,45))};bar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});bar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var brand=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};brand.Children.Add(new Image{Source=Icon,Width=18,Height=18,Margin=new Thickness(0,0,8,0)});brand.Children.Add(new TextBlock{Text="RO CLOUD BUFF",FontFamily=new FontFamily("Segoe UI"),FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(220,214,238)),VerticalAlignment=VerticalAlignment.Center});bar.Children.Add(brand);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);
        brand.ClipToBounds=true;
        pinButton=CaptionButton("","항상 위에 표시",TogglePin);var pinIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M6 2H14 M8 2V7L5 10V11H15V10L12 7V2 M10 11V17"),StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=14,Height=16,Stretch=Stretch.Uniform};pinIcon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground"){Source=pinButton});pinButton.Content=pinIcon;pinButton.MouseLeave+=(_,_)=>UpdatePin();actions.Children.Add(pinButton);UpdatePin();
        magneticButton=CaptionButton("\uE718","모니터 가장자리에 붙이기",()=>{magnetic=!magnetic;UpdateMagnetic();if(saved is not null)Save(saved);});
        var magnet=new System.Windows.Shapes.Path{Data=Geometry.Parse("M4 3H9V12A3 3 0 0 0 15 12V3H20V12A8 8 0 0 1 4 12V3Z M4 7H9 M15 7H20"),StrokeThickness=1.75,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};magnet.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground"){Source=magneticButton});var magnetCanvas=new Canvas{Width=24,Height=24};magnetCanvas.Children.Add(magnet);magneticButton.Content=new Viewbox{Width=16,Height=16,Child=magnetCanvas};magneticButton.MouseLeave+=(_,_)=>UpdateMagnetic();actions.Children.Add(magneticButton);UpdateMagnetic();
        actions.Children.Add(CaptionButton("\uE921","최소화",()=>SystemCommands.MinimizeWindow(this)));
        maximizeButton=CaptionButton("\uE922","최대화 / 복원",()=>{if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);});actions.Children.Add(maximizeButton);
        actions.Children.Add(CaptionButton("\uE8BB","닫기",Close,true));bar.Children.Add(actions);layout.Children.Add(bar);Grid.SetRow(web,1);web.DefaultBackgroundColor=System.Drawing.Color.FromArgb(32,33,38);web.Visibility=Visibility.Hidden;layout.Children.Add(web);
        var loading=new StackPanel{Width=240,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
        loading.Children.Add(new Image{Source=Icon,Width=64,Height=64,Margin=new Thickness(0,0,0,20)});
        loading.Children.Add(new TextBlock{Text="RO CLOUD BUFF",FontSize=20,FontFamily=new FontFamily("Segoe UI"),FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(232,226,245)),HorizontalAlignment=HorizontalAlignment.Center});
        loadingText=new TextBlock{Text="시작하는 중…",FontSize=13,Foreground=new SolidColorBrush(Color.FromRgb(169,161,186)),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,12,0,20)};loading.Children.Add(loadingText);
        loading.Children.Add(new ProgressBar{IsIndeterminate=true,Height=3,BorderThickness=new Thickness(0),Foreground=new SolidColorBrush(Color.FromRgb(169,148,213)),Background=new SolidColorBrush(Color.FromRgb(54,49,65))});
        loadingPanel=new Border{Background=Background,Child=loading};Grid.SetRow(loadingPanel,1);layout.Children.Add(loadingPanel);Content=layout;
        StateChanged+=(_,_)=>{if(maximizeButton is not null)maximizeButton.Content=WindowState==WindowState.Maximized?"\uE923":"\uE922";};
        SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(this).Handle;var source=HwndSource.FromHwnd(handle);source?.AddHook(ChromeMessage);int preference=3;DwmSetWindowAttribute(handle,33,ref preference,sizeof(int));};
    }
    Button CaptionButton(string glyph,string label,Action click,bool close=false){var button=new Button{Content=glyph,Width=42,Height=34,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(186,180,203)),Background=Brushes.Transparent,BorderThickness=new Thickness(0),ToolTip=label,Padding=new Thickness(0),Focusable=true};System.Windows.Automation.AutomationProperties.SetName(button,label);WindowChrome.SetIsHitTestVisibleInChrome(button,true);var border=new FrameworkElementFactory(typeof(Border));border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);button.Template=new ControlTemplate(typeof(Button)){VisualTree=border};button.MouseEnter+=(_,_)=>{button.Background=new SolidColorBrush(close?Color.FromRgb(169,64,87):Color.FromRgb(58,53,70));button.Foreground=Brushes.White;};button.MouseLeave+=(_,_)=>{button.Background=Brushes.Transparent;button.Foreground=new SolidColorBrush(Color.FromRgb(186,180,203));};button.Click+=(_,_)=>click();return button;}
    nint ChromeMessage(nint h,int message,nint w,nint l,ref bool handled){
        if(message==0x84&&WindowState==WindowState.Normal&&GetWindowRect(h,out var bounds)){int x=unchecked((short)(l.ToInt64()&0xffff)),y=unchecked((short)((l.ToInt64()>>16)&0xffff));int hit=ResizeHit(bounds,x,y,Math.Max(6,(int)Math.Round(6*GetDpiForWindow(h)/96.0)));if(hit!=0){handled=true;return hit;}}
        // Leave every drag position untouched. Snap once on release, so an attached
        // window can always be dragged away without fighting repeated corrections.
        if(message==0x231)movingWindow=false;
        if(message==0x216)movingWindow=true;
        if(message==0x232){bool moved=movingWindow;movingWindow=false;if(moved&&magnetic&&WindowState==WindowState.Normal&&GetWindowRect(h,out var rect)){var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(GetMonitorInfo(MonitorFromRect(ref rect,2),ref monitor)){var snapped=SnapRect(rect,monitor.Work,(int)Math.Round(MagneticSnapDistance*GetDpiForWindow(h)/96.0));if(snapped.Left!=rect.Left||snapped.Top!=rect.Top)SetWindowPos(h,0,snapped.Left,snapped.Top,0,0,0x15);}}}
        if(message==0x24){var info=Marshal.PtrToStructure<MinMaxInfo>(l);var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(GetMonitorInfo(MonitorFromWindow(h,2),ref monitor)){info.MaxPosition=new NativePoint{X=monitor.Work.Left-monitor.Bounds.Left,Y=monitor.Work.Top-monitor.Bounds.Top};info.MaxSize=new NativePoint{X=monitor.Work.Right-monitor.Work.Left,Y=monitor.Work.Bottom-monitor.Work.Top};Marshal.StructureToPtr(info,l,false);}handled=false;}return 0;}
    static NativeRect SnapRect(NativeRect r,NativeRect work,int distance){int width=r.Right-r.Left,height=r.Bottom-r.Top;int dx=Math.Abs(r.Left-work.Left)<=Math.Abs(r.Right-work.Right)?work.Left-r.Left:work.Right-r.Right;int dy=Math.Abs(r.Top-work.Top)<=Math.Abs(r.Bottom-work.Bottom)?work.Top-r.Top:work.Bottom-r.Bottom;if(Math.Abs(dx)<=distance){r.Left+=dx;r.Right=r.Left+width;}if(Math.Abs(dy)<=distance){r.Top+=dy;r.Bottom=r.Top+height;}return r;}
    static int ResizeHit(NativeRect r,int x,int y,int border){if(x<r.Left||x>=r.Right||y<r.Top||y>=r.Bottom)return 0;bool left=x<r.Left+border,right=x>=r.Right-border,top=y<r.Top+border,bottom=y>=r.Bottom-border;return top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;}
    void CheckVectorAndMagnet(){
        if(Icon is not DrawingImage)throw new InvalidOperationException("Vector icon missing");
        var wave=ChimeWave(65);if(wave.Length<44||System.Text.Encoding.ASCII.GetString(wave,0,4)!="RIFF"||!wave.Skip(44).Any(b=>b!=0)||ChimeWave(0).Skip(44).Any(b=>b!=0))throw new InvalidOperationException("Native alert waveform invalid");
        if(pinButton?.Content is not System.Windows.Shapes.Path)throw new InvalidOperationException("Title bar pin icon missing");
        var resizeBounds=new NativeRect{Left=-500,Top=0,Right=0,Bottom=600};int[] hits=[ResizeHit(resizeBounds,-499,300,6),ResizeHit(resizeBounds,-1,300,6),ResizeHit(resizeBounds,-250,1,6),ResizeHit(resizeBounds,-250,599,6),ResizeHit(resizeBounds,-499,1,6),ResizeHit(resizeBounds,-1,1,6),ResizeHit(resizeBounds,-499,599,6),ResizeHit(resizeBounds,-1,599,6)];if(!hits.SequenceEqual(new[]{10,11,12,15,13,14,16,17})||ResizeHit(resizeBounds,-250,300,6)!=0)throw new InvalidOperationException("Eight-direction resize hit test failed");
        if(magneticButton?.Content is not Viewbox)throw new InvalidOperationException("Vector magnet button missing");
        bool previousMagnetic=magnetic;var position=Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());try{magnetic=true;var proposed=new NativeRect{Left=5,Top=5,Right=455,Bottom=885};Marshal.StructureToPtr(proposed,position,false);bool handled=false;ChromeMessage(0,0x216,0,position,ref handled);var unchanged=Marshal.PtrToStructure<NativeRect>(position);if(handled||unchanged.Left!=5||unchanged.Top!=5)throw new InvalidOperationException("Dragging must remain free");}finally{magnetic=previousMagnetic;movingWindow=false;Marshal.FreeHGlobal(position);}
        var area=new NativeRect{Left=-1920,Top=0,Right=0,Bottom=1040};var r=SnapRect(new NativeRect{Left=-1915,Top=7,Right=-1465,Bottom=887},area,12);
        if(r.Left!=-1920||r.Top!=0||r.Right-r.Left!=450||r.Bottom-r.Top!=880)throw new InvalidOperationException("Magnetic position/size mismatch");
        var near=SnapRect(new NativeRect{Left=-1895,Top=25,Right=-1445,Bottom=905},area,MagneticSnapDistance);if(near.Left!=-1920||near.Top!=0)throw new InvalidOperationException("Expanded magnetic range failed");
        var far=SnapRect(new NativeRect{Left=-1871,Top=49,Right=-1421,Bottom=929},area,MagneticSnapDistance);if(far.Left!=-1871||far.Top!=49)throw new InvalidOperationException("Magnetic threshold mismatch");
    }
    [StructLayout(LayoutKind.Sequential)]struct NativePoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct MinMaxInfo{public NativePoint Reserved,MaxSize,MaxPosition,MinTrack,MaxTrack;}
    [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public NativeRect Bounds,Work;public uint Flags;}
    [DllImport("user32.dll")]static extern nint MonitorFromWindow(nint window,uint flags);
    [DllImport("user32.dll")]static extern nint MonitorFromRect(ref NativeRect rect,uint flags);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")]static extern bool GetWindowRect(nint window,out NativeRect rect);
    [DllImport("user32.dll")]static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
}
