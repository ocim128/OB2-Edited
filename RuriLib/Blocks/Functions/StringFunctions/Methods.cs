using RuriLib.Attributes;
using RuriLib.Extensions;
using RuriLib.Functions.Http;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RuriLib.Functions.Parsing;
using Newtonsoft.Json.Linq;

namespace RuriLib.Blocks.Functions.String
{
    [BlockCategory("String Functions", "Blocks for working with strings", "#9acd32")]
    public static class Methods
    {
        private static readonly Uri TranslateEndpointBaseUri = new("https://clients5.google.com/");

        #region RandomString fields
        private static readonly string _lowercase = "abcdefghijklmnopqrstuvwxyz";
        private static readonly string _uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private static readonly string _digits = "0123456789";
        private static readonly string _symbols = "\\!\"£$%&/()=?^'{}[]@#,;.:-_*+";
        private static readonly string _hex = _digits + "abcdef";
        private static readonly string _udChars = _uppercase + _digits;
        private static readonly string _ldChars = _lowercase + _digits;
        private static readonly string _upperlwr = _lowercase + _uppercase;
        private static readonly string _ludChars = _lowercase + _uppercase + _digits;
        private static readonly string _allChars = _lowercase + _uppercase + _digits + _symbols;
        #endregion

        [Block("Rounds the value down to the nearest integer")]
        public static int CountOccurrences(BotData data, [Variable] string input, string word)
        {
            var occurrences = input.CountOccurrences(word);
            data.Logger.LogHeader();
            data.Logger.Log($"Found {occurrences} occurrences of {word}", LogColors.YellowGreen);
            return occurrences;
        }

        [Block("Retrieves a piece of an input string")]
        public static string Substring(BotData data, [Variable] string input, int index, int length)
        {
            var substring = input.Substring(index, length);
            data.Logger.LogHeader();
            data.Logger.Log($"Retrieved substring: {substring}", LogColors.YellowGreen);
            return substring;
        }

        [Block("Reverses the characters in the input string")]
        public static string Reverse(BotData data, [Variable] string input)
        {
            char[] charArray = input.ToCharArray();
            Array.Reverse(charArray);
            var reversed = new string(charArray);
            data.Logger.LogHeader();
            data.Logger.Log($"Reversed {input} with result {reversed}", LogColors.YellowGreen);
            return reversed;
        }

        [Block("Removes leading or trailing whitespace from the input string")]
        public static string Trim(BotData data, [Variable] string input)
        {
            var trimmed = input.Trim();
            data.Logger.LogHeader();
            data.Logger.Log("Trimmed the input string", LogColors.YellowGreen);
            return trimmed;
        }

        [Block("Gets the length of a string")]
        public static int Length(BotData data, [Variable] string input)
        {
            var length = input.Length;
            data.Logger.LogHeader();
            data.Logger.Log($"Calculated length: {length}", LogColors.YellowGreen);
            return length;
        }

        [Block("Changes all letters of a string to uppercase")]
        public static string ToUppercase(BotData data, [Variable] string input)
        {
            var upper = input.ToUpper();
            data.Logger.LogHeader();
            data.Logger.Log($"Converted the input string: {upper}", LogColors.YellowGreen);
            return upper;
        }

        [Block("Changes all letters of a string to lowercase")]
        public static string ToLowercase(BotData data, [Variable] string input)
        {
            var lower = input.ToLower();
            data.Logger.LogHeader();
            data.Logger.Log($"Converted the input string: {lower}", LogColors.YellowGreen);
            return lower;
        }

        [Block("Replaces all occurrences of some text in a string")]
        public static string Replace(BotData data, [Variable] string original, string toReplace, string replacement)
        {
            if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(toReplace))
                return original;

            var replaced = original.Replace(toReplace, replacement);
            data.Logger.LogHeader();
            data.Logger.Log($"Replaced string: {replaced}", LogColors.YellowGreen);
            return replaced;
        }

        [Block("Replaces all regex matches with a given text",
            extraInfo = "The replacement can contain regex groups with syntax like $1$2")]
        public static string RegexReplace(BotData data, [Variable] string original, string pattern, string replacement)
        {
            if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(pattern))
                return original;

