/*
  CustomIconDashboarder - KeePass Plugin to get some information and 
  manage custom icons
  
  Copyright (C) 2023 Jareth Lomson <jareth.lomson@gmail.com>
 
 This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program; if not, write to the Free Software
  Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
 */
using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Text;
using System.Drawing;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using LomsonLib.Utility;

namespace CustomIconDashboarderPlugin
{

	/// <summary>
	/// Find the best icon of a web site from its url.
	/// </summary>
	public class BestIconFinder : IDisposable
	{
		#region Static elements
		
		/// <summary>
		/// STATIC Section
		/// </summary>
		
		
        // Retained for plugin integrations; TLS is configured only on our HTTP handler.
        public static void InitClass() { }

		private const string alphabetUC = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
		private const string alphabetLC = "abcdefghijklmnopqrstuvwxyz";
		
		private static string[] xPathToTest = {
			"//link[translate(@rel,'" + alphabetUC + "','" + alphabetLC +"')='shortcut icon']",
			"//link[translate(@rel,'" + alphabetUC + "','" + alphabetLC +"')='icon']",
			"//link[translate(@rel,'" + alphabetUC + "','" + alphabetLC +"')='apple-touch-icon']",
			"//link[translate(@rel,'" + alphabetUC + "','" + alphabetLC +"')='apple-touch-icon image_src']",
			"//link[translate(@rel,'" + alphabetUC + "','" + alphabetLC +"')='apple-touch-icon-precomposed']"
		};
		
        private readonly HashSet<Uri> testedImageUris = new HashSet<Uri>();
        private volatile bool disposed;
        private readonly object lifetimeLock = new object();
		#endregion		
		
		
		public Image BestImage {get; private set; }
		
		public Uri SiteUri {get; private set;} // Uri without path - eg http://www.sample.com
		public Uri OriginalSiteUri {get; private set;} // Original URI with all path - eg http://www.sample.com/folder/subfolder
		public Uri RootUriForIco {get; private set;}
		public FinderResult Result {get; private set; }
		public string Details {get;set; }
		public int NbRequestCacheInvoke {get; private set; }
		private SafeIconDownloader client;

		public List<ImageInfo> ListImageInfo {get; private set;} // All image and URI
		public int IndexOfBestImage {get; private set; }
		
		private MyLogger myLogger;
		private List<Uri> ListUriIcons {get; set;}
		
		public BestIconFinder(Uri siteUri)
		{
            if (siteUri == null) throw new ArgumentNullException("siteUri");
            if (!SafeIconDownloader.IsAllowed(siteUri)) throw new ArgumentException("Only HTTP/HTTPS addresses without embedded credentials are supported.", "siteUri");
			this.OriginalSiteUri = siteUri;
			if ( (siteUri.Scheme.ToLower() == "http") ||
			    (siteUri.Scheme.ToLower() == "https" ) ){
				this.SiteUri = new Uri(siteUri.GetLeftPart(UriPartial.Authority) + "/");
				this.Result = new FinderResult(FinderResult.RESULT_NO_URL);
			} else {
			    this.SiteUri = null;
			    this.Result = new FinderResult(FinderResult.RESULT_NO_URL);
			}
			this.myLogger = new MyLogger(this);
			this.BestImage = null;
			this.IndexOfBestImage = -1;
			this.ListImageInfo = new List<ImageInfo>();
			this.NbRequestCacheInvoke = 0;
			client = new SafeIconDownloader();
		}
		
		public BestIconFinder()
		{
			this.SiteUri = null;
            this.Result = null;
			this.myLogger = new MyLogger(this);
			this.BestImage = null;
			this.IndexOfBestImage = -1;
			this.ListImageInfo = new List<ImageInfo>();
			this.NbRequestCacheInvoke = 0;
			client = new SafeIconDownloader();
		}

        #region Embeded Factory
        public static BestIconFinder BuildInstanceWithNoURLResult()
        {
            BestIconFinder result = new BestIconFinder
            {
                Result = new FinderResult(FinderResult.RESULT_NO_URL)
            };
            return result;
        }

