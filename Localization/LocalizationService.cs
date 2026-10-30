using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using Colossal;
using Colossal.Localization;

namespace MedianGrassReplacement.Localization
{
    // Owns only managed dictionaries and its own native localization sources.
    // No game assets, entities, render memory, save callbacks or background work.
    internal static class LocalizationService
    {
        internal static readonly string[] Locales = { "en-US", "de-DE", "es-ES", "fr-FR", "it-IT", "nl-NL", "pl-PL", "pt-BR", "ru-RU", "uk-UA", "cs-CZ", "tr-TR", "ja-JP", "ko-KR", "zh-HANS", "zh-HANT", "da-DK", "sv-SE", "nb-NO", "fi-FI", "is-IS" };
        private static readonly Dictionary<string, Dictionary<string, string>> Tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object TableLock = new object();
        private static readonly Dictionary<string, Source> Sources = new Dictionary<string, Source>(StringComparer.Ordinal);
        private static LocalizationManager _manager;
        private static Setting _setting;
        private static string _phraseLocale;
        private static Regex _phrases;
        internal static int Revision { get; private set; }
        internal static string Selected => Normalize(_manager?.activeLocaleId);
        internal static string Normalize(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return "en-US";
            if (locale.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) || locale.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)) return "zh-HANS";
            if (locale.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) || locale.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)) return "zh-HANT";
            if (locale.StartsWith("no", StringComparison.OrdinalIgnoreCase)) return "nb-NO";
            foreach (var known in Locales) if (known.Equals(locale, StringComparison.OrdinalIgnoreCase)) return known;
            foreach (var known in Locales) if (locale.Length >= 2 && known.Substring(0, 2).Equals(locale.Substring(0, 2), StringComparison.OrdinalIgnoreCase)) return known;
            return "en-US";
        }
        internal static void Register(LocalizationManager manager, Setting setting)
        {
            Dispose(); if (manager == null || setting == null) return;
            _manager = manager; _setting = setting;
            // Register only supported game locales; later language packs are handled
            // by onSupportedLocalesChanged. Always follow the active game language.
            foreach (var locale in manager.GetSupportedLocales().Concat(new[] { "en-US" }).Distinct(StringComparer.Ordinal))
            {
                var source = new Source(setting, locale); Sources.Add(locale, source);
                manager.AddSource(locale, source);
            }
            manager.onActiveDictionaryChanged += Changed;
            manager.onSupportedLocalesChanged += SupportedLocalesChanged;
            Changed();
        }
        private static void SupportedLocalesChanged()
        {
            if (_manager == null || _setting == null) return;
            foreach (var locale in _manager.GetSupportedLocales())
            {
                if (Sources.ContainsKey(locale)) continue;
                var source = new Source(_setting, locale); Sources.Add(locale, source);
                _manager.AddSource(locale, source);
            }
            Changed();
        }
        private static void Changed() { unchecked { Revision++; } }
        internal static void Dispose()
        {
            var manager = _manager;
            if (manager != null) { manager.onActiveDictionaryChanged -= Changed; manager.onSupportedLocalesChanged -= SupportedLocalesChanged; }
            foreach (var pair in Sources)
            {
                pair.Value.Detach();
                try { manager?.RemoveSource(pair.Key, pair.Value); }
                catch (Exception ex) { Mod.Log.Warn("Could not unregister Curbside locale: " + ex.Message); }
            }
            Sources.Clear(); lock (TableLock) Tables.Clear(); _phrases = null; _phraseLocale = null; _setting = null; _manager = null; Changed();
        }
        internal static Dictionary<string, string> Table(string locale)
        {
            locale = Normalize(locale);
            // Native locale preloading may read sources concurrently. Publish only complete tables.
            lock (TableLock)
            {
                if (Tables.TryGetValue(locale, out var cached)) return cached;
                try
                {
                    using (var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream("MedianGrassReplacement.Localization." + locale + ".json"))
                    {
                        if (stream != null)
                        {
                            var serializer = new DataContractJsonSerializer(typeof(Dictionary<string, string>), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                            var table = serializer.ReadObject(stream) as Dictionary<string, string>;
                            return Tables[locale] = table ?? new Dictionary<string, string>(StringComparer.Ordinal);
                        }
                    }
                }
                catch (System.Runtime.Serialization.SerializationException) { /* Damaged translation: retain English. */ }
                return Tables[locale] = new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }
        internal static string Text(string english)
        {
            if (english == null || Selected == "en-US") return english;
            var locale = Selected; var table = Table(locale);
            if (table.TryGetValue(english, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
            if (_phraseLocale != locale)
            {
                _phraseLocale = locale;
                var parts = table.Where(pair => pair.Key != pair.Value && pair.Key.Trim().Length > 1 && !string.IsNullOrWhiteSpace(pair.Value)).OrderByDescending(pair => pair.Key.Length).Select(pair =>
                    (Regex.IsMatch(pair.Key, "^[A-Za-z]") ? @"\b" : "") + Regex.Escape(pair.Key) + (Regex.IsMatch(pair.Key, "[A-Za-z]$") ? @"\b" : ""));
                string expression = string.Join("|", parts);
                _phrases = expression.Length == 0 ? null : new Regex(@"(?:[A-Za-z]:[\\/][^\r\n""<>]*|[^\s\\/]+\.(?:png|jpg|jpeg|dds|json|log|cok|fbx)\b)|" + expression);
            }
            return _phrases?.Replace(english, match => table.TryGetValue(match.Value, out var replacement) ? replacement : match.Value) ?? english;
        }
        internal static string NativeText(string english)
        {
            if (_manager?.activeDictionary != null && _manager.activeDictionary.TryGetValue(Key(english), out var translated) && !string.IsNullOrWhiteSpace(translated)) return translated;
            return Text(english);
        }
        internal static Game.UI.Localization.LocalizedString Localized(string english) => Game.UI.Localization.LocalizedString.IdWithFallback(Key(english), english);
        internal static string Key(string text)
        {
            unchecked { uint hash = 2166136261; foreach (char value in text) { hash ^= value; hash *= 16777619; } return "Curbside.Text[" + hash.ToString("x8") + "]"; }
        }
        private sealed class Source : IDictionarySource
        {
            private Setting _owner;
            private readonly string _locale;
            internal Source(Setting owner, string locale) { _owner = owner; _locale = locale; }
            internal void Detach() { _owner = null; }
            public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
            {
                var owner = _owner;
                if (owner == null) return Array.Empty<KeyValuePair<string, string>>();
                string locale = Normalize(_locale);
                var translated = Table(locale);
                var entries = new Dictionary<string, string>();
                foreach (var pair in new LocaleEN(owner).ReadEntries(errors, indexCounts))
                    entries[pair.Key] = translated.TryGetValue(pair.Value, out var value) && !string.IsNullOrWhiteSpace(value) ? value : pair.Value;
                foreach (var pair in Table("en-US")) entries[Key(pair.Key)] = translated.TryGetValue(pair.Key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : pair.Value;
                return entries;
            }
            public void Unload() { /* A native reload is not mod disposal. */ }
        }
    }
}
