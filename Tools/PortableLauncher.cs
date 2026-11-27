using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

internal static class PortableLauncher
{
    [STAThread]
    private static int Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Ferryman-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (FileStream package = File.OpenRead(Assembly.GetExecutingAssembly().Location))
            using (var game = new ZipArchive(package, ZipArchiveMode.Read))
                game.ExtractToDirectory(directory);

            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(directory, "Ferryman.exe"),
                WorkingDirectory = directory,
                UseShellExecute = false
            }))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Ferryman", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