        public static BestIconFinder BuildInstanceWithMalformedURLResult()
        {
            BestIconFinder result = new BestIconFinder
            {
                Result = new FinderResult(FinderResult.RESULT_MALFORMED_URL)
            };
            result.myLogger.LogDebug("Non managed URL format");
            return result;
        }
        #endregion

        public void FindBestIcon()
		{
			if (this.SiteUri == null) {
				this.myLogger.LogWarn("No URL. Stop Find Best Icon");
				return;
			}
			
			FindBestIconForURI( this.SiteUri);
			
			if (this.BestImage == null) { // try with www
				
				Uri testURI = this.SiteUri;
				if (testURI.HostNameType == UriHostNameType.Dns && !testURI.IsLoopback && !testURI.Host.StartsWith("www.")) {
					FindBestIconForURI( new Uri( 
					   this.SiteUri.Scheme + "://www." + this.SiteUri.DnsSafeHost + (this.SiteUri.IsDefaultPort ? "" : ":" + this.SiteUri.Port) ));
				}
			}
			
			string rootDomain = this.SiteUri.HostNameType == UriHostNameType.Dns && !this.SiteUri.IsLoopback && this.SiteUri.IsDefaultPort
                ? URLUtility.RootDomain(this.SiteUri.AbsoluteUri) : null;
			
			if (! string.IsNullOrEmpty(rootDomain)) {
				if (this.BestImage == null) { // try with root domain prefix
					FindBestIconForURI( new Uri( this.SiteUri.Scheme + "://" + rootDomain ));
				}
				
				if (this.BestImage == null) { // try with host prefix and www
					FindBestIconForURI( new Uri (this.SiteUri.Scheme + "://www." + rootDomain));
				}
			    }
			
			if (this.RootUriForIco != null && this.RootUriForIco.HostNameType == UriHostNameType.Dns && !this.RootUriForIco.IsLoopback && this.RootUriForIco.IsDefaultPort) {
				if (this.BestImage == null) { // try with host prefix
					FindBestIconForURI( new Uri( 
					       this.RootUriForIco.Scheme + "://" + this.RootUriForIco.Host) );
				}
				
				if (this.BestImage == null) { // try with host prefix and www
					FindBestIconForURI( new Uri (
						this.RootUriForIco.Scheme + "://www." + this.RootUriForIco.Host));
				}
			}
			
			if (this.BestImage == null) {
				FindBestIconForURI( this.OriginalSiteUri );
			}
			
		}
		
       	public void FindBestIconForURI(Uri param_uri)
		{
			// Prepare
			if (param_uri == null) {
				this.myLogger.LogWarn("No url. Stop Find Best Icon");
				return;
			}
	
			Uri testedUri = param_uri;
			
			// Get Links
			GetIconLinksForURI(testedUri);
			
			if (this.Result.ResultCode != FinderResult.RESULT_OK) {
				this.myLogger.LogError("Error while retrieving Icon link for " + 
				                       testedUri.AbsoluteUri);
				return;
			}
			
			// Test Icon URL
			this.myLogger.LogInfo("Test Icon URL");
			this.myLogger.Indent();
			this.BestImage = null;
			foreach ( Uri uriTested in this.ListUriIcons) {
				TestImageUri( uriTested);
			}
			
			// Test default url
			if ( ((this.BestImage != null)
			      && (this.BestImage.Width + this.BestImage.Height < 64))
			      || (this.BestImage == null) ) {
				TestImageUri( new Uri(this.RootUriForIco, "favicon.ico"));
			}
			
			// Try better resolution  (usefull for site like www dot francetvsport dot com
			if ( ((this.BestImage != null)
			      && (this.BestImage.Width + this.BestImage.Height < 64))
			      || (this.BestImage == null) ) {
				TestImageUri( new Uri(this.RootUriForIco, "apple-touch-icon-precomposed.png"));
				TestImageUri( new Uri(this.RootUriForIco, "apple-touch-icon.png"));
			}
			
			this.myLogger.Unindent();
			this.myLogger.LogInfo("End test Icon URI");
			
		}
		