            var regex = RegexCache.GetOrCreate(pattern);
            var replaced = regex.Replace(original, replacement);
            data.Logger.LogHeader();
            data.Logger.Log($"Replaced string: {replaced}", LogColors.YellowGreen);
            return replaced;
        }

        [Block("Translates text in a string basing on a dictionary")]
        public static string Translate(BotData data, [Variable] string input, Dictionary<string, string> translations,
            bool replaceOne = false)
        {
            if (string.IsNullOrEmpty(input) || translations == null || translations.Count == 0)
                return input;

            var sb = new StringBuilder(input.Length * 2); // Pre-allocate capacity
            var replacements = 0;
            var currentIndex = 0;
            var inputLength = input.Length;
            
            // Sort translations by length descending to handle longer matches first
            var sortedTranslations = translations
                .OrderByDescending(e => e.Key.Length)
                .Where(e => !string.IsNullOrEmpty(e.Key))
                .ToArray();

            while (currentIndex < inputLength)
            {
                bool replaced = false;
                foreach (var entry in sortedTranslations)
                {
                    var key = entry.Key;
                    var keyLength = key.Length;
                    
                    if (currentIndex + keyLength <= inputLength &&
                        string.CompareOrdinal(input.Substring(currentIndex, keyLength), key) == 0)
                    {
                        sb.Append(entry.Value);
                        currentIndex += keyLength;
                        replacements++;
                        replaced = true;
                        if (replaceOne) break;
                        break;
                    }
                }
                
                if (!replaced)
                {
                    sb.Append(input[currentIndex]);
                    currentIndex++;
                }
                
                if (replaceOne && replacements > 0) break;
            }

            var translated = sb.ToString();
            data.Logger.LogHeader();
            data.Logger.Log($"Translated {replacements} occurrence(s). Translated string: {translated}", LogColors.YellowGreen);

            return translated;
        }

        [Block("Translates text to a target language using a public translation endpoint",
            extraInfo = "Returns the first translation candidate from the endpoint response")]
        public static async Task<string> TranslateLanguage(BotData data,
            [Variable] [BlockParam("Input", "The text to translate")] string input,
            [BlockParam("Target Language", "The target language code, e.g. en")] string targetLanguage,
            [BlockParam("Source Language", "The source language code or auto")] string sourceLanguage = "auto")
            => await TranslateLanguageCoreAsync(data, input, targetLanguage, sourceLanguage, TranslateEndpointBaseUri)
                .ConfigureAwait(false);

        internal static async Task<string> TranslateLanguageCoreAsync(BotData data, string input, string targetLanguage,
            string sourceLanguage, Uri endpointBaseUri)
        {
            if (string.IsNullOrWhiteSpace(targetLanguage))
                throw new ArgumentException("Target language cannot be null or empty", nameof(targetLanguage));

            data.Logger.LogHeader();

            if (string.IsNullOrEmpty(input))
            {
                data.Logger.Log("Input was empty, nothing to translate", LogColors.YellowGreen);
                return string.Empty;
            }

            sourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage.Trim();
            targetLanguage = targetLanguage.Trim();

            using var httpClient = HttpFactory.GetHttpClient(data.UseProxy ? data.Proxy : null, new HttpOptions
            {
                ConnectTimeout = TimeSpan.FromMilliseconds(30000),
                ReadWriteTimeout = TimeSpan.FromMilliseconds(30000)
            }, null);

            using var body = new ByteArrayContent("0"u8.ToArray());
            body.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                BuildTranslateLanguageUri(endpointBaseUri, input, sourceLanguage, targetLanguage))
            {
                Content = body
            };

            using var response = await httpClient.SendAsync(request, data.CancellationToken).ConfigureAwait(false);
            var responseBody = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(data.CancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException($"Translation request failed with status code {(int)response.StatusCode}");

            var (translatedText, detectedLanguage) = ParseTranslationLanguageResponse(responseBody);
            data.Logger.Log($"Translated text: {translatedText}", LogColors.YellowGreen);

            if (!string.IsNullOrWhiteSpace(detectedLanguage))
            {
                data.Logger.Log($"Detected source language: {detectedLanguage}", LogColors.YellowGreen);
            }

            return translatedText;
        }

        internal static (string TranslatedText, string DetectedLanguage) ParseTranslationLanguageResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                throw new InvalidOperationException("Translation response body was empty");

            var token = JToken.Parse(responseBody);

            if (token is not JArray { Count: > 0 } array)
                throw new InvalidOperationException("Translation response was not in the expected array format");

            if (array[0] is JValue firstValue && firstValue.Type == JTokenType.String)
            {
                return (firstValue.Value<string>() ?? string.Empty, string.Empty);
            }

            var firstCandidate = array[0] as JArray;
            var translatedText = firstCandidate?.ElementAtOrDefault(0)?.Value<string>();
            var detectedLanguage = firstCandidate?.ElementAtOrDefault(1)?.Value<string>() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(translatedText))
                throw new InvalidOperationException("Translation response did not contain a translation candidate");

            return (translatedText, detectedLanguage);
        }

        private static Uri BuildTranslateLanguageUri(Uri endpointBaseUri, string input, string sourceLanguage, string targetLanguage)
            => new(endpointBaseUri,
                $"translate_a/t?client=dict-chrome-ex&sl={Uri.EscapeDataString(sourceLanguage)}&tl={Uri.EscapeDataString(targetLanguage)}&q={Uri.EscapeDataString(input)}");

        [Block("URL encodes a string")]
        public static string UrlEncode(BotData data, [Variable] string input)
        {
            // The maximum allowed Uri size is 2083 characters, we use 2080 as a precaution
            var encoded = string.Join("", input.SplitInChunks(2080).Select(Uri.EscapeDataString));
            data.Logger.LogHeader();
            data.Logger.Log($"URL Encoded string: {encoded}", LogColors.YellowGreen);
            return encoded;
        }

        [Block("URL decodes a string")]
        public static string UrlDecode(BotData data, [Variable] string input)
        {
            var decoded = Uri.UnescapeDataString(input);
            data.Logger.LogHeader();
            data.Logger.Log($"URL Decoded string: {decoded}", LogColors.YellowGreen);
            return decoded;
        }

        [Block("Encodes HTML entities in a string")]
        public static string EncodeHTMLEntities(BotData data, [Variable] string input)
        {
            var encoded = WebUtility.HtmlEncode(input);
            data.Logger.LogHeader();
            data.Logger.Log($"Encoded string: {encoded}", LogColors.YellowGreen);
            return encoded;
        }

        [Block("Decodes HTML entities in a string")]
        public static string DecodeHTMLEntities(BotData data, [Variable] string input)
        {
            var decoded = WebUtility.HtmlDecode(input);
            data.Logger.LogHeader();
            data.Logger.Log($"Decoded string: {decoded}", LogColors.YellowGreen);
            return decoded;
        }

        [Block("Generates a random string given a mask",
            extraInfo = "?l = Lowercase, ?u = Uppercase, ?d = Digit, ?f = Uppercase + Lowercase, ?s = Symbol, ?h = Hex (Lowercase), ?H = Hex (Uppercase), ?m = Upper + Digits, ?n = Lower + Digits, ?i = Lower + Upper + Digits, ?a = Any, ?c = Custom")]
        public static string RandomString(BotData data, string input, string customCharset = "0123456789")
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var result = new StringBuilder(input.Length * 2);
            var random = data.Random;

            for (int i = 0; i < input.Length; i++)
            {
                if (i + 1 < input.Length && input[i] == '?')
                {
                    char next = input[i + 1];
                    string charset = next switch
                    {
                        'l' => _lowercase,
                        'u' => _uppercase,
                        'd' => _digits,
                        's' => _symbols,
                        'h' => _hex,
                        'H' => _hex.ToUpper(),
                        'a' => _allChars,
                        'm' => _udChars,
                        'n' => _ldChars,
                        'i' => _ludChars,
                        'f' => _upperlwr,
                        'c' => customCharset,
                        _ => null
                    };

                    if (charset != null)
                    {
                        result.Append(charset[random.Next(charset.Length)]);
                        i++; // Skip the next character
                    }
                    else
                    {
                        result.Append(input[i]);
                    }
                }
                else
                {
                    result.Append(input[i]);
                }
            }

            var generated = result.ToString();
            data.Logger.LogHeader();
            data.Logger.Log($"Generated string: {generated}", LogColors.YellowGreen);
            return generated;
        }

        [Block("Unescapes characters in a string")]
        public static string Unescape(BotData data, [Variable] string input)
        {
            var unescaped = Regex.Unescape(input);
            data.Logger.LogHeader();
            data.Logger.Log($"Unescaped: {unescaped}", LogColors.YellowGreen);
            return unescaped;
        }

        [Block("Splits a string into a list")]
        public static List<string> Split(BotData data, [Variable] string input, string separator)
        {
            var split = input.Split(separator, StringSplitOptions.None).ToList();
            data.Logger.LogHeader();
            data.Logger.Log($"Split the string into {split.Count}", LogColors.YellowGreen);
            return split;
        }

        [Block("Gets the character at a specific index")]
        public static string CharAt(BotData data, [Variable] string input, int index)
        {
            var character = input[index].ToString();
            data.Logger.LogHeader();
            data.Logger.Log($"The character at index {index} is {character}", LogColors.YellowGreen);
            return character;
        }
    }
}
