using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tesseract;

namespace Civ6EnglishDemo;

internal record OcrWord(string Text, int X, int Y, int Width, int Height);
internal record OcrResult(string Text, List<OcrWord> Words, double Milliseconds, float Confidence, long WorkingSet, long PrivateBytes);

internal static class OcrService
{
    internal static OcrResult Recognize(BitmapSource image, bool preprocess)
    {
        var watch = Stopwatch.StartNew();
        if ((long)image.PixelWidth * image.PixelHeight > 12_000_000)
            throw new InvalidOperationException("选区太大，请框选一段文字（不超过 1200 万像素）。");
        BitmapSource input = image;
        if (preprocess)
        {
            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int count = image.PixelWidth * image.PixelHeight;
            var bytes = new byte[count * 4]; bgra.CopyPixels(bytes, image.PixelWidth * 4, 0);
            var gray = new byte[count]; long total = 0;
            for (int i = 0; i < count; i++)
            {
                int j = i * 4;
                // Composite transparent pixels on white before luminance conversion.
                int lum = (bytes[j] * 29 + bytes[j + 1] * 150 + bytes[j + 2] * 77) >> 8;
                gray[i] = (byte)((lum * bytes[j + 3] + 255 * (255 - bytes[j + 3])) / 255);
                total += gray[i];
            }
            if (total / count < 128) for (int i = 0; i < count; i++) gray[i] = (byte)(255 - gray[i]);
            input = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Gray8, null, gray, image.PixelWidth);
            input = new TransformedBitmap(input, new ScaleTransform(2, 2));
        }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(input));
        using var stream = new MemoryStream(); encoder.Save(stream);
        var model = Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (!File.Exists(Path.Combine(model, "eng.traineddata"))) throw new FileNotFoundException("缺少英文识别模型，请从完整交付包恢复 tessdata 文件夹。");
        // Deliberately dispose the native engine each run: the demo measures cold initialization
        // and releases its memory after each user-triggered recognition.
        using var engine = new TesseractEngine(model, "eng", EngineMode.LstmOnly);
        using var pix = Pix.LoadFromMemory(stream.ToArray());
        using var page = engine.Process(pix, PageSegMode.Auto);
        var words = new List<OcrWord>();
        using var iterator = page.GetIterator(); iterator.Begin();
        int divisor = preprocess ? 2 : 1;
        do
        {
            var text = iterator.GetText(PageIteratorLevel.Word)?.Trim();
            if (!string.IsNullOrWhiteSpace(text) && iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var box))
                words.Add(new(text, box.X1 / divisor, box.Y1 / divisor, box.Width / divisor, box.Height / divisor));
        } while (iterator.Next(PageIteratorLevel.Word));
        watch.Stop(); using var process = Process.GetCurrentProcess(); process.Refresh();
        return new(page.GetText().Trim(), words, watch.Elapsed.TotalMilliseconds, page.GetMeanConfidence(), process.WorkingSet64, process.PrivateMemorySize64);
    }
}