		/// <summary>
		/// Test Image URI and store in Best Image if quality is better 
		/// </summary>
        public void TestImageUri(Uri uriToBeTested)
        {
            if (disposed || ListImageInfo.Count >= 8 || testedImageUris.Count >= 24 ||
                !SafeIconDownloader.IsAllowed(uriToBeTested, this.SiteUri) || !testedImageUris.Add(uriToBeTested)) return;
            try
            {
                Uri finalUri;
                byte[] data = client.Download(uriToBeTested, this.SiteUri, SafeIconDownloader.MaxImageBytes, out finalUri);
                data = PreparedIconBytesArrayToBeParsed(data);
                IconImageLimits.Validate(data);
                using (Image decoded = KeePassLib.Utility.GfxUtil.LoadImage(data))
                {
                    if (decoded.Width > 2048 || decoded.Height > 2048 || (long)decoded.Width * decoded.Height > 4 * 1024 * 1024)
                        throw new WebException("Icon dimensions are too large.");
                    int width = Math.Max(1, Math.Min(256, decoded.Width));
                    int height = Math.Max(1, decoded.Height * width / decoded.Width);
                    if (height > 256) { height = 256; width = Math.Max(1, decoded.Width * height / decoded.Height); }
                    Image image = new Bitmap(decoded, width, height);
                    lock (lifetimeLock)
                    {
                        if (disposed) { image.Dispose(); return; }
                        ListImageInfo.Add(new ImageInfo(image, finalUri.GetLeftPart(UriPartial.Path)));
                        if (BestImage == null || CompareImageQuality(image, BestImage) > 0)
                        { BestImage = image; IndexOfBestImage = ListImageInfo.Count - 1; }
                    }
                }
            }
            catch (Exception error)
            {
                // Untrusted image data and network errors must not terminate KeePass.
                myLogger.LogWarn("Icon unavailable: " + error.GetType().Name);
            }
        }

		public void GetIconLinks() {
			GetIconLinksForURI(this.SiteUri);
		}
		
        public void GetIconLinksForURI(Uri uri)
        {
            ListUriIcons = new List<Uri>();
            if (!SafeIconDownloader.IsAllowed(uri, SiteUri))
            { Result = new FinderResult(FinderResult.RESULT_NO_URL); return; }
            try
            {
                HtmlDocument page;
                Uri finalUri = GetURIOfSite(uri, out page);
                RootUriForIco = new Uri(finalUri.GetLeftPart(UriPartial.Authority) + "/");
                foreach (string xpath in xPathToTest)
                {
                    var nodes = page.DocumentNode.SelectNodes(xpath);
                    if (nodes == null) continue;
                    foreach (HtmlNode node in nodes)
                    {
                        Uri icon = SafeIconDownloader.Resolve(finalUri, node.GetAttributeValue("href", ""));
                        if (icon != null && !ListUriIcons.Contains(icon) && ListUriIcons.Count < 12) ListUriIcons.Add(icon);
                    }
                }
                Result = new FinderResult(FinderResult.RESULT_OK);
            }
            catch (Exception error)
            {
                Result = new FinderResult(FinderResult.RESULT_HTML_NOT_FOUND);
                myLogger.LogWarn("Page unavailable: " + error.GetType().Name);
            }
        }

		/// <summary>
		/// Compare Quality of two images.
		/// </summary>
		/// <param name="img1"></param>
		/// <param name="img2"></param>
		/// <returns>1 = if img1 better than img2, 0 = equivalent, -1 = worse</returns>
		
		private static int CompareImageQuality(Image img1, Image img2) {
			if ( (img1.Width + img1.Height) > (img2.Width + img2.Height) ) {
				return 1;
			}
			else if ( (img1.Width + img1.Height) == (img2.Width + img2.Height) ) {
				return 0;
			}
			
			return -1;
		}
		
