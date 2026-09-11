using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Ground43.Api.Infrastructure;

public sealed class AttachmentStorage
{
    private readonly StorageOptions _options;
    public string RootPath { get; }
    public string FilesPath { get; }
    public string TempPath { get; }
    public string BackupPath { get; }

    public AttachmentStorage(IOptions<StorageOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        RootPath = Path.GetFullPath(Path.IsPathRooted(_options.RootPath) ? _options.RootPath : Path.Combine(environment.ContentRootPath, _options.RootPath));
        FilesPath = Path.Combine(RootPath, "attachments");
        TempPath = Path.Combine(RootPath, "uploads");
        BackupPath = Path.Combine(RootPath, "backups");
        Directory.CreateDirectory(FilesPath);
        Directory.CreateDirectory(TempPath);
        Directory.CreateDirectory(BackupPath);
    }

    public string UploadDirectory(Guid uploadId) => Path.Combine(TempPath, uploadId.ToString("N"));
    public string ChunkPath(Guid uploadId, int index) => Path.Combine(UploadDirectory(uploadId), $"{index:D8}.part");
    public string RequirementDirectory(string requirementId) => Path.Combine(FilesPath, SafeSegment(requirementId));
    public string FullPath(string relativePath) => Path.GetFullPath(Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public async Task<(string RelativePath, string Sha256)> AssembleAsync(Guid uploadId, string requirementId, Guid attachmentId, string extension, int totalChunks, CancellationToken cancellationToken)
    {
        var directory = RequirementDirectory(requirementId);
        Directory.CreateDirectory(directory);
        var fileName = attachmentId.ToString("N") + extension;
        var target = Path.Combine(directory, fileName);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var index = 0; index < totalChunks; index++)
        {
            var chunk = ChunkPath(uploadId, index);
            if (!File.Exists(chunk)) throw new InvalidOperationException($"缺少分片 {index}");
            await using var input = new FileStream(chunk, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        return (Path.GetRelativePath(RootPath, target).Replace('\\', '/'), Convert.ToHexString(hash.GetHashAndReset()));
    }

    public void DeleteUpload(Guid uploadId)
    {
        var directory = UploadDirectory(uploadId);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    public void DeleteFile(string relativePath)
    {
        var full = FullPath(relativePath);
        if (File.Exists(full)) File.Delete(full);
    }

    private static string SafeSegment(string value) => string.Concat(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
}
