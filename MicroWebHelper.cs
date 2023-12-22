using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using FreneticUtilities.FreneticToolkit;
using FreneticUtilities.FreneticExtensions;

namespace DiscordBotBase
{
    /// <summary>Helper to generate an internal micro-website.</summary>
    public class MicroWebHelper
    {
        /// <summary>Helper to limit file paths to valid ones.</summary>
        public static AsciiMatcher FilePathLimiter = new(AsciiMatcher.LowercaseLetters + AsciiMatcher.Digits + "./_");

        /// <summary>The backing <see cref="HttpListener"/> instance.</summary>
        public HttpListener Listener;

        /// <summary>The file path for raw files, if any.</summary>
        public string RawFileRoot;

        /// <summary>The token that can cancel this web helper.</summary>
        public CancellationTokenSource CancelToken = new();

        /// <summary>Token indicates that the whole system is done.</summary>
        public CancellationTokenSource IsEnded = new();
        
        /// <summary>Pre-scanned list of valid paths for the raw file root.</summary>
        public HashSet<string> RawRootStartPaths = [];

        /// <summary>Function to get dynamic pages as-needed.</summary>
        public Func<string, HttpListenerContext, WebResult> PageGetter;

        /// <summary>Matcher for values escaped by <see cref="HtmlEscape"/>.</summary>
        public static AsciiMatcher NeedsHTMLEscapeMatcher = new("&<>");