		/// <summary>
        /// Gets a memory stream representing an image from an explicit favicon location.
        /// </summary>
        /// <param name="fullURI">The URI.</param>
        /// <returns></returns>
        private Uri GetURIOfSite(Uri initialURI, out HtmlDocument document)
        {
            Uri uri = initialURI;
            document = null;
            for (int refresh = 0; refresh <= 3; refresh++)
            {
                Uri finalUri;
                byte[] data = client.Download(uri, SiteUri, SafeIconDownloader.MaxHtmlBytes, out finalUri);
                document = new HtmlDocument();
                document.OptionMaxNestedChildNodes = 128;
                document.LoadHtml(Encoding.UTF8.GetString(data));
                Uri next = GetMetaRefreshLink(finalUri, document);
                if (next == null) return finalUri;
                if (refresh == 3) throw new WebException("Too many page refreshes.");
                uri = next;
            }
            throw new WebException("Unsupported page refresh.");
        }

        private Uri GetMetaRefreshLink(Uri uri, HtmlDocument document)
        {
            var metas = document.DocumentNode.SelectNodes("//meta[@http-equiv]");
            if (metas == null) return null;
            foreach (HtmlNode node in metas)
            {
                string kind = node.GetAttributeValue("http-equiv", "");
                if (!kind.Equals("refresh", StringComparison.OrdinalIgnoreCase) && !kind.Equals("location", StringComparison.OrdinalIgnoreCase)) continue;
                Match match = Regex.Match(node.GetAttributeValue("content", ""), @"\burl\s*=\s*(.+)$", RegexOptions.IgnoreCase);
                if (match.Success) return SafeIconDownloader.Resolve(uri, match.Groups[1].Value.Trim().Trim('\'', '"'));
            }
            return null;
        }

		/// <summary>
		/// Prepare bytes array representing an icon to be parsed by libraries
		///  (provided by KP and MS)
		/// </summary>
		/// <description>
		/// Reject truncated ICO payloads before passing data to image libraries.
		/// </description>
		/// <param name="batbp">Byte array to be parsed</param>
		/// <returns></returns>
		/// Inspired by the method KeePassLib.Utility.GsfxUtil.UnpackIco of KeePass v2.30
        private byte[] PreparedIconBytesArrayToBeParsed(byte[] data)
        {
            if (data == null || data.Length < 6) throw new InvalidDataException("Invalid icon data.");
            if (BitConverter.ToUInt16(data, 0) == 0 && BitConverter.ToUInt16(data, 2) == 1)
            {
                int count = BitConverter.ToUInt16(data, 4);
                if (count == 0 || count > 128 || data.Length < 6 + count * 16) throw new InvalidDataException("Invalid ICO directory.");
                for (int i = 0; i < count; i++)
                {
                    int entry = 6 + i * 16;
                    uint size = BitConverter.ToUInt32(data, entry + 8), offset = BitConverter.ToUInt32(data, entry + 12);
                    if ((ulong)offset + size > (ulong)data.Length) throw new InvalidDataException("Truncated ICO image.");
                }
            }
            return data;
        }

		private sealed class MyLogger {
			
			private int nbIndent;
			private String indent;
			private BestIconFinder myBestIf;
			private int logLevel; // ERROR = 1, WARN = 2, INFO = 3, DEBUG = 4
			
			public MyLogger(BestIconFinder paramMyBestIf) {
				this.nbIndent = 0;
				indent = "";
				this.myBestIf = paramMyBestIf;
				this.logLevel = 4;
			}
			
			
			public MyLogger(BestIconFinder paramMyBestIf, int paramLogLevel):this(paramMyBestIf) {
				logLevel = paramLogLevel;
			}
			
			public void Reset() {
				this.nbIndent = 0;
				indent = "";
			}
			
			public void Indent() {
				nbIndent++;
				updateIndent();
			}
			
			public void Unindent() {
				nbIndent--;
				updateIndent();
			}
			
			private void updateIndent() {
				indent = "";
				indent = indent.PadRight(nbIndent * 2, ' ');
			}
			
