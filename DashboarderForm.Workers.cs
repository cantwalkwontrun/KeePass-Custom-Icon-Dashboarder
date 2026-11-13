using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using KeePassLib;

namespace CustomIconDashboarderPlugin
{
    public partial class DashboarderForm
    {
        private SynchronizationContext m_uiContext;
        private int m_uiThreadId;
        private PwDatabase m_database;
        private PwGroup m_databaseRoot;
        private volatile bool m_closing;
        private volatile bool m_batchCancelled;
        private volatile int m_batchGeneration;
        private int m_failedWorkers;
        private string m_lastOperationSummary;
        private readonly Queue<Action> m_pendingWorkers = new Queue<Action>();
        private int m_scheduledWorkers;
        private const int MaximumWorkers = 4;

        private bool RunOnUi(Action action)
        {
            if (m_closing || IsDisposed || Disposing) return false;
            bool completed = false;
            SendOrPostCallback guarded = delegate
            {
                if (m_closing || IsDisposed || Disposing) return;
                action();
                completed = true;
            };
            if (Thread.CurrentThread.ManagedThreadId == m_uiThreadId) guarded(null);
            else
            {
                if (m_uiContext == null) return false;
                try { m_uiContext.Send(guarded, null); }
                catch (InvalidAsynchronousStateException) { return false; }
                catch (InvalidOperationException)
                {
                    if (m_closing || IsDisposed || Disposing) return false;
                    throw;
                }
            }
            return completed;
        }

        private bool IsCurrentDatabase()
        {
            return !m_closing && !IsDisposed && !Disposing && m_database != null && m_database.IsOpen &&
                ReferenceEquals(m_database, m_PluginHost.Database) &&
                ReferenceEquals(m_databaseRoot, m_database.RootGroup);
        }

        private bool IsLiveEntry(PwEntry entry)
        {
            return IsCurrentDatabase() && entry != null &&
                ReferenceEquals(m_databaseRoot.FindEntry(entry.Uuid, true), entry);
        }

        private void QueueBatchWork(Action<ListViewItem> operation, ListViewItem item)
        {
            int generation = m_batchGeneration;
            m_pendingWorkers.Enqueue(delegate { RunWorker(generation, delegate { operation(item); }); });
            StartPendingWorkers();
        }

        private void StartPendingWorkers()
        {
            while (!m_closing && batchStatus != BatchStatusType.Stopping && m_scheduledWorkers < MaximumWorkers && m_pendingWorkers.Count > 0)
            {
                Action work = m_pendingWorkers.Dequeue();
                m_scheduledWorkers++;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { work(); }
                    finally
                    {
                        RunOnUi(delegate { m_scheduledWorkers--; StartPendingWorkers(); });
                    }
                });
            }
            UpdateStartAndStopButtons();
        }

        private void RunWorker(int generation, Action operation)
        {
            bool started = false;
            try
            {
                if (!RunOnUi(delegate
                {
                    if (generation != m_batchGeneration || m_batchCancelled || batchStatus == BatchStatusType.Stopping || !IsCurrentDatabase()) return;
                    UpdateNbThread(1);
                    started = true;
                }) || !started) return;
                operation();
            }
            catch (Exception error)
            {
                // A failed download must not escape a ThreadPool callback and terminate KeePass.
                // Keep URLs and entry data out of diagnostics.
                Debug.WriteLine("Custom Icon Dashboarder worker failed: " + error.GetType().Name);
                RunOnUi(delegate { if (generation == m_batchGeneration) m_failedWorkers++; });
            }
            finally
            {
                if (started) RunOnUi(delegate { if (generation == m_batchGeneration) UpdateNbThread(-1); });
            }
        }
    }
}
