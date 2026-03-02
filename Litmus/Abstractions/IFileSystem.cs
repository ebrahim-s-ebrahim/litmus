namespace Litmus.Abstractions;

public interface IFileSystem
{
    string ReadAllText(string path);
    IEnumerable<string> GetFiles(string directory, string pattern, bool recursive);
    bool FileExists(string path);
    bool DirectoryExists(string path);
}
