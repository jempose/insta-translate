using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace InstaTranslate
{
    /// <summary>微软必应网页翻译（免 Key）</summary>
    internal static class BingEngine
    {
        private class Token
        {
            public string Ig = "";
            public string Iid = "";
            public string Key = "";
            public string Abuse = "";
            public DateTime At = DateTime.MinValue;
        }

        private static Token _token;
        private static readonly object Lock = new object();

        private static Token GetToken(bool force)
        {
            lock (Lock)
            {
                if (!force && _token != null && (DateTime.Now - _token.At).TotalMinutes < 8) return _token;
                var res = Http.Get("https://www.bing.com/translator", null, 15000);
                if (!res.Ok) throw new Exception("必应页面请求失败(" + res.Status + ") " + (res.Error ?? ""));
                string html = res.Body ?? "";
                var t = new Token();
                var mIg = Regex.Match(html, "IG:\"([^\"]+)\"");
                var mIid = Regex.Match(html, "data-iid=\"([^\"]+)\"");
                var mAb = Regex.Match(html, "params_AbusePreventionHelper\\s*=\\s*\\[([0-9]+),\"([^\"]+)\"");
                if (!mIg.Success || !mIid.Success || !mAb.Success)
                    throw new Exception("必应翻译页面结构变化，暂时无法取到 Token");
                t.Ig = mIg.Groups[1].Value;
                t.Iid = mIid.Groups[1].Value;
                t.Key = mAb.Groups[1].Value;
                t.Abuse = mAb.Groups[2].Value;
                t.At = DateTime.Now;
                _token = t;
                return t;
            }
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 200 ? s.Substring(0, 200) : s;
        }

        private static string MapLang(string code)
        {
            switch ((code ?? "").ToLowerInvariant())
            {
                case "zh": return "zh-Hans";
                case "zh-hans": return "zh-Hans";
                case "zh-cn": return "zh-Hans";
                case "auto": return "auto-detect";
                default: return code;
            }
        }

        private static TranslationResult CallOnce(string text, string from, string to, Token t)
        {
            string url = "https://www.bing.com/ttranslatev3?isVertical=1&IG=" + Uri.EscapeDataString(t.Ig) + "&IID=" + Uri.EscapeDataString(t.Iid) + ".1";
            string body = "fromLang=" + Uri.EscapeDataString(from) + "&to=" + Uri.EscapeDataString(to)
                        + "&text=" + Uri.EscapeDataString(text) + "&token=" + Uri.EscapeDataString(t.Abuse)
                        + "&key=" + Uri.EscapeDataString(t.Key);
            var headers = new Dictionary<string, string>();
            headers["Referer"] = "https://www.bing.com/translator";
            headers["Origin"] = "https://www.bing.com";
            var res = Http.Post(url, body, "application/x-www-form-urlencoded", headers, 20000);
            if (!res.Ok)
                throw new Exception("必应翻译失败(" + res.Status + ") " + (res.Error ?? "") + " " + Short(res.Body));
            var arr = Json.To<List<Dictionary<string, object>>>(res.Body);
            if (arr == null || arr.Count == 0) throw new Exception("必应翻译返回为空");
            var first = arr[0];
            var trans = Json.Arr(first, "translations");
            if (trans == null || trans.Count == 0) throw new Exception("必应翻译返回为空");
            var td = trans[0] as Dictionary<string, object>;
            var r = new TranslationResult();
            r.Text = Json.Str(td, "text");
            string det = Json.Str(Json.Sub(first, "detectedLanguage") ?? new Dictionary<string, object>(), "language");
            r.Detected = det;
            return r;
        }

        public static TranslationResult Translate(string text, string from, string to)
        {
            string f = MapLang(from);
            string t = MapLang(to);
            try
            {
                var tok = GetToken(false);
                return CallOnce(text, f, t, tok);
            }
            catch
            {
                var tok = GetToken(true);
                return CallOnce(text, f, t, tok);
            }
        }
    }

    /// <summary>大模型翻译（OpenAI 兼容接口，默认 DeepSeek）</summary>
    internal static class LlmEngine
    {
        private const string WordPrompt =
            "你是一名专业的中英词典编辑兼译者。用户会给你一个被选中的单词或短语，请严格按下面的 JSON 结构输出，不要输出任何解释、代码块或多余文字：\n" +
            "{\"phonetic\":\"音标，如 /ˈhɛloʊ/，无法给出则留空\",\"pos\":\"词性缩写，如 n. / v. / adj. / phr.\",\"trans\":[\"最贴切的释义1\",\"释义2\"],\"ctx\":\"常见语境下的自然译法或用法说明，一句话，可留空\"}\n" +
            "要求：trans 用目标语言表达，按常用程度排序，最多 4 项，不要罗列生僻义。";

        private const string PassagePrompt =
            "你是一名资深翻译，擅长中英互译。请把用户给出的文本翻译成目标语言。\n" +
            "要求：1) 忠实原意，语气自然，符合目标语言习惯，不要逐字硬译；2) 保留原文段落、换行、列表、编号与数字；" +
            "3) 专有名词用通行译法；4) 只输出译文本身，不要任何解释或 Markdown 代码块。";

        public static bool IsWordLike(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length == 0) return false;
            if (t.Length > 40) return false;
            if (Regex.IsMatch(t, "[。！？；!?;\\n]")) return false;
            bool cjk = Regex.IsMatch(t, "[\\u4e00-\\u9fff]");
            if (cjk) return t.Length <= 12;
            int words = Regex.Split(t, "\\s+").Length;
            return words <= 6;
        }

        private static string Chat(Config cfg, string system, string user, int maxTokens, int timeoutMs)
        {
            string baseUrl = (cfg.LlmBaseUrl ?? "https://api.deepseek.com").TrimEnd('/');
            string url = Regex.IsMatch(baseUrl, "/chat/completions$") ? baseUrl : baseUrl + "/chat/completions";
            var payload = new Dictionary<string, object>();
            payload["model"] = string.IsNullOrEmpty(cfg.LlmModel) ? "deepseek-chat" : cfg.LlmModel;
            var msgs = new List<object>();
            msgs.Add(new Dictionary<string, object> { { "role", "system" }, { "content", system } });
            msgs.Add(new Dictionary<string, object> { { "role", "user" }, { "content", user } });
            payload["messages"] = msgs;
            payload["stream"] = false;
            payload["temperature"] = 1.1;
            payload["max_tokens"] = maxTokens;

            var headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + (cfg.LlmApiKey ?? "");
            var res = Http.Post(url, Json.From(payload), "application/json", headers, timeoutMs);
            if (!res.Ok)
            {
                string detail = "";
                var d = Json.Dict(res.Body);
                if (d != null)
                {
                    var err = Json.Sub(d, "error");
                    detail = err != null ? Json.Str(err, "message") : Json.Str(d, "message");
                }
                throw new Exception("大模型请求失败(" + res.Status + ") " + detail + (res.Error ?? ""));
            }
            var root = Json.Dict(res.Body);
            if (root == null) throw new Exception("大模型返回无法解析");
            var choices = Json.Arr(root, "choices");
            if (choices == null || choices.Count == 0) throw new Exception("大模型没有返回结果");
            var c0 = choices[0] as Dictionary<string, object>;
            var msg = Json.Sub(c0, "message");
            string content = Json.Str(msg, "content");
            int safety = content.IndexOf("<ds_safety>", StringComparison.OrdinalIgnoreCase);
            if (safety >= 0) content = content.Substring(0, safety).Trim();
            return content.Trim();
        }

        public static TranslationResult Translate(string text, string from, string to, Config cfg)
        {
            string srcName = Lang.NameOf(from);
            string dstName = Lang.NameOf(to);
            if (Lang.Detect(text) == "zh" && from == "auto") srcName = "中文";
            bool word = IsWordLike(text);
            var r = new TranslationResult();

            if (word)
            {
                string sys = WordPrompt + "\n本次任务：把【" + srcName + "】的选中内容解释并翻译成【" + dstName + "】。";
                string raw = Chat(cfg, sys, text, 400, 30000);
                var d = ParseWord(raw);
                if (d != null)
                {
                    r.Senses = d.Senses;
                    r.Phonetic = d.Phonetic;
                    r.Pos = d.Pos;
                    r.Ctx = d.Ctx;
                    r.Text = string.Join("；", d.Senses.ToArray());
                }
                else
                {
                    r.Text = CleanCodeFence(raw);
                }
                return r;
            }

            string sysPassage = PassagePrompt + "\n本次任务：把下面的" + srcName + "文本翻译成" + dstName + "。";
            int max = Math.Min(4000, Math.Max(512, (int)(text.Length * 2.2)));
            r.Text = CleanCodeFence(Chat(cfg, sysPassage, text, max, 60000));
            return r;
        }

        private static string CleanCodeFence(string s)
        {
            string t = (s ?? "").Trim();
            if (t.StartsWith("```"))
            {
                int nl = t.IndexOf('\n');
                if (nl > 0) t = t.Substring(nl + 1);
                if (t.EndsWith("```")) t = t.Substring(0, t.Length - 3);
            }
            return t.Trim();
        }

        private class WordInfo
        {
            public string Phonetic = "";
            public string Pos = "";
            public string Ctx = "";
            public List<string> Senses = new List<string>();
        }

        private static WordInfo ParseWord(string raw)
        {
            var m = Regex.Match(raw ?? "", "\\{[\\s\\S]*\\}");
            if (!m.Success) return null;
            var d = Json.Dict(m.Value);
            if (d == null) return null;
            var arr = Json.Arr(d, "trans");
            if (arr == null || arr.Count == 0) return null;
            var info = new WordInfo();
            foreach (var o in arr)
            {
                string s = Convert.ToString(o);
                if (!string.IsNullOrEmpty(s)) info.Senses.Add(s);
            }
            if (info.Senses.Count == 0) return null;
            info.Phonetic = Json.Str(d, "phonetic");
            info.Pos = Json.Str(d, "pos");
            info.Ctx = Json.Str(d, "ctx");
            return info;
        }
    }

    // ---------------- 翻译门面 ----------------

    internal static class Translator
    {
        private static readonly Dictionary<string, TranslationResult> Cache = new Dictionary<string, TranslationResult>();
        private static readonly object Lock = new object();

        public static string EngineLabel(string engine)
        {
            switch (engine)
            {
                case "llm": return "大模型";
                case "bing": return "必应";
                default: return "自动";
            }
        }

        public static string PickEngine(Config cfg, string requested)
        {
            string e = string.IsNullOrEmpty(requested) ? cfg.Engine : requested;
            if (e == "llm" || e == "bing") return e;
            return string.IsNullOrEmpty(cfg.LlmApiKey) ? "bing" : "llm";
        }

        public static TranslationResult Translate(string rawText, Config cfg, string engineOverride)
        {
            string text = Lang.Normalize(rawText);
            if (text.Length == 0) throw new Exception("没有选中任何文字");
            if (text.Length > 8000) text = text.Substring(0, 8000);

            string from, to;
            Lang.Direction(text, cfg.TargetLang, out from, out to);
            string engine = PickEngine(cfg, engineOverride);

            string key = engine + "|" + from + "|" + to + "|" + text;
            lock (Lock)
            {
                TranslationResult hit;
                if (Cache.TryGetValue(key, out hit)) return hit;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            TranslationResult r;
            try
            {
                r = engine == "llm"
                    ? LlmEngine.Translate(text, from, to, cfg)
                    : BingEngine.Translate(text, from, to);
            }
            catch (Exception ex)
            {
                if (engine == "llm" && !Regex.IsMatch(ex.Message, "API Key|401|余额|balance", RegexOptions.IgnoreCase))
                {
                    try
                    {
                        r = BingEngine.Translate(text, from, to);
                        r.Degraded = "大模型暂时不可用，已用必应翻译";
                        engine = "bing";
                    }
                    catch
                    {
                        throw new Exception(Errors.Friendly(ex.Message));
                    }
                }
                else
                {
                    throw new Exception(Errors.Friendly(ex.Message));
                }
            }
            sw.Stop();

            r.Source = text;
            r.From = from;
            r.To = to;
            r.Engine = engine;
            r.EngineLabel = EngineLabel(engine);
            r.Elapsed = sw.ElapsedMilliseconds;
            r.CreatedAt = DateTime.Now;
            if (string.IsNullOrEmpty(r.Detected)) r.Detected = from;

            lock (Lock)
            {
                if (Cache.Count > 300) Cache.Clear();
                Cache[key] = r;
            }
            return r;
        }
    }

    // ---------------- 历史与生词本 ----------------

    public class HistoryItem
    {
        public string Source = "";
        public string Translation = "";
        public string From = "";
        public string To = "";
        public string Phonetic = "";
        public string Pos = "";
        public string EngineLabel = "";
        public string CreatedAt = "";
    }

    internal static class Store
    {
        private static readonly object Lock = new object();
        private static List<HistoryItem> _history;
        private static List<HistoryItem> _vocab;

        public static List<HistoryItem> History()
        {
            lock (Lock)
            {
                if (_history == null) _history = Load(Paths.HistoryFile);
                return _history;
            }
        }

        public static List<HistoryItem> Vocab()
        {
            lock (Lock)
            {
                if (_vocab == null) _vocab = Load(Paths.VocabFile);
                return _vocab;
            }
        }

        private static List<HistoryItem> Load(string file)
        {
            try
            {
                if (!File.Exists(file)) return new List<HistoryItem>();
                string json = File.ReadAllText(file, Encoding.UTF8);
                var list = Json.To<List<HistoryItem>>(json);
                return list ?? new List<HistoryItem>();
            }
            catch
            {
                return new List<HistoryItem>();
            }
        }

        private static void Save(List<HistoryItem> list, string file)
        {
            try { File.WriteAllText(file, Json.From(list), Encoding.UTF8); }
            catch (Exception ex) { Log.Write("保存 " + Path.GetFileName(file) + " 失败: " + ex.Message); }
        }

        public static void AddHistory(TranslationResult r)
        {
            if (r == null || string.IsNullOrEmpty(r.Source)) return;
            lock (Lock)
            {
                var list = History();
                if (list.Count > 0 && list[0].Source == r.Source &&
                    (DateTime.Now - ParseTime(list[0].CreatedAt)).TotalSeconds < 60)
                {
                    list[0].Translation = r.Text;
                    Save(list, Paths.HistoryFile);
                    return;
                }
                var item = new HistoryItem();
                item.Source = r.Source;
                item.Translation = r.Text;
                item.From = r.From;
                item.To = r.To;
                item.Phonetic = r.Phonetic ?? "";
                item.Pos = r.Pos ?? "";
                item.EngineLabel = r.EngineLabel;
                item.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                list.Insert(0, item);
                if (list.Count > 800) list.RemoveRange(800, list.Count - 800);
                Save(list, Paths.HistoryFile);
            }
        }

        private static DateTime ParseTime(string s)
        {
            DateTime t;
            return DateTime.TryParse(s, out t) ? t : DateTime.Now;
        }

        public static bool IsVocab(string source)
        {
            if (string.IsNullOrEmpty(source)) return false;
            lock (Lock)
            {
                foreach (var v in Vocab())
                    if (string.Equals(v.Source, source, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
        }

        public static void AddVocab(TranslationResult r)
        {
            if (r == null || string.IsNullOrEmpty(r.Source)) return;
            lock (Lock)
            {
                if (IsVocab(r.Source)) return;
                var list = Vocab();
                var item = new HistoryItem();
                item.Source = r.Source;
                item.Translation = r.Text;
                item.From = r.From;
                item.To = r.To;
                item.Phonetic = r.Phonetic ?? "";
                item.Pos = r.Pos ?? "";
                item.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                list.Insert(0, item);
                Save(list, Paths.VocabFile);
            }
        }

        public static void RemoveVocab(string source)
        {
            lock (Lock)
            {
                var list = Vocab();
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(list[i].Source, source, StringComparison.OrdinalIgnoreCase)) list.RemoveAt(i);
                }
                Save(list, Paths.VocabFile);
            }
        }

        public static void ClearHistory()
        {
            lock (Lock)
            {
                _history = new List<HistoryItem>();
                Save(_history, Paths.HistoryFile);
            }
        }

        public static void Export(string file, bool vocab)
        {
            var sb = new StringBuilder();
            sb.Append('\ufeff');
            sb.AppendLine("\"原文\",\"译文\",\"音标\",\"词性\",\"时间\"");
            foreach (var it in vocab ? Vocab() : History())
            {
                sb.Append('"').Append((it.Source ?? "").Replace("\"", "\"\"")).Append("\",\"");
                sb.Append((it.Translation ?? "").Replace("\"", "\"\"")).Append("\",\"");
                sb.Append((it.Phonetic ?? "").Replace("\"", "\"\"")).Append("\",\"");
                sb.Append((it.Pos ?? "").Replace("\"", "\"\"")).Append("\",\"");
                sb.Append(it.CreatedAt).AppendLine("\"");
            }
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
