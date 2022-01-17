using System.Diagnostics;
using System.Runtime.InteropServices;

var isOn = false;

var fireCooldown = TimeSpan.FromSeconds(.5);
var fireStopwatch = new Stopwatch();

HotKeyManager.RegisterHotKey(Keys.PageUp, KeyModifiers.Control);
HotKeyManager.HotKeyPressed += new EventHandler<HotKeyEventArgs>((sender, e) =>
{
    isOn = !isOn;

    if (isOn)
    {
        fireStopwatch.Start();
    }
    else
    {
        fireStopwatch.Stop();
    }
});

var screenWidth = 1920;
var screenHeight = 1080;

var bitmapWidth = screenWidth / 1;
var bitmapHeight = screenHeight / 1;

var bitmapSize = new Size(bitmapWidth, bitmapHeight);

var startX = screenWidth / 2 - bitmapWidth / 2;
var startY = screenHeight / 2 - bitmapHeight / 2;

var startPoint = new Point(startX, startY);

var bmp = new Bitmap(bitmapWidth, bitmapHeight);
var g = Graphics.FromImage(bmp);

var boundingRectangles = new List<Rect>();
var boundingRectangleDistanceThreshold = 20;
var boundingRectanglePen = new Pen(Color.Red);
var boundingRectangleAreaThreshold = 10;

var centerX = bitmapWidth / 2;
var centerY = bitmapHeight / 2;

