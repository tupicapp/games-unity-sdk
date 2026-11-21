// Tupic SDK core: insights-core culture data (calendar, themes) and the game registry, called with the user's token.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Tupic.Core
{
    public sealed class TupicInsights
    {
        readonly TupicEnvironment _env;
        readonly TupicApi _api;
        readonly string _gameId;

        internal TupicInsights(TupicEnvironment env, TupicApi api, string gameId) { _env = env; _api = api; _gameId = gameId; }

        /// <summary>The country of a culture name ("ar-AE" -> "AE"); null when the culture names no country.</summary>
        public static string RegionFromCulture(string cultureName)
        {
            if (string.IsNullOrWhiteSpace(cultureName)) return null;
            var parts = cultureName.Replace('_', '-').Split('-');
            for (var i = parts.Length - 1; i >= 1; i--)
                if (parts[i].Length == 2 && IsAsciiLetters(parts[i])) return parts[i].ToUpperInvariant();
            try
            {
                var ci = CultureInfo.GetCultureInfo(cultureName);
                if (!ci.IsNeutralCulture && !string.IsNullOrEmpty(ci.Name)) return new RegionInfo(ci.Name).TwoLetterISORegionName.ToUpperInvariant();
            }
            catch (ArgumentException) { }
            return null;
        }

        /// <summary>The device's country, from the current culture.</summary>
        public static string DeviceRegion() => RegionFromCulture(CultureInfo.CurrentCulture.Name);

        public Task<Dictionary<string, object>> GetCalendarAsync(string region, CancellationToken ct = default) =>
            _api.JsonAsync("Insights calendar", "GET", _env.InsightsApi + "/geography/countries/" + Country(region) + "/calendar", null, ct);

        /// <summary>The country's theme, or null when Insights has none for it (404).</summary>
        public Task<Dictionary<string, object>> GetThemeAsync(string region, CancellationToken ct = default) =>
            _api.JsonAsync("Insights theme", "GET", _env.InsightsApi + "/themes/country/" + Country(region), null, ct, notFoundIsNull: true);

        public Task<Dictionary<string, object>> RegisterGameAsync(string gameName, string genre, string region, string gameUrl,
            IEnumerable<string> assetUrls = null, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object>
            {
                ["game_id"] = _gameId,
                ["game_name"] = gameName,
                ["genre"] = genre,
                ["country_code"] = string.IsNullOrEmpty(region) ? null : Country(region),
                ["game_url"] = gameUrl,
                ["asset_urls"] = new List<object>(assetUrls ?? Array.Empty<string>()),
                ["metadata"] = new Dictionary<string, object> { ["engine"] = "unity", ["sdk"] = "com.tupic.sdk" },
            };
            return _api.JsonAsync("Insights game registration", "POST", _env.InsightsApi + "/game/register", body, ct);
        }

        static string Country(string region)
        {
            if (region == null || region.Length != 2 || !IsAsciiLetters(region))
                throw new ArgumentException("region must be a two-letter country code, e.g. AE.", nameof(region));
            return region.ToUpperInvariant();
        }

        static bool IsAsciiLetters(string s)
        {
            foreach (var c in s) if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
            return true;
        }
    }

    /// <summary>chain-core, read-only. Rewards are decided and paid server-side; a game can only look.</summary>
    public sealed class TupicChain
    {
        readonly TupicEnvironment _env;
        readonly TupicApi _api;

        internal TupicChain(TupicEnvironment env, TupicApi api) { _env = env; _api = api; }

        public Task<Dictionary<string, object>> GetRewardTypesAsync(CancellationToken ct = default) =>
            _api.JsonAsync("Chain reward types", "GET", _env.ChainApi + "/rewards/types", null, ct);

        public Task<Dictionary<string, object>> GetRewardStatsAsync(string address, CancellationToken ct = default) =>
            _api.JsonAsync("Chain reward stats", "GET", _env.ChainApi + "/rewards/stats/" + Address(address), null, ct);

        /// <summary>A 0x-prefixed 20-byte hex address.</summary>
        public static bool IsAddress(string address)
        {
            if (address == null || address.Length != 42 || !(address.StartsWith("0x") || address.StartsWith("0X"))) return false;
            for (var i = 2; i < address.Length; i++)
                if (Uri.IsHexDigit(address[i]) == false) return false;
            return true;
        }

        static string Address(string address) =>
            IsAddress(address) ? address : throw new ArgumentException("address must be a 0x-prefixed 40-hex-digit address.", nameof(address));
    }
}
