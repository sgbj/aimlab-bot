using System.Runtime.InteropServices;

var isOn = false;

HotKeyManager.RegisterHotKey(Keys.PageUp, KeyModifiers.Control);
HotKeyManager.HotKeyPressed += (_, _) => isOn = !isOn;

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

    for (var x = 0; x < bitmapWidth; x += 10)
    {
        for (var y = 0; y < bitmapHeight; y += 10)
        {
            var color = bmp.GetPixel(x, y);
            var hue = color.GetHue();
            var saturation = color.GetSaturation();
            var brightness = color.GetBrightness();

            if (hue is <= 20 or >= 340 && brightness is >= .3f and <= .7f && saturation >= .5)
            {
                var index = boundingRectangles.FindIndex(r => r.GetDistance(x, y) <= boundingRectangleDistanceThreshold);

                if (index == -1)
                {
                    boundingRectangles.Add(new Rect(x, y, 1, 1));
                }
                else
                {
                    var br = boundingRectangles[index];

                    if (!br.Contains(x, y))
                    {
                        // Increase size of bounding rectangle
                        if (x < br.X) br.X = x;
                        if (y < br.Y) br.Y = y;
                        if (x > br.X + br.Width) br.Width = x - br.X;
                        if (y > br.Y + br.Height) br.Height = y - br.Y;
                    }

                    boundingRectangles[index] = br;
                }
            }
        }
    }

    var goodBoundingRectangles = boundingRectangles.Where(r => r.Area >= boundingRectangleAreaThreshold).ToList();

    var nearest = goodBoundingRectangles.OrderBy(r => r.GetDistance(centerX, centerY)).FirstOrDefault();

    if (isOn && nearest != default)
    {
        // Approximate world to screen coordinates
        var dx = nearest.X + nearest.Width / 2 - centerX;
        var dy = nearest.Y + nearest.Height / 2 - centerY;

        HotKeyManager.mouse_event(HotKeyManager.MOUSEEVENTF_MOVE, dx, dy, 0, 0);

        if (Math.Sqrt(dx * dx + dy * dy) < 100)
        {
            HotKeyManager.mouse_event(HotKeyManager.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            HotKeyManager.mouse_event(HotKeyManager.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }

        await Task.Delay(25);
    }

    await Task.Yield();
}

record struct Rect(int X, int Y, int Width, int Height)
{
    public int Area => Width * Height;

    public bool Contains(int x, int y) => x > X && x < X + Width && y > Y && y < Y + Height;

    public double GetDistance(int x, int y)
    {
        var dx = Math.Max(Math.Max(X - x, 0), x - (X + Width));
        var dy = Math.Max(Math.Max(Y - y, 0), y - (Y + Height));
        return Math.Sqrt(dx * dx + dy * dy);
    }
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

    public const int MOUSEEVENTF_MOVE = 0x0001;
    public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const int MOUSEEVENTF_LEFTUP = 0x0004;
    public const int MOUSEEVENTF_RIGHTDOWN = 0x0008;

    [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
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
