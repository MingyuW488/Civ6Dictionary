using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Civ6EnglishDemo;

internal record ApiResult(bool Success, string Text, string Code, double Milliseconds, bool Cancelled = false);
internal record Credentials(int Provider, string Id, string Secret);

internal static class Translation
{
    internal static string YoudaoInput(string text)
    {
        // The service's length and slicing are Unicode characters, not UTF-16 code units.
        var runes = text.EnumerateRunes().Select(r => r.ToString()).ToArray();
        return runes.Length <= 20 ? text : string.Concat(runes.Take(10)) + runes.Length.ToString(CultureInfo.InvariantCulture) + string.Concat(runes.TakeLast(10));
    }

    internal static string Hash(string value, bool sha256) => Convert.ToHexString(sha256
        ? SHA256.HashData(Encoding.UTF8.GetBytes(value)) : MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static Dictionary<string, string> Parameters(string text, Credentials credentials, string salt, string timestamp)
    {
        if (credentials.Provider == 0)
            return new() { ["q"] = text, ["from"] = "en", ["to"] = "zh-CHS", ["appKey"] = credentials.Id, ["salt"] = salt,
                ["curtime"] = timestamp, ["signType"] = "v3", ["sign"] = Hash(credentials.Id + YoudaoInput(text) + salt + timestamp + credentials.Secret, true) };
        return new() { ["q"] = text, ["from"] = "en", ["to"] = "zh", ["appid"] = credentials.Id, ["salt"] = salt,
            ["sign"] = Hash(credentials.Id + text + salt + credentials.Secret, false) };
    }

    internal static HttpClient CreateClient() => new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) };

    internal static async Task<ApiResult> QueryAsync(HttpClient client, string text, Credentials credentials, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var salt = Guid.NewGuid().ToString("N");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            using var content = new FormUrlEncodedContent(Parameters(text, credentials, salt, timestamp));
            var url = credentials.Provider == 0 ? "https://openapi.youdao.com/api" : "https://fanyi-api.baidu.com/api/trans/vip/translate";
            using var response = await client.PostAsync(url, content, token);
            var json = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode) return new(false, "服务器 HTTP 错误", "HTTP " + (int)response.StatusCode, watch.Elapsed.TotalMilliseconds);
            return Parse(json, credentials.Provider, watch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return new(false, token.IsCancellationRequested ? "已取消" : "请求超时", token.IsCancellationRequested ? "CANCELLED" : "TIMEOUT", watch.Elapsed.TotalMilliseconds, token.IsCancellationRequested);
        }
        catch (HttpRequestException) { return new(false, "连接失败，请检查直连网络或证书", "NETWORK", watch.Elapsed.TotalMilliseconds); }
        catch (JsonException) { return new(false, "服务返回了无法识别的数据", "INVALID_JSON", watch.Elapsed.TotalMilliseconds); }
    }

    internal static string ErrorMessage(int provider, string code)
    {
        if (provider != 0) return "百度接口拒绝请求，请按错误码检查 APP ID、密钥、服务权限、余额及限流。";
        return code switch
        {
            "108" => "应用 ID 无效：请从有道控制台复制当前 API 应用的应用 ID 到第一栏；第二栏填写同一应用的应用密钥。不要填成服务实例 ID，也不要将两项互换。",
            "110" => "当前应用未绑定有效服务：请在有道控制台为这个应用绑定已开通的文本翻译（NMT）服务。",
            "202" => "签名校验失败：请检查应用 ID 和应用密钥是否来自同一应用，以及密钥是否已被重置。",
            "205" => "接入平台不一致：当前程序使用 HTTP API，请检查有道应用的接入方式是否为 API。",
            "206" => "时间戳无效：请先在 Windows 设置中同步系统时间。",
            "203" => "当前出口 IP 不在应用允许列表中，请检查有道控制台的 IP 白名单。",
            "401" => "账户余额不足，请在有道控制台检查余额和资源包。",
            "411" or "412" => "请求被限流，请稍后重试并检查服务配额。",
            _ => "有道接口拒绝请求，请按错误码检查应用及服务设置。"
        };
    }

    internal static ApiResult Parse(string json, int provider, double milliseconds)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return new(false, "服务返回结构异常", "INVALID_RESPONSE", milliseconds);
        string field = provider == 0 ? "errorCode" : "error_code";
        if (root.TryGetProperty(field, out var error))
        {
            var code = error.ToString();
            if (code != "0" && code != "52000")
            {
                // Only retain a numeric code; never log arbitrary server content or credentials.
                code = code.Length <= 12 && code.All(char.IsAsciiDigit) ? code : "API_ERROR";
                return new(false, ErrorMessage(provider, code), code, milliseconds);
            }
        }
        string text = "";
        if (provider == 0 && root.TryGetProperty("translation", out var translations) && translations.ValueKind == JsonValueKind.Array)
            text = string.Join("\n", translations.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
        if (provider == 1 && root.TryGetProperty("trans_result", out var results) && results.ValueKind == JsonValueKind.Array)
            text = string.Join("\n", results.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("dst", out var dst) && dst.ValueKind == JsonValueKind.String).Select(x => x.GetProperty("dst").GetString()));
        return string.IsNullOrWhiteSpace(text) ? new(false, "未返回译文", "EMPTY", milliseconds) : new(true, text, "OK", milliseconds);
    }
}

internal static class Statistics
{
    internal static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0) return 0;
        int mid = values.Length / 2;
        return values.Length % 2 == 0 ? (values[mid - 1] + values[mid]) / 2 : values[mid];
    }
    internal static double Percentile(IEnumerable<double> source, double percentile)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0) return 0;
        return values[Math.Clamp((int)Math.Ceiling(percentile * values.Length) - 1, 0, values.Length - 1)];
    }
}
