namespace SmsOpsHQ.Desktop.Services;

public interface IPhoneDialer
{
    bool IsConfigured { get; }
    Task<XBlueDialResult> DialAsync(string phoneNumber);
}
