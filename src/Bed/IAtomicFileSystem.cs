namespace Bed;

// Internal per-operation seam: tests can stop the filesystem without running process-exit cleanup.
internal interface IAtomicFileSystem
{
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);
    void MoveOverwrite(string source, string target);
    bool Exists(string path);
    void Delete(string path);
}
