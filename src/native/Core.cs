using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace InstaTranslate
{
    internal static class Log
    {
        private static readonly object Lock = new object();
        public static string Dir;
        public static string File;

        static Log()
        {
            string[] cands;
            try
            {
                cands = new string[]
                {
                    Path.Combine(Path.GetTempPath(), "InstaTranslate"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InstaTranslate"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")
                };
            }
            catch
            {
                cands = new string[] { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs") };
            }
            foreach (var d in cands)
            {
                try
                {
                    Directory.CreateDirectory(d);
                    string probe = Path.Combine(d, ".w");
                    System.IO.File.WriteAllText(probe, "1");
                    System.IO.File.Delete(probe);
                    Dir = d;
                    File = Path.Combine(d, "app.log");
                    return;
                }
                catch { }
            }
        }

        public static void Boot(string line)
        {
            Write("BOOT " + line);
        }

        public static void Write(string line)
        {
            string text = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + line + Environment.NewLine;
            lock (Lock)
            {
                try
                {
                    if (File != null)
                    {
                        var fi = new FileInfo(File);
                        if (fi.Exists && fi.Length > 512 * 1024)
                        {
                            try { System.IO.File.Delete(File + ".old"); } catch { }
                            try { System.IO.File.Move(File, File + ".old"); } catch { }
                        }
                        System.IO.File.AppendAllText(File, text, Encoding.UTF8);
                    }
                }
                catch { }
            }
        }
    }

    internal static class Paths
    {
        private static string _dataDir;

        private static bool TryCreate(string p)
        {
            try
            {
                Directory.CreateDirectory(p);
                string probe = Path.Combine(p, ".writable");
                System.IO.File.WriteAllText(probe, "1");
                System.IO.File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string DataDir
        {
            get
            {
                if (_dataDir != null) return _dataDir;
                var candidates = new List<string>();
                try { candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InstaTranslate")); } catch { }
                try { candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InstaTranslate")); } catch { }
                try { candidates.Add(Path.Combine(Path.GetTempPath(), "InstaTranslate")); } catch { }
                try { candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data")); } catch { }

                foreach (var c in candidates)
                {
                    if (TryCreate(c))
                    {
                        _dataDir = c;
                        return _dataDir;
                    }
                }
                _dataDir = Path.GetTempPath();
                return _dataDir;
            }
        }

        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.json"); } }
        public static string HistoryFile { get { return Path.Combine(DataDir, "history.json"); } }
        public static string VocabFile { get { return Path.Combine(DataDir, "vocab.json"); } }
    }

    // ---------------- 设置 ----------------

    public class Config
    {
        public string Hotkey = "Alt+T";
        public bool AutoTranslate = true;
        public int AutoDelay = 420;
        public string Engine = "auto";        // auto | llm | bing
        public string TargetLang = "auto";    // auto | zh | en | ...
        public string LlmBaseUrl = "https://api.deepseek.com";
        public string LlmApiKey = "";
        public string LlmModel = "deepseek-chat";
        public bool Tts = true;
        public bool HotkeyTts = false;
        public bool AutoStart = false;
        public int BubbleWidth = 430;
        public string ExcludedApps = "";      // 每行一个关键字
        public bool FirstRun = true;

        public static Config Load()
        {
            try
            {
                if (System.IO.File.Exists(Paths.SettingsFile))
                {
                    string json = System.IO.File.ReadAllText(Paths.SettingsFile, Encoding.UTF8);
                    var cfg = Json.To<Config>(json);
                    if (cfg != null)
                    {
                        if (string.IsNullOrEmpty(cfg.LlmApiKey)) cfg.LlmApiKey = SeedKey();
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取设置失败: " + ex.Message);
            }
            var c = new Config();
            c.LlmApiKey = SeedKey();
            return c;
        }

        /// <summary>
        /// 首次运行时尝试自动带上 Key。只认环境变量，不读任何人的私密配置——
        /// 这个程序会分发给别人，绝不能把开发者自己的 Key 打进去。
        /// </summary>
        private static string SeedKey()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
                if (!string.IsNullOrEmpty(env)) return env;
                return "";
            }
            catch
            {
                return "";
            }
        }

        public void Save()
        {
            try
            {
                System.IO.File.WriteAllText(Paths.SettingsFile, Json.From(this), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Write("保存设置失败: " + ex.Message);
            }
        }
    }

    // ---------------- JSON ----------------

    internal static class Json
    {
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        static Json()
        {
            Ser.MaxJsonLength = 20 * 1024 * 1024;
            Ser.RecursionLimit = 100;
        }

        public static string From(object o)
        {
            try { return Ser.Serialize(o); }
            catch { return "{}"; }
        }

        public static T To<T>(string json) where T : class
        {
            try { return Ser.Deserialize<T>(json); }
            catch { return null; }
        }

        public static Dictionary<string, object> Dict(string json)
        {
            try { return Ser.Deserialize<Dictionary<string, object>>(json); }
            catch { return null; }
        }

        public static object Get(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v == null ? "" : Convert.ToString(v);
        }

        public static Dictionary<string, object> Sub(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v as Dictionary<string, object>;
        }

        public static List<object> Arr(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            var list = v as List<object>;
            if (list != null) return list;
            var arr = v as System.Collections.IEnumerable;
            if (arr != null && !(v is string))
            {
                var outp = new List<object>();
                foreach (var o in arr) outp.Add(o);
                return outp;
            }
            return null;
        }
    }

    // ---------------- 语言判定 ----------------

    internal static class Lang
    {
        private static readonly Regex Cjk = new Regex("[\\u3400-\\u4dbf\\u4e00-\\u9fff\\uf900-\\ufaff]");
        private static readonly Regex Hangul = new Regex("[\\uac00-\\ud7af]");
        private static readonly Regex Kana = new Regex("[\\u3040-\\u30ff]");
        private static readonly Regex Cyr = new Regex("[\\u0400-\\u04ff]");
        private static readonly Regex Latin = new Regex("[A-Za-z]");

        public static string Detect(string text)
        {
            if (string.IsNullOrEmpty(text)) return "unknown";
            int cjk = Cjk.Matches(text).Count;
            int han = Hangul.Matches(text).Count;
            int kana = Kana.Matches(text).Count;
            int cyr = Cyr.Matches(text).Count;
            int lat = Latin.Matches(text).Count;
            if (han > 0 && han >= cjk) return "ko";
            if (kana > 0 && kana >= cjk) return "ja";
            if (cjk > 0) return "zh";
            if (cyr > lat) return "ru";
            if (lat > 0) return "en";
            return "unknown";
        }

        public static bool LooksTranslatable(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim();
            if (t.Length == 0) return false;
            bool has = false;
            foreach (char c in t)
            {
                if (char.IsLetterOrDigit(c)) { has = true; break; }
            }
            return has;
        }

        public static string NameOf(string code)
        {
            switch (code)
            {
                case "zh": return "中文";
                case "en": return "英语";
                case "ja": return "日语";
                case "ko": return "韩语";
                case "fr": return "法语";
                case "de": return "德语";
                case "es": return "西班牙语";
                case "ru": return "俄语";
                default: return code;
            }
        }

        /// <summary>返回 { from, to }</summary>
        public static void Direction(string text, string targetLang, out string from, out string to)
        {
            string lang = Detect(text);
            from = lang == "unknown" ? "auto" : lang;
            if (!string.IsNullOrEmpty(targetLang) && targetLang != "auto")
            {
                to = targetLang;
                if (to == from) to = (from == "zh") ? "en" : "zh";
                return;
            }
            if (lang == "en") to = "zh";
            else if (lang == "zh") to = "en";
            else to = "zh";
        }

        public static string Normalize(string text)
        {
            if (text == null) return "";
            string t = text.Replace("\r\n", "\n").Replace('\r', '\n');
            t = t.Replace("\u00ad", "").Replace("\u200b", "").Replace("\u200c", "").Replace("\u200d", "").Replace("\ufeff", "");
            t = t.Replace('\u00a0', ' ');
            return t.Trim();
        }
    }

    // ---------------- 翻译结果 ----------------

    public class TranslationResult
    {
        public string Source = "";
        public string Text = "";
        public string From = "";
        public string To = "";
        public string Detected = "";
        public string Phonetic = "";
        public string Pos = "";
        public List<string> Senses;
        public string Ctx = "";
        public string Engine = "";
        public string EngineLabel = "";
        public string Degraded = "";
        public long Elapsed;
        public DateTime CreatedAt = DateTime.Now;
    }

    // ---------------- HTTP ----------------

    internal static class Http
    {
        private static bool _tls;
        public static void EnsureTls()
        {
            if (_tls) return;
            _tls = true;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
        }

        public class Response
        {
            public int Status;
            public string Body = "";
            public string Error = "";
            public bool Ok { get { return Status >= 200 && Status < 300; } }
        }

        public static Response Post(string url, string body, string contentType, Dictionary<string, string> headers, int timeoutMs)
        {
            var res = new Response();
            try
            {
                EnsureTls();
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.Timeout = timeoutMs <= 0 ? 20000 : timeoutMs;
                req.ReadWriteTimeout = req.Timeout;
                req.ContentType = contentType;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
                req.Accept = "*/*";
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                if (headers != null)
                {
                    foreach (var kv in headers)
                    {
                        if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(kv.Key, "Referer", StringComparison.OrdinalIgnoreCase)) { req.Referer = kv.Value; continue; }
                        try { req.Headers[kv.Key] = kv.Value; } catch { }
                    }
                }
                byte[] data = Encoding.UTF8.GetBytes(body ?? "");
                req.ContentLength = data.Length;
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (var r = (HttpWebResponse)req.GetResponse())
                {
                    res.Status = (int)r.StatusCode;
                    using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                        res.Body = sr.ReadToEnd();
                }
            }
            catch (WebException wex)
            {
                var r = wex.Response as HttpWebResponse;
                if (r != null)
                {
                    res.Status = (int)r.StatusCode;
                    try
                    {
                        using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                            res.Body = sr.ReadToEnd();
                    }
                    catch { }
                }
                else
                {
                    res.Error = wex.Message;
                }
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
            }
            return res;
        }

        public static Response Get(string url, Dictionary<string, string> headers, int timeoutMs)
        {
            var res = new Response();
            try
            {
                EnsureTls();
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs <= 0 ? 20000 : timeoutMs;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
                req.Accept = "text/html,application/xhtml+xml";
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                if (headers != null)
                {
                    foreach (var kv in headers)
                    {
                        if (string.Equals(kv.Key, "Referer", StringComparison.OrdinalIgnoreCase)) { req.Referer = kv.Value; continue; }
                        try { req.Headers[kv.Key] = kv.Value; } catch { }
                    }
                }
                using (var r = (HttpWebResponse)req.GetResponse())
                {
                    res.Status = (int)r.StatusCode;
                    using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                        res.Body = sr.ReadToEnd();
                }
            }
            catch (WebException wex)
            {
                var r = wex.Response as HttpWebResponse;
                if (r != null)
                {
                    res.Status = (int)r.StatusCode;
                    try
                    {
                        using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                            res.Body = sr.ReadToEnd();
                    }
                    catch { }
                }
                else res.Error = wex.Message;
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
            }
            return res;
        }
    }

    internal static class Errors
    {
        public static string Friendly(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return "翻译失败";
            string m = msg;
            if (Regex.IsMatch(m, "timeout|超时|timed out", RegexOptions.IgnoreCase)) return "请求超时，请稍后重试";
            if (Regex.IsMatch(m, "401|invalid api key|unauthorized", RegexOptions.IgnoreCase)) return "API Key 无效或无权限，请在设置里检查";
            if (Regex.IsMatch(m, "402|insufficient|balance|余额", RegexOptions.IgnoreCase)) return "账户余额不足";
            if (Regex.IsMatch(m, "429|rate limit|too many", RegexOptions.IgnoreCase)) return "请求太频繁，请稍后再试";
            if (m.IndexOf("无法连接", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("远程名称", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("基础连接", StringComparison.Ordinal) >= 0 ||
                Regex.IsMatch(m, "name resolution|ENOTFOUND|forcibly closed|connection.*closed", RegexOptions.IgnoreCase))
                return "网络连接失败，请检查网络或代理";
            return m;
        }
    }
}
