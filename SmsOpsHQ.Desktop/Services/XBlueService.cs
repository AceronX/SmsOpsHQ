using System.Net.Http;

namespace SmsOpsHQ.Desktop.Services;

// Click-to-call VoIP service for Fanvil IPG9 via XBlue HTTP API.
// Sends dial commands to the phone system when a user clicks a phone number.
public sealed class XBlueService : IDisposable
{
    private readonly HttpClient _httpClient;
    private string _xblueIp = string.Empty;
    private bool _enabled;

    public bool IsConfigured => _enabled && !string.IsNullOrEmpty(_xblueIp);

    public XBlueService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    // Configure the XBlue endpoint.
    public void Configure(string ipAddress, bool enabled)
    {
        _xblueIp = ipAddress.Trim();
        _enabled = enabled;
    }

    // Dial a phone number. Strips to 10 digits and adds dial prefix.
    public async Task<bool> DialAsync(string phoneNumber)
    {
        if (!IsConfigured) return false;

        string digits = NormalizeForDialing(phoneNumber);
        if (string.IsNullOrEmpty(digits)) return false;

        try
        {
            string url = $"http://{_xblueIp}/cgi-bin/ConfigManApp.com?key=DIAL:{digits}";
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // Toggle speaker on the phone.
    public async Task<bool> ToggleSpeakerAsync()
    {
        return await SendCommandAsync("SPEAKER");
    }

    // Volume up.
    public async Task<bool> VolumeUpAsync()
    {
        return await SendCommandAsync("VOLUME_UP");
    }

    // Volume down.
    public async Task<bool> VolumeDownAsync()
    {
        return await SendCommandAsync("VOLUME_DOWN");
    }

    // Mute toggle.
    public async Task<bool> ToggleMuteAsync()
    {
        return await SendCommandAsync("MUTE");
    }

    // Toggle headset mode.
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
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // Strip phone number to last 10 digits for dialing.
    private static string NormalizeForDialing(string phone)
    {
        string digitsOnly = new(phone.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length >= 10)
            return digitsOnly[^10..];
        return digitsOnly;
    }

    public void Dispose() => _httpClient.Dispose();
}
