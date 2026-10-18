using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Civ6EnglishDemo;

internal static class Native
{
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr source, int sx, int sy, uint mode);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    internal static BitmapSource Capture()
    {
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77), w = GetSystemMetrics(78), h = GetSystemMetrics(79);
        if (w <= 0 || h <= 0 || (long)w * h > 100_000_000) throw new InvalidOperationException("屏幕尺寸超出截图限制。");
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new Win32Exception();
        IntPtr mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            mem = CreateCompatibleDC(dc); bmp = CreateCompatibleBitmap(dc, w, h);
            if (mem == IntPtr.Zero || bmp == IntPtr.Zero) throw new Win32Exception();
            old = SelectObject(mem, bmp);
            if (!BitBlt(mem, 0, 0, w, h, dc, x, y, 0x40CC0020)) throw new Win32Exception();
            var image = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze(); return image;
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, dc);
        }
    }
}

internal sealed class SelectionWindow : Window
{
    readonly BitmapSource source;
    readonly Canvas canvas = new();
    readonly Rectangle selection = new() { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35, 0, 200, 255)) };
    Point start;
    bool dragging;
    internal BitmapSource? SelectedImage { get; private set; }

    internal SelectionWindow(BitmapSource image)
    {
        source = image;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowInTaskbar = false; Cursor = Cursors.Cross;
        var root = new Grid { Background = Brushes.Black };
        root.Children.Add(new Image { Source = image, Stretch = Stretch.Fill, Opacity = .85 });
        root.Children.Add(canvas);
        canvas.Children.Add(selection);
        root.Children.Add(new TextBlock
        {
            Text = "拖动框选一句话或一段文字 · Esc / 右键取消",
            Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(210, 20, 30, 45)),
            Padding = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 18, 0, 0), IsHitTestVisible = false
        });
        Content = root;
        SourceInitialized += (_, _) => Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1),
            Native.GetSystemMetrics(76), Native.GetSystemMetrics(77), Native.GetSystemMetrics(78), Native.GetSystemMetrics(79), 0x0040);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        MouseRightButtonDown += (_, _) => DialogResult = false;
        MouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(canvas); dragging = true; canvas.CaptureMouse();
            Canvas.SetLeft(selection, start.X); Canvas.SetTop(selection, start.Y); selection.Width = selection.Height = 0;
        };
        MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = Clamp(e.GetPosition(canvas));
            Canvas.SetLeft(selection, Math.Min(start.X, p.X)); Canvas.SetTop(selection, Math.Min(start.Y, p.Y));
            selection.Width = Math.Abs(start.X - p.X); selection.Height = Math.Abs(start.Y - p.Y);
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            dragging = false; canvas.ReleaseMouseCapture();
            var p = Clamp(e.GetPosition(canvas));
            var rect = MapCrop(start, p, canvas.ActualWidth, canvas.ActualHeight, source.PixelWidth, source.PixelHeight);
            if (rect.Width < 5 || rect.Height < 5) return;
            // Copy only the selected pixels so the retained preview does not keep the
            // full multi-monitor screenshot alive through CroppedBitmap's source reference.
            var cropped = new FormatConvertedBitmap(new CroppedBitmap(source, rect), PixelFormats.Bgra32, null, 0);
            int stride = rect.Width * 4; var bytes = new byte[stride * rect.Height];
            cropped.CopyPixels(bytes, stride, 0);
            SelectedImage = BitmapSource.Create(rect.Width, rect.Height, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
            SelectedImage.Freeze(); DialogResult = true;
        };
    }

    Point Clamp(Point p) => new(Math.Clamp(p.X, 0, canvas.ActualWidth), Math.Clamp(p.Y, 0, canvas.ActualHeight));

    internal static Int32Rect MapCrop(Point a, Point b, double width, double height, int pixelsX, int pixelsY)
    {
        int x1 = Math.Clamp((int)Math.Floor(Math.Min(a.X, b.X) / width * pixelsX), 0, pixelsX);
        int y1 = Math.Clamp((int)Math.Floor(Math.Min(a.Y, b.Y) / height * pixelsY), 0, pixelsY);
        int x2 = Math.Clamp((int)Math.Ceiling(Math.Max(a.X, b.X) / width * pixelsX), 0, pixelsX);
        int y2 = Math.Clamp((int)Math.Ceiling(Math.Max(a.Y, b.Y) / height * pixelsY), 0, pixelsY);
        return new Int32Rect(x1, y1, x2 - x1, y2 - y1);
    }
}
