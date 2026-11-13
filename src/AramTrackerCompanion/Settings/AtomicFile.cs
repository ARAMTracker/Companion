namespace AramTrackerCompanion.Settings;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, contents);
        File.Move(temporary, path, overwrite: true);
    }

    public static void WriteAllBytes(string path, byte[] contents)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, contents);
        File.Move(temporary, path, overwrite: true);
    }
}
