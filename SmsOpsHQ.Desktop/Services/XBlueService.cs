using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using SmsOpsHQ.Core.Utilities;

namespace SmsOpsHQ.Desktop.Services;

public readonly record struct XBlueConnectionTest(bool Ok, string Message);

public readonly record struct XBlueDialResult(bool Ok, int StatusCode, string Message);

// Fanvil X/U: remote dial via ConfigManApp keypad (one GET per digit, then ENTER or POUND per dial plan).
// xmlService Dial often returns RetCode 0 without placing a call on some firmware.
public sealed class XBlueService : IDisposable
{
    private readonly HttpClient _httpClient;
    private string _xblueIp = string.Empty;
    private bool _enabled;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private bool _speakerBeforeDial = true;
    private string _outboundPrefix = string.Empty;
    /// <summary>ConfigManApp key after digits: ENTER, or POUND when phone uses “Press # to invoke dialing”.</summary>
    private string _configManSendKey = "ENTER";

    public bool IsConfigured => _enabled && !string.IsNullOrEmpty(_xblueIp);

    public XBlueService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public void Configure(
        string ipAddress,
        bool enabled,
        string username,
        string password,
        bool speakerBeforeDial = true,
        string outboundPrefix = "",
        bool pressPoundToSend = false)
    {
        _xblueIp = ipAddress.Trim();
        _enabled = enabled;
        _username = username.Trim();
        _password = password;
        _speakerBeforeDial = speakerBeforeDial;
        _outboundPrefix = new string((outboundPrefix ?? "").Where(char.IsDigit).ToArray());
        _configManSendKey = pressPoundToSend ? "POUND" : "ENTER";
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        if (string.IsNullOrEmpty(_username))
            return;

        string pair = $"{_username}:{_password}";
        string b64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(pair));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", b64);
    }

    private async Task<HttpResponseMessage> SendGetAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyBasicAuth(request);
        return await _httpClient.SendAsync(request);
    }

    public async Task<XBlueConnectionTest> TestConnectionAsync(string? ipOverride = null)
    {
        string host = !string.IsNullOrWhiteSpace(ipOverride) ? ipOverride.Trim() : _xblueIp;
        if (string.IsNullOrWhiteSpace(host))
            return new XBlueConnectionTest(false, "Enter the phone IP address.");

        string[] urls =
        [
            $"http://{host}/",
            $"http://{host}/cgi-bin/ConfigManApp.com"
        ];

        Exception? last = null;
        foreach (string url in urls)
        {
            try
            {
                using HttpResponseMessage resp = await SendGetAsync(url);
                int code = (int)resp.StatusCode;
                string authHint = code != 401
                    ? string.Empty
                    : string.IsNullOrEmpty(_username)
                        ? " Phone returned 401 — set HTTP Basic user/password (often admin / admin) in VoIP settings."
                        : " Credentials were rejected or missing — check username/password in VoIP settings.";

                return new XBlueConnectionTest(true,
                    $"Reachable — {url} returned HTTP {code} {resp.ReasonPhrase}.{authHint}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
            }
        }

        string detail = last is TaskCanceledException
            ? "Timed out. Check the IP, cabling, and that the phone allows HTTP from this PC."
            : $"Cannot reach phone: {last?.Message ?? "Unknown error."}";
        return new XBlueConnectionTest(false, detail);
    }

    public async Task<XBlueDialResult> DialAsync(string phoneNumber)
    {
        if (!IsConfigured)
            return new XBlueDialResult(false, 0, "VoIP not configured (enable XBlue and set IP in Settings).");

        string? digits = PhoneUtils.GetDialString(phoneNumber);
        if (string.IsNullOrEmpty(digits))
            return new XBlueDialResult(false, 0, "No dialable digits in phone number.");

        string toDial = CombineOutboundPrefix(_outboundPrefix, digits);
        string baseUrl = $"http://{_xblueIp}/cgi-bin/ConfigManApp.com";

        try
        {
            if (_speakerBeforeDial)
            {
                using (HttpResponseMessage sp = await SendGetAsync($"{baseUrl}?key=SPEAKER"))
                {
                }

                await Task.Delay(120);
            }

            // 1) Keypad: one ConfigManApp GET per digit. A single request with key=7;1;8;… makes IP9G/Fanvil
            // concatenate into one "number" (device log: dialed number = 7%3B1%3B8%…) instead of discrete keys.
            XBlueDialResult kp = await TryKeypadSequentialDialAsync(baseUrl, toDial, _configManSendKey);
            if (kp.Ok)
                return kp;

            if (kp.StatusCode == 401)
                return kp;

            // 2) xmlService Dial — many devices acknowledge without originating; used after keypad HTTP failure.
            XBlueDialResult? xml = await TryPostXmlDialAsync(toDial);
            if (xml.HasValue && xml.Value.Ok)
                return xml.Value;

            string? xmlHint = xml.HasValue && !xml.Value.Ok ? xml.Value.Message : null;

            string urlCombined = $"{baseUrl}?key={Uri.EscapeDataString($"DIAL:{toDial};{_configManSendKey}")}";
            using (HttpResponseMessage response = await SendGetAsync(urlCombined))
            {
                int code = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    string msg =
                        $"Sent ConfigManApp DIAL+{_configManSendKey}."
                        + (xmlHint is not null ? $" xmlService note: {xmlHint}" : "");
                    return new XBlueDialResult(true, code, msg);
                }

                if (code == 401)
                {
                    return new XBlueDialResult(false, code,
                        (response.ReasonPhrase ?? "Unauthorized") + " Check admin user/password on the VoIP tab.");
                }
            }

            using (HttpResponseMessage r1 = await SendGetAsync($"{baseUrl}?key={Uri.EscapeDataString($"DIAL:{toDial}")}"))
            {
                if (!r1.IsSuccessStatusCode)
                {
                    int c1 = (int)r1.StatusCode;
                    string hint = c1 == 401 ? " Check VoIP username/password." : string.Empty;
                    string fail = (r1.ReasonPhrase ?? "Error") + hint;
                    if (xmlHint is not null)
                        fail += " " + xmlHint;
                    return new XBlueDialResult(false, c1, fail);
                }
            }

            await Task.Delay(150);

            string sendEsc = Uri.EscapeDataString(_configManSendKey);
            using (HttpResponseMessage r2 = await SendGetAsync($"{baseUrl}?key={sendEsc}"))
            {
                int code2 = (int)r2.StatusCode;
                if (r2.IsSuccessStatusCode)
                {
                    return new XBlueDialResult(true, code2,
                        $"Sent DIAL then {_configManSendKey} (fallback)."
                        + (xmlHint is not null ? $" xmlService: {xmlHint}" : ""));
                }

                return new XBlueDialResult(false, code2,
                    $"DIAL succeeded but {_configManSendKey} failed: " + (r2.ReasonPhrase ?? "Error")
                    + (xmlHint is not null ? " " + xmlHint : ""));
            }
        }
        catch (Exception ex)
        {
            return new XBlueDialResult(false, 0, ex.Message);
        }
    }

    /// <summary>ConfigManApp: one HTTP request per digit (matches physical key flow), then ENTER or POUND.</summary>
    private async Task<XBlueDialResult> TryKeypadSequentialDialAsync(string baseUrl, string digitsOnly, string terminator)
    {
        if (string.IsNullOrEmpty(digitsOnly))
            return new XBlueDialResult(false, 0, "No digits for keypad dial.");

        const int delayBetweenDigitsMs = 70;
        const int delayBeforeTerminatorMs = 100;

        try
        {
            foreach (char c in digitsOnly)
            {
                if (!char.IsDigit(c))
                    continue;

                string token = c.ToString();
                string url = $"{baseUrl}?key={Uri.EscapeDataString(token)}";
                using HttpResponseMessage response = await SendGetAsync(url);
                int code = (int)response.StatusCode;
                if (code == 401)
                {
                    return new XBlueDialResult(false, code,
                        (response.ReasonPhrase ?? "Unauthorized") + " Check admin user/password on the VoIP tab.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new XBlueDialResult(false, code,
                        $"Keypad digit [{token}] HTTP {code}: {response.ReasonPhrase ?? "Error"}");
                }

                await Task.Delay(delayBetweenDigitsMs);
            }

            await Task.Delay(delayBeforeTerminatorMs);

            string termUrl = $"{baseUrl}?key={Uri.EscapeDataString(terminator)}";
            using HttpResponseMessage termResp = await SendGetAsync(termUrl);
            int termCode = (int)termResp.StatusCode;
            if (termResp.IsSuccessStatusCode)
            {
                string sendHint = string.Equals(terminator, "POUND", StringComparison.Ordinal)
                    ? "then # (POUND) per dial plan. "
                    : "then ENTER. ";
                return new XBlueDialResult(true, termCode,
                    "Keypad: sent each digit separately " + sendHint
                    + "If the call still drops, fix SIP line registration first — device logs \"Line=[0] not usable\" mean the line cannot place calls until Line shows registered in the phone UI.");
            }

            if (termCode == 401)
            {
                return new XBlueDialResult(false, termCode,
                    (termResp.ReasonPhrase ?? "Unauthorized") + " Check admin user/password on the VoIP tab.");
            }

            return new XBlueDialResult(false, termCode,
                $"Keypad send ({terminator}) HTTP {termCode}: {termResp.ReasonPhrase ?? "Error"}");
        }
        catch (Exception ex)
        {
            return new XBlueDialResult(false, 0, ex.Message);
        }
    }

    /// <summary>Fanvil/OpenVox HTTP API: POST /xmlService with FanvilPhoneExecute Dial URI.</summary>
    private async Task<XBlueDialResult?> TryPostXmlDialAsync(string dialString)
    {
        string uriToken = $"Dial:{dialString}";
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
            "<FanvilPhoneExecute beep=\"no\">\r\n" +
            $"<ExecuteItem>URI=&quot;{EscapeXml(uriToken)}&quot;</ExecuteItem>\r\n" +
            "</FanvilPhoneExecute>";

        string url = $"http://{_xblueIp}/xmlService";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyBasicAuth(request);
            request.Content = new StringContent(xml, Encoding.UTF8, "text/xml");
            using HttpResponseMessage resp = await _httpClient.SendAsync(request);
            int http = (int)resp.StatusCode;
            string body = await resp.Content.ReadAsStringAsync();

            if (resp.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!resp.IsSuccessStatusCode)
                return new XBlueDialResult(false, http, $"xmlService HTTP {http}: {resp.ReasonPhrase}");

            if (TryParseFanvilRetCode(body, out int rc))
            {
                if (rc == 0)
                {
                    return new XBlueDialResult(true, http,
                        "Outbound dial via Fanvil xmlService (Dial execute). Check handset/PBX if no audio.");
                }

                return new XBlueDialResult(false, http,
                    $"xmlService RetCode={rc}. Try outbound prefix (e.g. 9) in VoIP settings if calling outside lines. Body: {Truncate(body, 160)}");
            }

            // Some firmware omits RetCode on success
            if (body.Contains("FanvilPhoneExecute", StringComparison.OrdinalIgnoreCase)
                || body.Contains("<RetCode", StringComparison.OrdinalIgnoreCase))
            {
                return new XBlueDialResult(true, http,
                    "xmlService returned OK (check RetCode if present). Verify SIP registration on the phone.");
            }

            return new XBlueDialResult(false, http, "xmlService unexpected body: " + Truncate(body, 180));
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static string EscapeXml(string s)
    {
        return s
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static bool TryParseFanvilRetCode(string xml, out int retCode)
    {
        retCode = -1;
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        Match m = Regex.Match(xml, @"<RetCode>\s*(\d+)\s*</RetCode>", RegexOptions.IgnoreCase);
        if (!m.Success)
            return false;

        return int.TryParse(m.Groups[1].Value, out retCode);
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    /// <summary>Prefix (e.g. 9) before national numbers; extensions (&lt;10 digits) stay unprefixed.</summary>
    private static string CombineOutboundPrefix(string prefixDigits, string numberDigits)
    {
        if (string.IsNullOrEmpty(prefixDigits))
            return numberDigits;

        if (numberDigits.Length >= 10)
            return prefixDigits + numberDigits;

        return numberDigits;
    }

    public async Task<bool> ToggleSpeakerAsync()
    {
        return await SendCommandAsync("SPEAKER");
    }

    public async Task<bool> VolumeUpAsync()
    {
        return await SendCommandAsync("VOLUME_UP");
    }

    public async Task<bool> VolumeDownAsync()
    {
        return await SendCommandAsync("VOLUME_DOWN");
    }

    public async Task<bool> ToggleMuteAsync()
    {
        return await SendCommandAsync("MUTE");
    }

    public async Task<bool> ToggleHeadsetAsync()
    {
        return await SendCommandAsync("HEADSET");
    }

    private async Task<bool> SendCommandAsync(string command)
    {
        if (!IsConfigured) return false;

        try
        {
            string url = $"http://{_xblueIp}/cgi-bin/ConfigManApp.com?key={command}";
            using HttpResponseMessage response = await SendGetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
