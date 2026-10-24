using System.IO;
namespace MH4UModern.ViewModels;

public sealed class BackupRowViewModel
{
    public BackupRowViewModel(FileInfo file)
    {
        File = file;
    }

    public FileInfo File { get; }
    public string Name => File.Name;
    public string Date => File.LastWriteTime.ToString("MMM d, yyyy  h:mm tt");
    public string Size => $"{File.Length / 1024.0:N1} KB";
}
