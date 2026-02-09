namespace SmsOpsHQ.Core.Services;

// Contract for downloading and storing media attachments from Twilio MMS.
public interface IMediaService
{
    // Download a media file from Twilio and store it locally.
    // Returns the local file path of the downloaded media.
    Task<string> DownloadMediaAsync(string mediaUrl, string phoneNumber,
        CancellationToken cancellationToken = default);
}
