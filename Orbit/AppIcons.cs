using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitWeave.Actions;

namespace OrbitWeave.Orbit;

// Icônes réelles des boutons : extraites sur un fil STA à part (jamais sur celui de la roue),
// gardées en mémoire et sur disque (PNG) pour que la roue s'ouvre vite. Rien ne lève d'exception :
// une icône illisible donne null, et la carte garde son symbole.
public static class AppIcons
{
    private const int Size = 64;
    private static readonly ConcurrentDictionary<IconTarget, Task<ImageSource?>> Loaded = new();
    private static readonly HttpClient Http = CreateHttp();

    // Certains sites (Wikipédia…) refusent une requête sans nom d'application.
    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitWeave/1.0");
        return http;
    }
    private static readonly Lazy<StaWorker> Worker = new(() => new StaWorker());

    public static string CacheDirectory => DiagnosticFlags.DataDirectory is { } data
        ? Path.Combine(data, "IconCache")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitWeave", "IconCache");

    public static string CacheKey(IconTarget target, DateTime? lastWrite)
    {
        var text = $"{target.Kind}|{target.Value.ToLowerInvariant()}|{lastWrite?.ToUniversalTime().Ticks ?? 0}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public static bool TryGetLoaded(IconTarget target, out ImageSource? image)
    {
        image = null;
        if (target.Kind == IconKind.None) return true;
        if (!Loaded.TryGetValue(target, out var task) || !task.IsCompletedSuccessfully) return false;
        image = task.Result;
        return true;
    }

    public static Task<ImageSource?> LoadAsync(IconTarget target) =>
        target.Kind == IconKind.None ? Task.FromResult<ImageSource?>(null) : Loaded.GetOrAdd(target, Load);

    private static async Task<ImageSource?> Load(IconTarget target)
    {
        try
        {
            if (target.Kind == IconKind.Image) return await Task.Run(() => ImageFile(target.Value)).ConfigureAwait(false);
            var lastWrite = target.Kind == IconKind.Shell ? LastWrite(target.Value) : null;
            var key = CacheKey(target, lastWrite);
            var png = Path.Combine(CacheDirectory, key + ".png");
            var none = Path.Combine(CacheDirectory, key + ".none");
            if (File.Exists(png) && ReadPng(png) is { } cached) return cached;
            if (File.Exists(none) && DateTime.UtcNow - File.GetLastWriteTimeUtc(none) < TimeSpan.FromDays(7)) return null;
            var image = target.Kind == IconKind.Site ? await Favicon(target.Value).ConfigureAwait(false)
                : await Worker.Value.Run(() => ShellImage(target.Value)).ConfigureAwait(false);
            Store(image, png, none);
            return image;
        }
        catch (Exception) { return null; }
    }

    private static DateTime? LastWrite(string path)
    {
        try
        {
            if (File.Exists(path)) return File.GetLastWriteTimeUtc(path);
            if (Directory.Exists(path)) return Directory.GetLastWriteTimeUtc(path);
        }
        catch (Exception) { }
        return null;
    }

    private static BitmapSource? ReadPng(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception) { return null; }
    }

    private static void Store(BitmapSource? image, string png, string none)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            if (image is null) { File.WriteAllText(none, ""); return; }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            var temp = png + ".tmp";
            using (var stream = File.Create(temp)) encoder.Save(stream);
            File.Move(temp, png, true);
        }
        catch (Exception) { }
    }

    // Image choisie par l'utilisateur : la plus grande image du fichier (une .ico en contient plusieurs), réduite à 64 px.
    private static BitmapSource? ImageFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            return Shrink(decoder.Frames.OrderByDescending(f => f.PixelWidth).First());
        }
        catch (Exception) { return null; }
    }

    private static BitmapSource Shrink(BitmapSource frame)
    {
        var largest = Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (largest > Size)
            frame = new TransformedBitmap(frame, new ScaleTransform((double)Size / largest, (double)Size / largest));
        var copy = new WriteableBitmap(new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0));
        copy.Freeze();
        return copy;
    }

    // Favicon du site : favicon.ico puis apple-touch-icon.png ; la plus grande image, réduite à 64 px.
    private static async Task<BitmapSource?> Favicon(string site)
    {
        foreach (var name in new[] { "favicon.ico", "apple-touch-icon.png" })
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(site + name).ConfigureAwait(false);
                using var stream = new MemoryStream(bytes);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                if (frame.PixelWidth > Size)
                    frame = new TransformedBitmap(frame, new ScaleTransform((double)Size / frame.PixelWidth, (double)Size / frame.PixelHeight));
                var copy = new WriteableBitmap(new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0));
                copy.Freeze();
                return copy;
            }
            catch (Exception) { }
        }
        return null;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Type, Width, Height, WidthBytes;
        public short Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr handle, int size, out BitmapInfo info);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    // Exe, raccourci, dossier, appli du Store (shell:AppsFolder\…) : l'icône que montre l'Explorateur.
    private static BitmapSource? ShellImage(string parsingName)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory) != 0 || factory is null) return null;
        try
        {
            const int BiggerSizeOk = 0x1, IconOnly = 0x4;
            if (factory.GetImage(new NativeSize { Width = Size, Height = Size }, BiggerSizeOk | IconOnly, out var handle) != 0 || handle == IntPtr.Zero)
                return null;
            try { return FromHBitmap(handle); }
            finally { DeleteObject(handle); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    private static BitmapSource? FromHBitmap(IntPtr handle)
    {
        if (GetObject(handle, Marshal.SizeOf<BitmapInfo>(), out var info) == 0 || info.Width <= 0 || info.Height <= 0) return null;
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = info.Width, Height = -info.Height, Planes = 1, BitCount = 32
        };
        var stride = info.Width * 4;
        var pixels = new byte[stride * info.Height];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, handle, 0, (uint)info.Height, pixels, ref header, 0) == 0) return null;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
        // Vieille icône sans transparence : alpha tout à zéro, on la rend opaque.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { hasAlpha = true; break; }
        if (!hasAlpha) for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var bitmap = BitmapSource.Create(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    // Un seul fil STA pour les appels Shell, servis dans l'ordre.
    private sealed class StaWorker
    {
        private readonly BlockingCollection<Action> _queue = new();

        public StaWorker()
        {
            var thread = new Thread(() => { foreach (var work in _queue.GetConsumingEnumerable()) work(); })
            { IsBackground = true, Name = "OrbitWeave icônes" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public Task<T> Run<T>(Func<T> work)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try { result.SetResult(work()); }
                catch (Exception ex) { result.SetException(ex); }
            });
            return result.Task;
        }
    }
}
