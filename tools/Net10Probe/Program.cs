using System;
using System.IO;
using System.Data.SQLite;
using Spotnet.Helpers;
using Spotnet.Model;
using System.Runtime.InteropServices;
using System.Windows;

// No Spotnet.App instance or real user profile: exercise published dependencies.
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        int failures = 0;
        void Check(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }
        Console.WriteLine(RuntimeInformation.FrameworkDescription);
        Check("bundled runtime", () => {
            string runtime = typeof(object).Assembly.Location;
            Console.WriteLine(runtime);
            if (!runtime.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Runtime was loaded outside the published folder.");
        });
        Check("native SQLite", () => {
            using var db = new SQLiteConnection("Data Source=:memory:");
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "select sqlite_version()";
            Console.WriteLine(cmd.ExecuteScalar());
        });
        Check("WPF theme", () => {
            _ = new Application(); // Registers WPF pack resources; no Spotnet startup.
            var resources = new ResourceDictionary {
                Source = new Uri("pack://application:,,,/Spotnet;component/style/moderndark.xaml")
            };
            if (resources.Count == 0) throw new Exception("Empty theme.");
        });
        Check("JSON disk cache round trip", () => {
            string directory = Path.Combine(AppContext.BaseDirectory, "cache-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            // Preserve this isolated diagnostic directory for inspection.
            var cache = new JsonSpotCache(directory);
            cache.Save(new SpotEx { MessageId = "migration-probe", Body = "cache-value", ImageSource = new byte[] { 1, 2, 3 } });
            var reopened = new JsonSpotCache(directory);
            var value = reopened.Get("migration-probe");
            if (value?.Body != "cache-value" || value.ImageSource?.Length != 3)
                throw new Exception("Persisted cache value could not be read back.");
        });
        return failures == 0 ? 0 : 1;
    }
}
