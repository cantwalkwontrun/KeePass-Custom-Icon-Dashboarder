using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CustomIconDashboarderPlugin;
using KeePass.App.Configuration;
using KeePass.DataExchange;
using KeePass.Ecas;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePass.Util;
using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.PasswordGenerator;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _passed, _failed;

    [STAThread]
    private static void Main()
    {
        Run("worker entry changes and events stay on UI thread", () => Fixture((form, host, db, context) =>
        {
            var entry = AddEntry(db); var password = entry.Strings.GetSafe(PwDefs.PasswordField);
            int ui = Thread.CurrentThread.ManagedThreadId, eventThread = 0;
            EventHandler<ObjectTouchedEventArgs> touched = (s, e) => { if (ReferenceEquals(e.Object, entry)) eventThread = Thread.CurrentThread.ManagedThreadId; };
            PwEntry.EntryTouched += touched;
            try
            {
                using (var image = IconImage())
                {
                    PwCustomIcon result = null;
                    Task worker = Task.Run(() => result = (PwCustomIcon)Call(form, "UpdateEntryFromImage", entry, image, db));
                    context.Until(() => worker.IsCompleted); worker.GetAwaiter().GetResult();
                    Assert(result != null && entry.CustomIconUuid.Equals(result.Uuid) && eventThread == ui);
                    Assert(ReferenceEquals(password, entry.Strings.GetSafe(PwDefs.PasswordField)));
                    Assert(entry.HasTag("pwned") && entry.CustomData.Get("pwned-count") == "42" && db.Modified && db.UINeedsIconUpdate);
                }
            }
            finally { PwEntry.EntryTouched -= touched; }
        }));
        Run("shared-icon replacement updates live entries and groups", () => Fixture((form, host, db, context) =>
        {
            var first = AddEntry(db); var second = AddEntry(db); PwCustomIcon old;
            using (var image = new Bitmap(4, 4))
            using (var stream = new System.IO.MemoryStream()) { image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); old = new PwCustomIcon(new PwUuid(true), stream.ToArray()); }
            db.CustomIcons.Add(old); first.CustomIconUuid = second.CustomIconUuid = db.RootGroup.CustomIconUuid = old.Uuid;
            var stats = new IconStatsHandler(); stats.Initialize(db); Set(form, "m_iconCounter", stats);
            using (var image = IconImage())
            {
                PwCustomIcon result = null;
                Task worker = Task.Run(() => result = (PwCustomIcon)Call(form, "UpdateCustomIconFromImage", old, image, db));
                context.Until(() => worker.IsCompleted); worker.GetAwaiter().GetResult();
                Assert(result != null && first.CustomIconUuid.Equals(result.Uuid) && second.CustomIconUuid.Equals(result.Uuid) && db.RootGroup.CustomIconUuid.Equals(result.Uuid));
            }
        }));
        foreach (string scenario in new[] { "switched", "locked", "removed", "clone", "closed", "disposed" })
        {
            string captured = scenario;
            Run("discard icon result when " + scenario, () => Fixture((form, host, db, context) =>
            {
                var entry = AddEntry(db); var originalIcon = entry.CustomIconUuid; PwEntry target = entry;
                if (captured == "switched") host.Database = NewDatabase();
                if (captured == "locked") db.Close();
                if (captured == "removed") db.RootGroup.Entries.Remove(entry);
                if (captured == "clone") target = entry.CloneDeep();
                if (captured == "closed") Call(form, "OnFormClosing", form, new FormClosingEventArgs(CloseReason.UserClosing, false));
                if (captured == "disposed") form.Dispose();
                using (var image = IconImage()) Assert(Call(form, "UpdateEntryFromImage", target, image, db) == null);
                Assert(entry.CustomIconUuid.Equals(originalIcon));
                if (host.Database != db) host.Database.Close();
            }));
        }
        Run("close discards an already queued UI mutation", () => Fixture((form, host, db, context) =>
        {
            var entry = AddEntry(db);
            using (var image = IconImage())
            {
                object result = new object();
                Task worker = Task.Run(() => result = Call(form, "UpdateEntryFromImage", entry, image, db));
                context.WaitForPost();
                Call(form, "OnFormClosing", form, new FormClosingEventArgs(CloseReason.UserClosing, false));
                context.Until(() => worker.IsCompleted); worker.GetAwaiter().GetResult();
                Assert(result == null && entry.CustomIconUuid.Equals(PwUuid.Zero) && db.CustomIcons.Count == 0);
            }
        }));
        Run("lock during an entry callback stops shared-icon updates", () => Fixture((form, host, db, context) =>
        {
            var entry = AddEntry(db); var group = db.RootGroup;
            PwCustomIcon old;
            using (var image = new Bitmap(4, 4))
            using (var stream = new System.IO.MemoryStream()) { image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); old = new PwCustomIcon(new PwUuid(true), stream.ToArray()); }
            db.CustomIcons.Add(old); entry.CustomIconUuid = group.CustomIconUuid = old.Uuid;
            var stats = new IconStatsHandler(); stats.Initialize(db); Set(form, "m_iconCounter", stats);
            EventHandler<ObjectTouchedEventArgs> touched = (s, e) => { if (ReferenceEquals(e.Object, entry)) db.Close(); };
            PwEntry.EntryTouched += touched;
            try { using (var image = IconImage()) Call(form, "UpdateCustomIconFromImage", old, image, db); }
            finally { PwEntry.EntryTouched -= touched; }
            Assert(!db.IsOpen && group.CustomIconUuid.Equals(old.Uuid));
        }));
        Run("worker failures release counters and report failure", () => Fixture((form, host, db, context) =>
        {
            Call(form, "ResetThreadManagement", 1);
            int generation = (int)Get(form, "m_batchGeneration");
            Task worker = Task.Run(() => Call(form, "RunWorker", generation, (Action)(() => { throw new InvalidOperationException("synthetic failure"); })));
            context.Until(() => worker.IsCompleted); worker.GetAwaiter().GetResult();
            Assert((int)Get(form, "nbActiveThread") == 0 && (int)Get(form, "m_failedWorkers") == 1);
            Assert(((Label)Get(form, "lbl_status")).Text.Contains("Failed: 1"));
        }));
        Run("queued jobs keep batch in progress", () => Fixture((form, host, db, context) =>
        {
            Call(form, "ResetThreadManagement", 3);
            Assert(Get(form, "batchStatus").ToString() == "InProgress" && (int)Get(form, "nbActiveThread") == 0);
        }));
        Run("old batch generation cannot start work", () => Fixture((form, host, db, context) =>
        {
            Call(form, "ResetThreadManagement", 1); int old = (int)Get(form, "m_batchGeneration");
            Call(form, "ResetThreadManagement", 1); bool ran = false;
            Task worker = Task.Run(() => Call(form, "RunWorker", old, (Action)(() => ran = true)));
            context.Until(() => worker.IsCompleted); worker.GetAwaiter().GetResult();
            Assert(!ran && (int)Get(form, "nbCreatedThread") == 0);
        }));
        Run("close does not wait for a running download", () => Fixture((form, host, db, context) =>
        {
            Call(form, "ResetThreadManagement", 1);
            int generation = (int)Get(form, "m_batchGeneration");
            using (var entered = new ManualResetEventSlim())
            using (var finish = new ManualResetEventSlim())
            {
                Task worker = Task.Run(() => Call(form, "RunWorker", generation, (Action)(() => { entered.Set(); finish.Wait(); })));
                context.Until(() => entered.IsSet);
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Call(form, "OnFormClosing", form, new FormClosingEventArgs(CloseReason.UserClosing, false));
                    Assert(watch.ElapsedMilliseconds < 1000 && !worker.IsCompleted);
                }
                finally { finish.Set(); context.Until(() => worker.IsCompleted); worker.GetAwaiter().GetResult(); }
            }
        }));
        Run("plugin termination is safe after failed initialisation", () =>
        {
            var plugin = new CustomIconDashboarderPluginExt(); Assert(!plugin.Initialize(null)); plugin.Terminate(); plugin.Terminate(); plugin.Dispose();
        });
        Console.WriteLine($"Passed: {_passed}; failed: {_failed}");
        Environment.ExitCode = _failed == 0 ? 0 : 1;
    }

    private static void Fixture(Action<DashboarderForm, FakeHost, PwDatabase, PumpContext> test)
    {
        var previous = SynchronizationContext.Current; var context = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(context);
        var db = NewDatabase(); var host = new FakeHost { Database = db };
        try
        {
            using (var form = new DashboarderForm(host))
            {
                Set(form, "m_iconChooserIndexerFromEntry", new Dictionary<PwEntry, IconChooser>());
                Set(form, "m_iconChooserIndexerFromIcon", new Dictionary<PwUuid, IconChooser>());
                test(form, host, db, context);
            }
        }
        finally { context.Drain(); db.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static Bitmap IconImage() { var image = new Bitmap(8, 8); using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.CornflowerBlue); return image; }
    private static PwEntry AddEntry(PwDatabase db) { var entry = new PwEntry(true, true); entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "synthetic-fixture")); entry.AddTag("pwned"); entry.CustomData.Set("pwned-count", "42"); db.RootGroup.AddEntry(entry, true); return entry; }
    private static PwDatabase NewDatabase() { var db = new PwDatabase(); db.New(new IOConnectionInfo { Path = "synthetic-fixture.kdbx" }, new CompositeKey()); db.Modified = false; return db; }
    private static object Call(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, Private | BindingFlags.DeclaredOnly).Invoke(instance, args);
    private static object Get(object instance, string field) => instance.GetType().GetField(field, Private).GetValue(instance);
    private static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Private).SetValue(instance, value);
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Run(string name, Action test) { try { test(); _passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException()); } }
    private sealed class PumpContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        public override void Send(SendOrPostCallback callback, object state)
        {
            using (var done = new ManualResetEventSlim())
            {
                Exception error = null;
                _queue.Enqueue(() => { try { callback(state); } catch (Exception caught) { error = caught; } finally { done.Set(); } });
                if (!done.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("UI dispatch timed out.");
                if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
        public override void Post(SendOrPostCallback callback, object state) => _queue.Enqueue(() => callback(state));
        internal void WaitForPost() { DateTime end = DateTime.UtcNow.AddSeconds(5); while (_queue.IsEmpty) { if (DateTime.UtcNow > end) throw new TimeoutException(); Thread.Sleep(1); } }
        internal void Drain() { while (_queue.TryDequeue(out Action action)) action(); }
        internal void Until(Func<bool> complete) { DateTime end = DateTime.UtcNow.AddSeconds(5); while (!complete()) { Drain(); if (DateTime.UtcNow > end) throw new TimeoutException(); Thread.Sleep(1); } Drain(); }
    }
    private sealed class FakeHost : IPluginHost
    {
        public MainForm MainWindow => null;
        public PwDatabase Database { get; set; }
        public AceCustomConfig CustomConfig { get; } = new AceCustomConfig();
        public CommandLineArgs CommandLineArgs => null;
        public CipherPool CipherPool => null;
        public KeyProviderPool KeyProviderPool => null;
        public KeyValidatorPool KeyValidatorPool => null;
        public FileFormatPool FileFormatPool => null;
        public TempFilesPool TempFilesPool => null;
        public EcasPool EcasPool => null;
        public EcasTriggerSystem TriggerSystem => null;
        public CustomPwGeneratorPool PwGeneratorPool => null;
        public ColumnProviderPool ColumnProviderPool => null;
        public ResourceManager Resources => null;
    }
}