			public void LogInfo(String what) {
				if ( this.logLevel >= 3)
					myBestIf.Details += indent + what + System.Environment.NewLine;
			}
			
			public void LogDebug(String what) {
				if ( this.logLevel >= 4)
					myBestIf.Details += indent + what + System.Environment.NewLine;
			}
			
			public void LogDebugIndented(String what) {
				if ( this.logLevel >= 4) {
					what = what.Replace("\n", System.Environment.NewLine + indent);
					
					myBestIf.Details += indent + what + System.Environment.NewLine;
				}
			}
			
			public void LogWarn(String what) {
				if ( this.logLevel >= 2)
					myBestIf.Details += indent + what + System.Environment.NewLine;
			}
			
			public void LogError(String what) {
				if ( this.logLevel >= 1)
					myBestIf.Details += indent + "ERROR - " + what + System.Environment.NewLine;
			}
		}
		
        public void Dispose()
        {
            lock (lifetimeLock)
            {
                if (disposed) return;
                disposed = true;
                var owned = new HashSet<Image>();
                foreach (var info in ListImageInfo) if (info.ImgData != null) owned.Add(info.ImgData);
                foreach (Image image in owned) image.Dispose();
                ListImageInfo.Clear();
                BestImage = null;
            }
            if (client != null) client.Dispose();
        }

//       // Set "UnsafeHeaderParsing" flags. Usefull to retrieve information from several sites
//       // try www dot geni dot com    
//       // to be used with "using System.Reflection;" in header
//       public static bool SetAllowUnsafeHeaderParsing(bool value)
//		{
//		  //Get the assembly that contains the internal class
//		  Assembly aNetAssembly = Assembly.GetAssembly(typeof(System.Net.Configuration.SettingsSection));
//		  if (aNetAssembly != null)
//		  {
//		    //Use the assembly in order to get the internal type for the internal class
//		    Type aSettingsType = aNetAssembly.GetType("System.Net.Configuration.SettingsSectionInternal");
//		    if (aSettingsType != null)
//		    {
//		      //Use the internal static property to get an instance of the internal settings class.
//		      //If the static instance isn't created allready the property will create it for us.
//		      object anInstance = aSettingsType.InvokeMember("Section",
//		        BindingFlags.Static | BindingFlags.GetProperty | BindingFlags.NonPublic, null, null, new object[] { });
//		
//		      if (anInstance != null)
//		      {
//		        //Locate the private bool field that tells the framework is unsafe header parsing should be allowed or not
//		        FieldInfo aUseUnsafeHeaderParsing = aSettingsType.GetField("useUnsafeHeaderParsing", BindingFlags.NonPublic | BindingFlags.Instance);
//		        if (aUseUnsafeHeaderParsing != null)
//		        {
//		          aUseUnsafeHeaderParsing.SetValue(anInstance, value);
//		          return true;
//		        }
//		      }
//		    }
//		  }
//		  return false;
//		} 
//
	

	}
	
	public sealed class FinderResult {
		
		public const int RESULT_NOT_YET_SEARCH = 0;
		public const int RESULT_OK = 1;
		public const int RESULT_HTML_NOT_FOUND = 2;
		public const int RESULT_HTML_PARSING = 3;
		public const int RESULT_NO_URL = 4;
        public const int RESULT_MALFORMED_URL = 5;
		
		public int ResultCode {get; private set; }
		public FinderResult (int code) 
		{
			ResultCode = code;
		}
		
		public override string ToString() {
			switch (this.ResultCode) {
				case FinderResult.RESULT_NOT_YET_SEARCH : return "Not Yet Search";
				case RESULT_OK : return "OK";
				case RESULT_HTML_NOT_FOUND : return "Html not found";
				case RESULT_HTML_PARSING : return "Error Html parsing";
				case RESULT_NO_URL : return "No URL";
                case RESULT_MALFORMED_URL: return "Malformed URL";
				default : return "";
			}
		}
	}

}
