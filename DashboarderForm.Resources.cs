using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using KeePassLib;

namespace CustomIconDashboarderPlugin
{
    public partial class DashboarderForm
    {
        private bool m_resourcesReleased;
        private readonly object m_downloadLock = new object();
        private readonly HashSet<BestIconFinder> m_activeDownloads = new HashSet<BestIconFinder>();

        private Func<Uri, BestIconFinder> m_createIconFinder = uri => new BestIconFinder(uri);

        private void FindBestIconForDashboard(BestIconFinder finder)
        {
            lock (m_downloadLock)
            {
                if (m_closing || m_batchCancelled || batchStatus == BatchStatusType.Stopping) { finder.Dispose(); return; }
                m_activeDownloads.Add(finder);
            }
            try { finder.FindBestIcon(); }
            finally { lock (m_downloadLock) m_activeDownloads.Remove(finder); }
        }

        private void CancelActiveDownloads()
        {
            BestIconFinder[] active;
            lock (m_downloadLock) { active = new BestIconFinder[m_activeDownloads.Count]; m_activeDownloads.CopyTo(active); }
            foreach (var finder in active) finder.Dispose();
        }

        private static Bitmap OwnedPreview(Image source, int size)
        {
            var image = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
                int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                graphics.DrawImage(source, new Rectangle((size - width) / 2, (size - height) / 2, width, height));
            }
            return image;
        }

        private static void SetPreview(PictureBox box, Image source, int size)
        {
            Image replacement = source == null ? null : OwnedPreview(source, size);
            Image previous = box.BackgroundImage;
            box.BackgroundImage = replacement;
            if (previous != null) previous.Dispose();
        }

        private void ClearDownloadImages()
        {
            ImageList large = m_lvDownloadResult.LargeImageList;
            ImageList small = m_lvDownloadResult.SmallImageList;
            m_lvDownloadResult.LargeImageList = null;
            m_lvDownloadResult.SmallImageList = null;
            if (large != null) large.Dispose();
            if (small != null && !ReferenceEquals(small, large)) small.Dispose();
        }

        private void ReleaseDashboardResources()
        {
            if (m_resourcesReleased) return;
            m_resourcesReleased = true;
            m_closing = true;
            CancelActiveDownloads();
            m_pendingWorkers.Clear();
            foreach (PictureBox box in new[] { pbo_selectedIcon128, pbo_selectedIcon64, pbo_selectedIcon32, pbo_selectedIcon16,
                pbo_downloadedIcon128, pbo_downloadedIcon64, pbo_downloadedIcon32, pbo_downloadedIcon16 }) SetPreview(box, null, 1);
            ClearDownloadImages();
            ImageList customIcons = m_lvViewIcon.SmallImageList;
            m_lvViewIcon.SmallImageList = null;
            if (customIcons != null) customIcons.Dispose();
            // m_lvAllEntries.SmallImageList is borrowed from KeePass.
            m_lvAllEntries.SmallImageList = null;
            var finders = new HashSet<BestIconFinder>();
            lock (lckIconChooserIndexerFromEntry)
            {
                if (m_iconChooserIndexerFromEntry != null)
                {
                    foreach (var chooser in m_iconChooserIndexerFromEntry.Values) if (chooser != null) finders.Add(chooser.Bif);
                    m_iconChooserIndexerFromEntry.Clear();
                }
            }
            lock (lckIconChooserIndexerFromIcon)
            {
                if (m_iconChooserIndexerFromIcon != null)
                {
                    foreach (var chooser in m_iconChooserIndexerFromIcon.Values) if (chooser != null) finders.Add(chooser.Bif);
                    m_iconChooserIndexerFromIcon.Clear();
                }
            }
            CurrentIconChooser = null;
            if (m_stdIcons != null) { foreach (Image image in m_stdIcons) image.Dispose(); m_stdIcons = null; }
            foreach (BestIconFinder finder in finders) finder.Dispose();
            if (m_dashboardBoldFont != null) m_dashboardBoldFont.Dispose();
            if (m_dashboardFont != null) m_dashboardFont.Dispose();
        }

        private int ApplyEntryIcons(IEnumerable<PwEntry> entries, PwIcon standard, PwUuid custom)
        {
            if (!IsCurrentDatabase()) return 0;
            int changed = 0;
            foreach (PwEntry entry in entries)
            {
                if (!IsLiveEntry(entry)) continue;
                bool different = !entry.CustomIconUuid.Equals(custom) || (custom.Equals(PwUuid.Zero) && entry.IconId != standard);
                if (!different) continue;
                entry.CustomIconUuid = custom;
                if (custom.Equals(PwUuid.Zero)) entry.IconId = standard;
                m_database.Modified = true;
                m_database.UINeedsIconUpdate = true;
                entry.Touch(true);
                changed++;
            }
            return changed;
        }
    }
}
