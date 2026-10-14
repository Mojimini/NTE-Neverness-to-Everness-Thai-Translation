using System;
using System.Threading;

public sealed class DistributionConfig {
    public string Repository { get; set; }
    public string CurrentVersion { get; set; }
    public string UpdaterSha256 { get; set; }
}
public sealed class ContentCatalog {
    public string ReleaseTag { get; set; }
    public ContentGroup[] Groups { get; set; }
}
public sealed class ContentGroup {
    public string Id { get; set; }
    public string AssetName { get; set; }
    public string Sha256 { get; set; }
    public long Bytes { get; set; }
    public ContentFile[] Files { get; set; }
}
public sealed class ContentFile {
    public string Path { get; set; }
    public string Sha256 { get; set; }
    public long Bytes { get; set; }
}
public sealed class ReleaseInfo {
    public string Version { get; set; }
    public string Tag { get; set; }
    public string Notes { get; set; }
    public string InstallerName { get; set; }
    public string InstallerUrl { get; set; }
    public string InstallerSha256 { get; set; }
    public long InstallerBytes { get; set; }
}
public sealed class TransferProgress {
    public string Text { get; set; }
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
}
public interface IContentDelivery {
    void Ensure(string[] groups, Action<TransferProgress> progress, CancellationToken token);
}
public interface IReleaseDelivery {
    ReleaseInfo Check(CancellationToken token);
    string Download(ReleaseInfo release, Action<TransferProgress> progress, CancellationToken token);
}
