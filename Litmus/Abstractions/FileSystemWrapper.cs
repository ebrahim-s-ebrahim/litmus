namespace Litmus.Abstractions;

public class FileSystemWrapper : IFileSystem
{
    public string ReadAllText(string path) => File.ReadAllText(path);

    public IEnumerable<string> GetFiles(string directory, string pattern, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.GetFiles(directory, pattern, option);
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);
}
