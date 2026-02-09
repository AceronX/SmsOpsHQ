using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SmsOpsHQ.Desktop.Views;

public partial class MediaViewerView : Window
{
    public MediaViewerView()
    {
        InitializeComponent();
    }

    // Load image from raw bytes (e.g. from ApiClient.ProxyMediaAsync).
    public void LoadFromBytes(byte[] imageData, string? info = null)
    {
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(imageData);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        MediaImage.Source = bitmap;
        MediaInfo.Text = info ?? $"{imageData.Length / 1024} KB";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
