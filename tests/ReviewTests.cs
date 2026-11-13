using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CustomIconDashboarderPlugin;
using KeePassLib;
using KeePassLib.Security;

internal static partial class Program
{
    private static void RunEnhancedTests(string[] args)
    {
        Run("columns retain readable headers after narrowing", () => Fixture((form, host, db, context) =>
        {
            SeedDashboard(form, db);
            var list = (ListView)Get(form, "m_lvViewIcon");
            int[] before = new int[list.Columns.Count];
            for (int i = 0; i < before.Length; i++) before[i] = list.Columns[i].Width;
            form.Width = 950;
            for (int i = 0; i < before.Length; i++) Assert(list.Columns[i].Width == before[i] && before[i] >= TextRenderer.MeasureText(list.Columns[i].Text, list.Font).Width + 20);
        }));
        Run("selecting an icon preserves manually widened main columns", () => Fixture((form, host, db, context) =>
        {
            SeedDashboard(form, db);
            var entries = (ListView)Get(form, "m_lvAllEntries"); entries.Columns[4].Width = 700;
            using (var image = IconImage())
                Call(form, "UpdateSelectedIconPane", image, db.RootGroup.GetEntries(true).GetEnumerator(), null, false, 0);
            Assert(entries.Columns[4].Width == 700);
        }));
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            float captured = scale;
            Run("window geometry fits narrow working area at scale " + scale, () => Fixture((form, host, db, context) =>
            {
                Set(form, "m_layoutScale", captured);
                host.CustomConfig.SetString("KPCID.dashboardPosition", "-9000x7000");
                host.CustomConfig.SetString("KPCID.dashboardSize", "300x250");
                Rectangle area = new Rectangle(-1280, 0, 1280, 720);
                Call(form, "RestoreReadableWindowForArea", area);
                Assert(area.Contains(form.Bounds));
                var split = (SplitContainer)Get(form, "spc_mainSplitter");
                Assert(split.SplitterDistance >= split.Panel1MinSize && split.Panel2.Width >= split.Panel2MinSize);
            }));
        }
        Run("multi-entry custom and standard assignments affect every chosen row", () => Fixture((form, host, db, context) =>
        {
            var first = AddEntry(db); var second = AddEntry(db); var untouched = AddEntry(db);
            var custom = new PwUuid(true);
            Assert((int)Call(form, "ApplyEntryIcons", new[] { first, second }, PwIcon.Key, custom) == 2);
            Assert(first.CustomIconUuid.Equals(custom) && second.CustomIconUuid.Equals(custom) && untouched.CustomIconUuid.Equals(PwUuid.Zero));
            Assert((int)Call(form, "ApplyEntryIcons", new[] { first, second }, PwIcon.World, PwUuid.Zero) == 2);
            Assert(first.IconId == PwIcon.World && second.IconId == PwIcon.World && untouched.IconId == PwIcon.Key);
        }));
        Run("exact-size preview sources survive repeated replacement and close", () => Fixture((form, host, db, context) =>
        {
            using (var image = new Bitmap(128, 128))
            {
                var method = typeof(DashboarderForm).GetMethod("SetPreview", BindingFlags.Static | BindingFlags.NonPublic);
                var box = (PictureBox)Get(form, "pbo_selectedIcon128");
                for (int i = 0; i < 100; i++) method.Invoke(null, new object[] { box, image, 128 });
                form.Dispose();
                Assert(image.Width == 128); using (Graphics g = Graphics.FromImage(image)) g.Clear(Color.White);
            }
        }));
        Run("empty download selection clears previews and rows", () => Fixture((form, host, db, context) =>
        {
            var finder = BestIconFinder.BuildInstanceWithNoURLResult();
            finder.ListImageInfo.Add(new ImageInfo(new Bitmap(64, 64), "https://example.test/icon.png"));
            var chooser = new IconChooser(finder) { ChoosenIndex = 0 };
            Call(form, "UpdateAllDownloadIconPanel", chooser);
            Assert(((ListView)Get(form, "m_lvDownloadResult")).Items.Count == 1);
            Call(form, "UpdateAllDownloadIconPanel", new object[] { null });
            Assert(((ListView)Get(form, "m_lvDownloadResult")).Items.Count == 0 && ((PictureBox)Get(form, "pbo_downloadedIcon128")).BackgroundImage == null);
            finder.Dispose();
        }));
        Run("download worker concurrency is bounded", () => Fixture((form, host, db, context) =>
        {
            Call(form, "ResetThreadManagement", 12);
            int active = 0, maximum = 0, completed = 0;
            using (var release = new ManualResetEventSlim())
            {
                Action<ListViewItem> work = row =>
                {
                    int now = Interlocked.Increment(ref active);
                    int previous;
                    do { previous = maximum; if (previous >= now) break; } while (Interlocked.CompareExchange(ref maximum, now, previous) != previous);
                    release.Wait(); Interlocked.Decrement(ref active); Interlocked.Increment(ref completed);
                };
                for (int i = 0; i < 12; i++) Call(form, "QueueBatchWork", work, new ListViewItem("synthetic"));
                context.Until(() => active == 4);
                Assert(maximum == 4);
                release.Set(); context.Until(() => completed == 12 && (int)Get(form, "m_scheduledWorkers") == 0);
                Assert(maximum == 4);
            }
        }));
        Run("IPv6 and explicit origin ports are preserved", () =>
        {
            using (var finder = new BestIconFinder(new Uri("https://[::1]:8443/account/login"))) Assert(finder.SiteUri.Port == 8443 && finder.SiteUri.Host == "[::1]");
            using (var finder = new BestIconFinder(new Uri("https://example.test:9443/path"))) Assert(finder.SiteUri.Port == 9443);
            for (int i = 0; i < 10; i++) { try { new BestIconFinder(null); } catch (ArgumentNullException) { } }
            GC.Collect(); GC.WaitForPendingFinalizers();
        });
        Type downloader = typeof(BestIconFinder).Assembly.GetType("CustomIconDashboarderPlugin.SafeIconDownloader", true);
        MethodInfo resolve = downloader.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (string reference in new[] { "file:///C:/synthetic/icon.png", "file://synthetic-share/icon.png", "ftp://example.test/icon.png", "\\\\synthetic-share\\icon.png", "https://user:secret@example.test/icon.png", "http://example.test/insecure.png" })
        {
            string captured = reference;
            Run("reject unsafe favicon reference " + reference.Split(':')[0], () => Assert(resolve.Invoke(null, new object[] { new Uri("https://example.test/Page/"), captured }) == null));
        }
        Run("relative favicon links preserve path and case", () =>
        {
            var uri = (Uri)resolve.Invoke(null, new object[] { new Uri("https://example.test/Account/"), "Icons/Logo.PNG?Token=AbC" });
            Assert(uri.AbsoluteUri == "https://example.test/Account/Icons/Logo.PNG?Token=AbC");
        });
        Run("chunked oversized responses are rejected", () =>
        {
            using (var client = new HttpClient(new TestHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamingContent(new byte[33]) })))
            using (var target = NewDownloader(downloader, client))
                ExpectFailure(() => Download(downloader, target, new Uri("https://example.test/"), 32));
        });
        foreach (string redirect in new[] { "file:///C:/synthetic/icon.png", "http://example.test/icon.png", "https://user:secret@example.test/icon.png" })
        {
            string captured = redirect;
            Run("redirect destination is validated before I/O", () =>
            {
                int requests = 0;
                using (var client = new HttpClient(new TestHandler(r => { requests++; var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri(captured); return response; })))
                using (var target = NewDownloader(downloader, client)) { ExpectFailure(() => Download(downloader, target, new Uri("https://example.test/"), 32)); Assert(requests == 1); }
            });
        }
        Run("a body stream ignoring cancellation is disposed within the deadline", () =>
        {
            var stream = new SlowStream();
            using (var client = new HttpClient(new TestHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new SlowContent(stream, false) })))
            using (var target = NewDownloader(downloader, client))
            {
                downloader.GetField("_requestTimeout", Private).SetValue(target, TimeSpan.FromMilliseconds(80));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                ExpectFailure(() => Download(downloader, target, new Uri("https://example.test/"), 32));
                Assert(watch.Elapsed < TimeSpan.FromSeconds(2) && stream.Disposed);
            }
        });
        Run("a delayed response stream cannot bypass its deadline", () =>
        {
            var stream = new SlowStream(); var content = new SlowContent(stream, true);
            using (var client = new HttpClient(new TestHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = content })))
            using (var target = NewDownloader(downloader, client))
            {
                downloader.GetField("_requestTimeout", Private).SetValue(target, TimeSpan.FromMilliseconds(80));
                ExpectFailure(() => Download(downloader, target, new Uri("https://example.test/"), 32));
                content.Opened.TrySetResult(stream);
                Assert(SpinWait.SpinUntil(() => stream.Disposed, 1000));
            }
        });
        Run("closing cancels a body read that ignores cancellation", () => Fixture((form, host, db, context) =>
        {
            var stream = new SlowStream();
            using (var finder = InjectFinder(downloader, r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new SlowContent(stream, false) }))
            {
                ((HashSet<BestIconFinder>)Get(form, "m_activeDownloads")).Add(finder);
                Task work = Task.Run(() => finder.TestImageUri(new Uri("https://example.test/icon.png")));
                context.Until(() => stream.Entered);
                form.Dispose(); context.Until(() => work.IsCompleted); work.GetAwaiter().GetResult();
                Assert(stream.Disposed);
            }
        }));
        Run("Stop before registration prevents any network request", () => Fixture((form, host, db, context) =>
        {
            int requests = 0;
            using (var finder = InjectFinder(downloader, r => { requests++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }; }))
            {
                Call(form, "ResetThreadManagement", 1); Call(form, "InitiateThreadStopping");
                Call(form, "FindBestIconForDashboard", finder);
                Assert(requests == 0 && ((HashSet<BestIconFinder>)Get(form, "m_activeDownloads")).Count == 0);
            }
        }));
        foreach (bool shared in new[] { false, true })
        {
            bool captured = shared;
            Run("Stop during find-and-apply never restarts download: " + (shared ? "shared icon" : "entry"), () => Fixture((form, host, db, context) =>
            {
                SeedDashboard(form, db); Call(form, "ResetThreadManagement", 1);
                var list = (ListView)Get(form, captured ? "m_lvViewIcon" : "m_lvAllEntries");
                var entry = db.RootGroup.Entries.GetAt(0); var original = entry.CustomIconUuid;
                int requests = 0, factories = 0; var stream = new SlowStream();
                Set(form, "m_createIconFinder", (Func<Uri, BestIconFinder>)(uri =>
                {
                    Interlocked.Increment(ref factories);
                    return InjectFinder(downloader, r => { Interlocked.Increment(ref requests); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new SlowContent(stream, false) }; });
                }));
                string method = captured ? "UpdateBestIconAndPickIcon" : "UpdateBestIconAndPickIconForEntries";
                Task work = Task.Run(() => Call(form, method, list.Items[0]));
                context.Until(() => stream.Entered); Call(form, "InitiateThreadStopping");
                context.Until(() => work.IsCompleted); work.GetAwaiter().GetResult();
                Assert(factories == 1 && requests == 1 && stream.Disposed && entry.CustomIconUuid.Equals(original));
                Assert(((Dictionary<PwEntry, IconChooser>)Get(form, "m_iconChooserIndexerFromEntry")).Count == 0);
                Assert(((Dictionary<PwUuid, IconChooser>)Get(form, "m_iconChooserIndexerFromIcon")).Count == 0);
            }));
        }
        Run("a stale URL result releases its reservation for a retry", () => Fixture((form, host, db, context) =>
        {
            SeedDashboard(form, db); Call(form, "ResetThreadManagement", 1);
            var list = (ListView)Get(form, "m_lvAllEntries"); var entry = (PwEntry)list.Items[0].Tag;
            using (var release = new ManualResetEventSlim())
            {
                var entered = new ManualResetEventSlim();
                Set(form, "m_createIconFinder", (Func<Uri, BestIconFinder>)(uri => { entered.Set(); release.Wait(); return BestIconFinder.BuildInstanceWithNoURLResult(); }));
                Task work = Task.Run(() => Call(form, "UpdateBestIconFinderAndLviFromEntryLvi", list.Items[0]));
                context.Until(() => entered.IsSet);
                entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://example.test/changed")); release.Set();
                context.Until(() => work.IsCompleted); work.GetAwaiter().GetResult();
                Assert(!((Dictionary<PwEntry, IconChooser>)Get(form, "m_iconChooserIndexerFromEntry")).ContainsKey(entry));
                Assert(list.Items[0].SubItems[2].Text == "");
                entered.Dispose();
            }
        }));
        Run("deep HTML nesting is rejected safely", () =>
        {
            string html = string.Concat(System.Linq.Enumerable.Repeat("<div>", 300)) + "icon" + string.Concat(System.Linq.Enumerable.Repeat("</div>", 300));
            using (var finder = new BestIconFinder(new Uri("https://example.test/")))
            using (var client = new HttpClient(new TestHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) })))
            {
                var target = NewDownloader(downloader, client);
                typeof(BestIconFinder).GetField("client", Private).SetValue(finder, target);
                finder.GetIconLinks(); Assert(finder.Result.ResultCode != FinderResult.RESULT_OK);
            }
        });
        Run("oversized PNG and truncated ICO fail before decoding", () =>
        {
            Type limits = typeof(BestIconFinder).Assembly.GetType("CustomIconDashboarderPlugin.IconImageLimits", true);
            MethodInfo validate = limits.GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic);
            byte[] png = new byte[24]; png[0] = 137; png[1] = 80; png[2] = 78; png[3] = 71; png[16] = 127; png[20] = 127;
            ExpectFailure(() => validate.Invoke(null, new object[] { png }));
            byte[] ico = new byte[22]; ico[2] = 1; ico[4] = 1; ico[14] = 255;
            ExpectFailure(() => validate.Invoke(null, new object[] { ico }));
        });
        Run("embedded JPEG cannot masquerade as an ICO bitmap header", () =>
        {
            byte[] ico = new byte[70]; ico[2] = 1; ico[4] = 1;
            Array.Copy(BitConverter.GetBytes(48u), 0, ico, 14, 4); Array.Copy(BitConverter.GetBytes(22u), 0, ico, 18, 4);
            ico[22] = 255; ico[23] = 216; ico[24] = 255; ico[25] = 224;
            Array.Copy(BitConverter.GetBytes(2048), 0, ico, 26, 4); Array.Copy(BitConverter.GetBytes(2), 0, ico, 30, 4);
            ValidateRejectedImage(ico);
        });
        Run("overlapping ICO payloads are rejected before decoder allocation", () =>
        {
            byte[] ico = new byte[78]; ico[2] = 1; ico[4] = 2;
            for (int i = 0; i < 2; i++) { Array.Copy(BitConverter.GetBytes(40u), 0, ico, 14 + i * 16, 4); Array.Copy(BitConverter.GetBytes(38u), 0, ico, 18 + i * 16, 4); }
            Array.Copy(BitConverter.GetBytes(40), 0, ico, 38, 4); Array.Copy(BitConverter.GetBytes(16), 0, ico, 42, 4); Array.Copy(BitConverter.GetBytes(32), 0, ico, 46, 4);
            ValidateRejectedImage(ico);
        });
        Run("a valid PNG is downloaded and decoded through the bounded path", () =>
        {
            byte[] bytes; using (var image = new Bitmap(32, 32)) using (var stream = new MemoryStream()) { image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); bytes = stream.ToArray(); }
            using (var finder = new BestIconFinder(new Uri("https://example.test/")))
            using (var client = new HttpClient(new TestHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })))
            {
                ((IDisposable)typeof(BestIconFinder).GetField("client", Private).GetValue(finder)).Dispose();
                typeof(BestIconFinder).GetField("client", Private).SetValue(finder, NewDownloader(downloader, client));
                finder.TestImageUri(new Uri("https://example.test/icon.png"));
                Assert(finder.BestImage != null && finder.ListImageInfo.Count == 1 && finder.BestImage.Width == 32);
            }
        });
        Run("late response headers cannot bypass the request deadline", () =>
        {
            var headers = new TaskCompletionSource<HttpResponseMessage>();
            var content = new SlowContent(new SlowStream(), false);
            using (var client = new HttpClient(new DelayedHandler(headers.Task)))
            using (var target = NewDownloader(downloader, client))
            {
                downloader.GetField("_requestTimeout", Private).SetValue(target, TimeSpan.FromMilliseconds(80));
                ExpectFailure(() => Download(downloader, target, new Uri("https://example.test/"), 32));
                headers.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
                Assert(SpinWait.SpinUntil(() => content.Disposed, 1000));
            }
        });
        Run("Dashboarder leaves process TLS policy unchanged", () => { var policy = ServicePointManager.SecurityProtocol; BestIconFinder.InitClass(); Assert(ServicePointManager.SecurityProtocol == policy); });
        if (args.Length >= 2 && args[0] == "--preview")
            Fixture((form, host, db, context) => RenderPreview(form, db, args[1], args.Length > 2 ? args[2] : null), true);
    }

    private static void SeedDashboard(DashboarderForm form, PwDatabase db)
    {
        db.RootGroup.Name = "Demonstration vault";
        for (int i = 0; i < 8; i++)
        {
            byte[] data;
            using (var image = new Bitmap(64, 64))
            using (Graphics graphics = Graphics.FromImage(image))
            using (var stream = new MemoryStream())
            {
                graphics.Clear(Color.FromArgb(255, 40 + i * 20, 100, 200 - i * 14));
                graphics.DrawString(((char)('A' + i)).ToString(), SystemFonts.CaptionFont, Brushes.White, 20, 20);
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); data = stream.ToArray();
            }
            var icon = new PwCustomIcon(new PwUuid(true), data); db.CustomIcons.Add(icon);
            var entry = AddEntry(db); entry.CustomIconUuid = icon.Uuid;
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "Example " + (i % 2 == 0 ? "customer portal" : "workspace account") + " " + (i + 1)));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "sample.user"));
            entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://example.test/workspace/" + (i + 1)));
        }
        Call(form, "InitEx");
        ((ComboBox)Get(form, "cbo_iconActionSelector")).SelectedIndex = 0;
        ((ComboBox)Get(form, "cbo_entryActionSelector")).SelectedIndex = 0;
    }

    private static void RenderPreview(DashboarderForm form, PwDatabase db, string path, string themePath)
    {
        SeedDashboard(form, db);
        var icons = (ListView)Get(form, "m_lvViewIcon"); var handle = icons.Handle;
        icons.Items[0].Selected = true;
        Call(form, "OnLvViewIconSelectedIndexChanged", icons, EventArgs.Empty);
        object styler = null;
        if (themePath != null)
        {
            Assembly theme = Assembly.LoadFrom(Path.GetFullPath(themePath));
            Type palette = theme.GetType("FluentTheme.Palette", true);
            object colours = palette.GetMethod("Create").Invoke(null, new object[] { true, Color.CornflowerBlue });
            Type styling = theme.GetType("FluentTheme.DialogStyler", true);
            styler = Activator.CreateInstance(styling, new object[] { null });
            styling.GetMethod("Update").Invoke(styler, new object[] { colours, 1f });
            styling.GetMethod("Style").Invoke(styler, new object[] { form });
        }
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000);
        form.Show();
        form.PerformLayout();
        form.Update();
        using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        form.Hide();
        if (styler != null) styler.GetType().GetMethod("Detach").Invoke(styler, null);
    }

    private sealed class PreviewDashboarder : DashboarderForm
    {
        internal PreviewDashboarder(FakeHost host) : base(host) { }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnLoad(EventArgs e) { } // Already initialised using synthetic fixture data.
    }

    private static void ValidateRejectedImage(byte[] data)
    {
        Type limits = typeof(BestIconFinder).Assembly.GetType("CustomIconDashboarderPlugin.IconImageLimits", true);
        ExpectFailure(() => limits.GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { data }));
    }

    private static BestIconFinder InjectFinder(Type downloader, Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var finder = new BestIconFinder(new Uri("https://example.test/"));
        ((IDisposable)typeof(BestIconFinder).GetField("client", Private).GetValue(finder)).Dispose();
        typeof(BestIconFinder).GetField("client", Private).SetValue(finder, NewDownloader(downloader, new HttpClient(new TestHandler(handler))));
        return finder;
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        private readonly Task<HttpResponseMessage> _response;
        internal DelayedHandler(Task<HttpResponseMessage> response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => _response;
    }
    private static IDisposable NewDownloader(Type type, HttpClient client) => (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { client }, null);
    private static byte[] Download(Type type, object target, Uri uri, int limit) => (byte[])type.GetMethod("Download", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, new object[] { uri, uri, limit, null });
    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            for (Exception current = error; current != null; current = current.InnerException)
                if (current is WebException || current is InvalidDataException) return;
            throw;
        }
        throw new Exception("Expected safe rejection.");
    }
    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
        internal TestHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(_response(request));
    }
    private sealed class SlowStream : Stream
    {
        internal volatile bool Disposed, Entered;
        private readonly TaskCompletionSource<int> _pending = new TaskCompletionSource<int>();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { Entered = true; return _pending.Task; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; _pending.TrySetException(new IOException("synthetic closed stream")); base.Dispose(disposing); }
    }
    private sealed class SlowContent : HttpContent
    {
        internal volatile bool Disposed;
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        internal readonly TaskCompletionSource<Stream> Opened = new TaskCompletionSource<Stream>();
        private readonly Stream _stream; private readonly bool _delay;
        internal SlowContent(Stream stream, bool delay) { _stream = stream; _delay = delay; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => throw new NotSupportedException();
        protected override Task<Stream> CreateContentReadStreamAsync() => _delay ? Opened.Task : Task.FromResult(_stream);
    }
    private sealed class StreamingContent : HttpContent
    {
        private readonly byte[] _bytes;
        internal StreamingContent(byte[] bytes) => _bytes = bytes;
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => stream.WriteAsync(_bytes, 0, _bytes.Length);
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(_bytes));
    }
}