while (true)
{
    g.CopyFromScreen(startPoint, Point.Empty, bmp.Size);

    boundingRectangles.Clear();

    for (var x = 0; x < bitmapWidth; x += 15)
    {
        for (var y = 0; y < bitmapHeight; y += 15)
        {
            var color = bmp.GetPixel(x, y);
            var hue = color.GetHue();
            var saturation = color.GetSaturation();

            var brightness = color.GetBrightness();
            if ((hue >= 340 || hue <= 20) && brightness <= .8 && brightness >= .2 && saturation >= .01)
            {
                var boundingRectangle = boundingRectangles.FirstOrDefault(r => GetDistance(r, x, y) <= boundingRectangleDistanceThreshold);

                if (boundingRectangle == null)
                {
                    boundingRectangle = new Rect(x, y, 1, 1);
                    boundingRectangles.Add(boundingRectangle);
                }
                else if (!boundingRectangle.Contains(x, y))
                {
                    // increase size of bounding rectangle
                    var br = boundingRectangle;

                    // Update the top-left corner.
                    if (x < br.X) br.X = x;
                    if (y < br.Y) br.Y = y;

                    // Update the bottom-right corner.
                    if (x > br.X + br.Width) br.Width = x - br.X;
                    if (y > br.Y + br.Height) br.Height = y - br.Y;
                }
            }
        }
    }

    var goodBoundingRectangles = boundingRectangles.Where(r => r.Area >= boundingRectangleAreaThreshold).ToList();

    var nearest = goodBoundingRectangles.OrderBy(r => GetDistance(r, centerX, centerY)).FirstOrDefault();

    if (isOn && nearest != null)
    {
        var cx = nearest.X + nearest.Width / 2;
        var cy = nearest.Y + nearest.Height / 2;

        var modifier = 5 * .09;

        var dx = (cx - centerX) / modifier;
        var dy = (cy - centerY) / modifier;

        HotKeyManager.mouse_event(HotKeyManager.MOUSEEVENTF_MOVE, (int)dx, (int)dy, 0, 0);
        HotKeyManager.mouse_event(HotKeyManager.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        HotKeyManager.mouse_event(HotKeyManager.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }
}

static double GetDistance(Rect rectangle, int x, int y)
{
    var dx = Math.Max(Math.Max(rectangle.X - x, 0), x - (rectangle.X + rectangle.Width));
    var dy = Math.Max(Math.Max(rectangle.Y - y, 0), y - (rectangle.Y + rectangle.Height));
    return Math.Sqrt(dx * dx + dy * dy);
}

class Rect
{

    public Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Area => Width * Height;

    public bool Contains(int x, int y)
    {
        return x > X && x < X + Width &&
               y > Y && y < Y + Height;
    }

    public override string ToString() => $"X={X}, Y={Y}, Width={Width}, Height={Height}";
}

static class HotKeyManager
{
    public static event EventHandler<HotKeyEventArgs>? HotKeyPressed;

    public static int RegisterHotKey(Keys key, KeyModifiers modifiers)
    {
        _windowReadyEvent.WaitOne();
        int id = Interlocked.Increment(ref _id);
        _wnd?.Invoke(new RegisterHotKeyDelegate(RegisterHotKeyInternal), _hwnd, id, (uint)modifiers, (uint)key);
        return id;
    }

    public static void UnregisterHotKey(int id)
    {
        _wnd?.Invoke(new UnRegisterHotKeyDelegate(UnRegisterHotKeyInternal), _hwnd, id);
    }

    delegate void RegisterHotKeyDelegate(IntPtr hwnd, int id, uint modifiers, uint key);
    delegate void UnRegisterHotKeyDelegate(IntPtr hwnd, int id);

    private static void RegisterHotKeyInternal(IntPtr hwnd, int id, uint modifiers, uint key)
    {
        RegisterHotKey(hwnd, id, modifiers, key);
    }

    private static void UnRegisterHotKeyInternal(IntPtr hwnd, int id)
    {
        UnregisterHotKey(_hwnd, id);
    }

    private static void OnHotKeyPressed(HotKeyEventArgs e)
    {
        HotKeyPressed?.Invoke(null, e);
    }

    private static volatile MessageWindow? _wnd;
    private static volatile IntPtr _hwnd = IntPtr.Zero;
    private static readonly ManualResetEvent _windowReadyEvent = new(false);

    static HotKeyManager()
    {
        Thread messageLoop = new(delegate ()
        {
            Application.Run(new MessageWindow());
        })
        {
            Name = "MessageLoopThread",
            IsBackground = true
        };
        messageLoop.Start();
    }

    [DllImport("user32")]
    public static extern int SetCursorPos(int x, int y);

    public const int MOUSEEVENTF_MOVE = 0x0001; /* mouse move */
    public const int MOUSEEVENTF_LEFTDOWN = 0x0002; /* left button down */
    public const int MOUSEEVENTF_LEFTUP = 0x0004; /* left button up */
    public const int MOUSEEVENTF_RIGHTDOWN = 0x0008; /* right button down */

    [DllImport("user32.dll",
        CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    public static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons,
        int dwExtraInfo);

    private class MessageWindow : Form
    {
        public MessageWindow()
        {
            _wnd = this;
            _hwnd = Handle;
            _windowReadyEvent.Set();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                HotKeyEventArgs e = new(m.LParam);
                OnHotKeyPressed(e);
            }

            base.WndProc(ref m);
        }

        protected override void SetVisibleCore(bool value)
        {
            // Ensure the window never becomes visible
            base.SetVisibleCore(false);
        }

        private const int WM_HOTKEY = 0x312;
    }

    [DllImport("user32", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static int _id = 0;
}

class HotKeyEventArgs : EventArgs
{
    public readonly Keys Key;
    public readonly KeyModifiers Modifiers;

    public HotKeyEventArgs(Keys key, KeyModifiers modifiers)
    {
        this.Key = key;
        this.Modifiers = modifiers;
    }

    public HotKeyEventArgs(IntPtr hotKeyParam)
    {
        uint param = (uint)hotKeyParam.ToInt64();
        Key = (Keys)((param & 0xffff0000) >> 16);
        Modifiers = (KeyModifiers)(param & 0x0000ffff);
    }
}

[Flags]
enum KeyModifiers
{
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
    NoRepeat = 0x4000
}