        /// <summary>Escapes a string to safely output it into HTML.</summary>
        public static string HtmlEscape(string input)
        {
            if (!NeedsHTMLEscapeMatcher.ContainsAnyMatch(input))
            {
                return input;
            }
            return input.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>Helper class to make injectable HTML based on the &lt;%INJECT_ID%&gt; format.</summary>
        public class InjectableHtml
        {
            /// <summary>A single part of the HTML page.</summary>
            public class Component
            {
                /// <summary>If true: injectable text. If false: raw text.</summary>
                public bool DoReplace;

                /// <summary>The raw content of the component.</summary>
                public string Content;
            }

            /// <summary>The parts that make up this page.</summary>
            public List<Component> Components = new(4);

            /// <summary>An estimate of the length of this page.</summary>
            public int Length;

            /// <summary>Constructs the injectable HTML from the raw pseudo-html string.</summary>
            public InjectableHtml(string raw)
            {
                Length = raw.Length;
                int start = raw.IndexOf("<%");
                int lastEnd = 0;
                while (start != -1)
                {
                    Components.Add(new Component() { DoReplace = false, Content = raw[lastEnd..start] });
                    int end = raw.IndexOf("%>", start + 2);
                    if (end == -1)
                    {
                        throw new Exception("Invalid injectable html, mismatched inject blocks");
                    }
                    Components.Add(new Component() { DoReplace = true, Content = raw[(start + 2)..end] });
                    lastEnd = end + 2;
                    start = raw.IndexOf("<%", end);
                }
                Components.Add(new Component() { DoReplace = false, Content = raw[lastEnd..] });
            }

            /// <summary>Gets the final form of the HTML, using the injecter method.</summary>
            public string Get(Func<string, string> inject)
            {
                StringBuilder output = new(Length * 2);
                foreach (Component component in Components)
                {
                    output.Append(component.DoReplace ? inject(component.Content) : component.Content);
                }
                return output.ToString();
            }
        }

        /// <summary>Holds a cacheable response to a web request.</summary>
        public class WebResult
        {
            /// <summary>The default utter failure response.</summary>
            public static WebResult FAIL = new() { Code = 400, ContentType = "text/plain", Data = StringConversionHelper.UTF8Encoding.GetBytes("Server did not provide a response to the request.") };

            /// <summary>The status code, eg '200' for 'OK' or '404' for 'File Not Found'</summary>
            public int Code = 200;

            /// <summary>The content type of the response, eg 'text/html' for HTML text.</summary>
            public string ContentType = "text/plain";

            /// <summary>The raw data of the response.</summary>
            public byte[] Data;

            /// <summary>The time this result was generated per <see cref="Environment.TickCount64"/>, for cache management.</summary>
            public long GeneratedTickTime = Environment.TickCount64;

            /// <summary>Applies the cached response to a request.</summary>
            public void Apply(HttpListenerResponse response)
            {
                response.StatusCode = Code;
                response.ContentType = ContentType;
                response.ContentLength64 = Data.LongLength;
                response.OutputStream.Write(Data, 0, Data.Length);
                response.Close();
            }
        }

        /// <summary>Cache of raw root files.</summary>
        public Dictionary<string, WebResult> RawFileCache = [];

        /// <summary>Creates and immediately starts the web helper.</summary>
        public MicroWebHelper(string bind, Func<string, HttpListenerContext, WebResult> _pageGetter, string _rawFileRoot = "wwwroot/")
        {
            PageGetter = _pageGetter;
            RawFileRoot = _rawFileRoot;
            if (_rawFileRoot is not null)
            {
                RawRootStartPaths.UnionWith(Directory.GetFiles(_rawFileRoot).Select(f => f.AfterLast('/')));
                RawRootStartPaths.UnionWith(Directory.GetDirectories(_rawFileRoot).Select(f => f.AfterLast('/')));
            }
            Listener = new HttpListener();
            Listener.Prefixes.Add(bind);
            Listener.Start();
            new Thread(InternalLoop) { Name = "MicroWebHelper" }.Start();
        }

        /// <summary>Cancels the web helper. Signals web thread to close ASAP.</summary>
        public void Cancel()
        {
            CancelToken.Cancel();
            try
            {
                Task.Delay(TimeSpan.FromSeconds(5)).Wait(IsEnded.Token);
            }
            catch (OperationCanceledException)
            {
                // Ignore - expected
            }
        }

        /// <summary>A mapping of common file extensions to their content type.</summary>
        public static Dictionary<string, string> CommonContentTypes = new()
        {
            { "png", "image/png" },
            { "jpg", "image/jpeg" },
            { "jpeg", "image/jpeg" },
            { "gif", "image/gif" },
            { "ico", "image/x-icon" },
            { "svg", "image/svg+xml" },
            { "mp3", "audio/mpeg" },
            { "wav", "audio/x-wav" },
            { "js", "application/javascript" },
            { "ogg", "application/ogg" },
            { "json", "application/json" },
            { "zip", "application/zip" },
            { "dat", "application/octet-stream" },
            { "css", "text/css" },
            { "htm", "text/html" },
            { "html", "text/html" },
            { "txt", "text/plain" },
            { "yml", "text/plain" },
            { "fds", "text/plain" },
            { "xml", "text/xml" },
            { "mp4", "video/mp4" },
            { "mpeg", "video/mpeg" },
            { "webm", "video/webm" }
        };

        /// <summary>Guesses the content type based on path for common file types.</summary>
        public static string GuessContentType(string path)
        {
            string extension = path.AfterLast('.');
            if (CommonContentTypes.TryGetValue(extension, out string type))
            {
                return type;
            }
            return "application/octet-stream";
        }

        /// <summary>Internal thread loop, do not reference directly.</summary>
        public void InternalLoop()
        {
            string lastUrl = "unknown";
            try
            {
                while (true)
                {
                    if (CancelToken.IsCancellationRequested)
                    {
                        return;
                    }
                    try
                    {
                        Task<HttpListenerContext> contextGetter = Listener.GetContextAsync();
                        contextGetter.Wait(CancelToken.Token);
                        HttpListenerContext context = contextGetter.Result;
                        string url = context.Request.Url.AbsolutePath;
                        if (url.StartsWithFast('/'))
                        {
                            url = url[1..];
                        }
                        string fixedUrl = FilePathLimiter.TrimToMatches(url);
                        lastUrl = fixedUrl;
                        if (!fixedUrl.Contains("..") && !fixedUrl.Contains("/.") && !fixedUrl.Contains("./") && RawRootStartPaths.Contains(fixedUrl.Before('/')))
                        {
                            if (RawFileCache.TryGetValue(fixedUrl, out WebResult cache))
                            {
                                cache.Apply(context.Response);
                                continue;
                            }
                            string filePath = RawFileRoot + fixedUrl;
                            if (File.Exists(filePath))
                            {
                                WebResult newResult = new()
                                {
                                    Code = 200,
                                    ContentType = GuessContentType(fixedUrl),
                                    Data = File.ReadAllBytes(filePath)
                                };
                                RawFileCache[fixedUrl] = newResult;
                                newResult.Apply(context.Response);
                                continue;
                            }
                        }
                        WebResult result = PageGetter(fixedUrl, context);
                        if (result is not null)
                        {
                            result.Apply(context.Response);
                            continue;
                        }
                        WebResult.FAIL.Apply(context.Response);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Web request ({lastUrl}) failed: {ex}");
                    }
                }
            }
            finally
            {
                Listener.Abort();
                IsEnded.Cancel();
            }
        }
    }
}
