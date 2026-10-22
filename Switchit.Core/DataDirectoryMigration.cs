namespace Switchit.Core;

public static class DataDirectoryMigration
{
    /// <summary>Copy legacy records once, keeping the source intact and never replacing existing data.</summary>
    public static bool CopyIfNeeded(string legacyDirectory, string currentDirectory)
    {
        if (Directory.Exists(currentDirectory) || !Directory.Exists(legacyDirectory)) return false;
        var target = Path.GetFullPath(currentDirectory);
        var source = Path.GetFullPath(legacyDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The new data directory must not be inside the legacy directory.");
        var staging = target + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(legacyDirectory, "*", options))
            {
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                var destination = Path.Combine(staging, Path.GetRelativePath(legacyDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            Directory.Move(staging, target);
            return true;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
