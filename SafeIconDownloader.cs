using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace CustomIconDashboarderPlugin
{
    internal sealed class SafeIconDownloader : IDisposable
    {
        internal const int MaxHtmlBytes = 512 * 1024;
        internal const int MaxImageBytes = 2 * 1024 * 1024;
        private readonly HttpClient _client;
        private readonly Stopwatch _budget = new Stopwatch();
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private TimeSpan _requestTimeout = TimeSpan.FromSeconds(10);
        private bool _disposed;

        internal SafeIconDownloader() : this(new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            SslProtocols = SslProtocols.Tls12,
            UseCookies = true
        })) { }

        internal SafeIconDownloader(HttpClient client)
        {
            _client = client;
            _client.Timeout = Timeout.InfiniteTimeSpan;
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "KeePass Custom Icon Dashboarder");
        }

        internal static bool IsAllowed(Uri uri, Uri source = null)
        {
            return uri != null && uri.IsAbsoluteUri && string.IsNullOrEmpty(uri.UserInfo) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                (source == null || source.Scheme != Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttps);
        }

        internal static Uri Resolve(Uri source, string reference)
        {
            Uri target;
            if (string.IsNullOrWhiteSpace(reference) || reference.IndexOf('\\') >= 0 ||
                !Uri.TryCreate(source, reference.Trim(), out target) || !IsAllowed(target, source)) return null;
            return target;
        }

        internal byte[] Download(Uri uri, Uri source, int limit, out Uri finalUri)
        {
            try
            {
                var result = DownloadAsync(uri, source, limit).GetAwaiter().GetResult();
                finalUri = result.Item2;
                return result.Item1;
            }
            catch (OperationCanceledException error) { throw new WebException("Icon request exceeded its deadline.", error); }
            catch (HttpRequestException error) { throw new WebException("Icon request failed.", error); }
            catch (IOException error) { throw new WebException("Icon response could not be read.", error); }
            catch (ObjectDisposedException error) { throw new WebException("Icon request was cancelled.", error); }
        }

        private async Task<Tuple<byte[], Uri>> DownloadAsync(Uri uri, Uri source, int limit)
        {
            if (!IsAllowed(uri, source)) throw new WebException("Only HTTP/HTTPS addresses without embedded credentials are supported.");
            if (!_budget.IsRunning) _budget.Start();
            TimeSpan remaining = TimeSpan.FromSeconds(30) - _budget.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new WebException("Icon search exceeded its deadline.");
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token))
            {
                cancellation.CancelAfter(remaining < _requestTimeout ? remaining : _requestTimeout);
                Task deadline = Task.Delay(Timeout.Infinite, cancellation.Token);
                for (int redirect = 0; redirect <= 5; redirect++)
                {
                    using (var response = await BeforeDeadline(_client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation.Token), deadline, cancellation.Token).ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (status >= 300 && status <= 399)
                        {
                            Uri next = response.Headers.Location == null ? null : Resolve(uri, response.Headers.Location.ToString());
                            if (next == null || redirect == 5) throw new WebException("Unsupported or excessive icon redirect.");
                            uri = next;
                            continue;
                        }
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > limit) throw new WebException("Icon response is too large.");
                        using (cancellation.Token.Register(response.Dispose))
                        using (var stream = await BeforeDeadline(response.Content.ReadAsStreamAsync(), deadline, cancellation.Token).ConfigureAwait(false))
                        using (var output = new MemoryStream())
                        {
                            var buffer = new byte[8192];
                            int count;
                            while ((count = await BeforeDeadline(stream.ReadAsync(buffer, 0, buffer.Length, cancellation.Token), deadline, cancellation.Token).ConfigureAwait(false)) != 0)
                            {
                                if (output.Length + count > limit) throw new WebException("Icon response is too large.");
                                output.Write(buffer, 0, count);
                            }
                            return Tuple.Create(output.ToArray(), uri);
                        }
                    }
                }
            }
            throw new WebException("Unsupported icon redirect.");
        }

        private static async Task<T> BeforeDeadline<T>(Task<T> work, Task deadline, CancellationToken token)
        {
            if (await Task.WhenAny(work, deadline).ConfigureAwait(false) != work)
            {
                var cleanup = work.ContinueWith(task =>
                {
                    if (task.IsFaulted) { var observed = task.Exception; }
                    else if (task.Status == TaskStatus.RanToCompletion)
                    {
                        var resource = task.Result as IDisposable;
                        if (resource != null) resource.Dispose();
                    }
                }, TaskScheduler.Default);
                throw new OperationCanceledException(token);
            }
            return await work.ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _shutdown.Cancel();
            _client.Dispose();
            _shutdown.Dispose();
        }
    }
}
