using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Data.SQLite;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.DAL;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Views
{
    public enum DbRecoveryAction
    {
        Repaired,
        /// <summary>Rows salvaged into a fresh database file; the damaged one kept as .bak.</summary>
        Rebuilt,
        Cleaned,
        Closed
    }

    public partial class DbRecoveryWindow : MetroWindow
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        public DbRecoveryAction SelectedAction { get; private set; } = DbRecoveryAction.Closed;

        public DbRecoveryWindow(string reason = "")
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(reason))
            {
                ReasonTextBlock.Text = reason;
            }
        }

        public static DbRecoveryAction Prompt(Window owner = null, string reason = "")
        {
            App.CloseSplash();
            DbRecoveryWindow dlg = new DbRecoveryWindow(reason);
            if (owner != null) dlg.Owner = owner;
            dlg.ShowDialog();
            return dlg.SelectedAction;
        }

        private async void RepairButton_Click(object sender, RoutedEventArgs e)
        {
            SetWorkingState("Running quick repair and checkpointing SQLite databases...");

            bool success = await Task.Run(() => PerformQuickRepair());
            if (success)
            {
                SelectedAction = DbRecoveryAction.Repaired;
                // A repair runs unattended for long enough that the user has usually gone
                // to do something else by the time it finishes.
                NotificationHelper.NotifyDatabaseRecovered("Quick repair completed. Spotnet is starting normally.");
                DialogResult = true;
                Close();
            }
            else
            {
                StatusTextBlock.Text = "Quick repair encountered an issue. You can try 'Clean Reset' instead.";
                ProgressBarControl.Visibility = Visibility.Collapsed;
                EnableButtons(true);
            }
        }

        private async void RebuildButton_Click(object sender, RoutedEventArgs e)
        {
            SetWorkingState("Salvaging spots into a fresh database. This can take a few minutes...");

            string dbFile = AppHelper.GetDbFilename("dbs");
            SpotsDbRebuilder.RebuildResult result = await Task.Run(() => SpotsDbRebuilder.Rebuild(dbFile));
            if (result.Succeeded)
            {
                ClearConfigurationFlags();
                StatusTextBlock.Text = $"Recovered {result.SpotsRecovered} spots.";
                NotificationHelper.NotifyDatabaseRecovered($"Rebuild completed. {result.SpotsRecovered} spots recovered.");
                SelectedAction = DbRecoveryAction.Rebuilt;
                DialogResult = true;
                Close();
            }
            else
            {
                StatusTextBlock.Text = "Rebuild could not read the database. 'Clean Reset' will start fresh from your provider.";
                ProgressBarControl.Visibility = Visibility.Collapsed;
                EnableButtons(true);
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            SetWorkingState("Backing up databases and resetting...");

            try
            {
                string programData = AppHelper.SettingsFolder;
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // Backup .dbs and .dbc
                foreach (var file in Directory.GetFiles(programData, "*.db*"))
                {
                    if (file.EndsWith(".dbs") || file.EndsWith(".dbc"))
                    {
                        string backupName = $"{file}.{timestamp}.bak";
                        try
                        {
                            File.Move(file, backupName);
                            Log.Info("Backed up {0} to {1}", file, backupName);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("Could not backup file: {0} ({1})", file, ex.Message);
                        }
                    }
                }

                ClearConfigurationFlags();
                SelectedAction = DbRecoveryAction.Cleaned;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                StatusTextBlock.Text = "Reset failed: " + ex.Message;
                ProgressBarControl.Visibility = Visibility.Collapsed;
                EnableButtons(true);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = DbRecoveryAction.Closed;
            DialogResult = false;
            Close();
        }

        private bool PerformQuickRepair()
        {
            try
            {
                string programData = AppHelper.SettingsFolder;

                // 1. Checkpoint and unlock all .dbs and .dbc SQLite files
                string[] dbFiles = Directory.GetFiles(programData, "*.db*");
                foreach (var db in dbFiles)
                {
                    if (db.EndsWith(".dbs") || db.EndsWith(".dbc"))
                    {
                        try
                        {
                            // Stay in WAL. Forcing "Journal Mode=Delete" here used to convert
                            // the database back to the rollback journal, quietly undoing the
                            // crash-safety the rest of the app depends on.
                            using var conn = new SQLiteConnection($"Data Source={db};Version=3;Journal Mode=WAL;BusyTimeout=5000;");
                            conn.Open();
                            // REINDEX and quick_check walk the whole schema, which
                            // includes the FTS5 virtual tables.
                            Fts5Module.Register(conn);
                            using var cmd = conn.CreateCommand();
                            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                            cmd.ExecuteNonQuery();

                            cmd.CommandText = "REINDEX;";
                            cmd.ExecuteNonQuery();

                            // Report what shape the file is actually in, so the log says
                            // whether Quick Repair was enough or a Rebuild is needed.
                            cmd.CommandText = "PRAGMA quick_check(1);";
                            string check = Convert.ToString(cmd.ExecuteScalar());
                            if (!"ok".Equals(check, StringComparison.OrdinalIgnoreCase))
                            {
                                Log.Warn("quick_check on {0} reports: {1}", db, check);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("Error checkpointing db {0}: {1}", db, ex.Message);
                        }
                    }
                }

                // 2. Synchronize watermarks in userinfo
                string[] dbsFiles = Directory.GetFiles(programData, "*.dbs");
                foreach (var dbs in dbsFiles)
                {
                    try
                    {
                        using var conn = new SQLiteConnection($"Data Source={dbs};Version=3;");
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        // Matches the shape SpotProvider creates. It declared a PRIMARY KEY
                        // here, which IF NOT EXISTS could never apply to an existing table -
                        // so the INSERT OR REPLACE below silently appended duplicate rows
                        // instead of replacing, and readers that take the first match could
                        // then pick up a stale watermark.
                        cmd.CommandText = SpotsSchema.CreateUserInfo + ";";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "SELECT MIN(rowid), MAX(rowid) FROM spots;";
                        using var reader = cmd.ExecuteReader();
                        if (reader.Read() && !reader.IsDBNull(0))
                        {
                            long minId = reader.GetInt64(0);
                            reader.Close();

                            // Delete-then-insert works whether or not the table has a key.
                            cmd.CommandText = "DELETE FROM userinfo WHERE field = 'minId_headers';";
                            cmd.ExecuteNonQuery();

                            cmd.CommandText = "INSERT INTO userinfo(field, value) VALUES('minId_headers', @val);";
                            cmd.Parameters.AddWithValue("@val", minId.ToString());
                            cmd.ExecuteNonQuery();
                            cmd.Parameters.Clear();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Error aligning watermarks in {0}: {1}", dbs, ex.Message);
                    }
                }

                // 3. Clear auto-wipe malformed flags in user settings
                ClearConfigurationFlags();

                return true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                return false;
            }
        }

        private void ClearConfigurationFlags()
        {
            Settings.Default.SpotsDbFileMalformed = false;
            Settings.Default.CommentsDbFileMalformed = false;
            Settings.Default.RecreateDbScheduled = false;
            Settings.Default.Save();

            // Also clean user.config on disk
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string spotnetConfigDir = Path.Combine(localAppData, "Spotnet");
                if (Directory.Exists(spotnetConfigDir))
                {
                    foreach (var configFile in Directory.GetFiles(spotnetConfigDir, "user.config", SearchOption.AllDirectories))
                    {
                        string content = File.ReadAllText(configFile);
                        bool modified = false;
                        if (content.Contains("SpotsDbFileMalformed"))
                        {
                            content = Regex.Replace(content, @"(<setting name=""SpotsDbFileMalformed""[^>]*>\s*<value>)[^<]*(</value>)", "${1}False${2}");
                            modified = true;
                        }
                        if (content.Contains("CommentsDbFileMalformed"))
                        {
                            content = Regex.Replace(content, @"(<setting name=""CommentsDbFileMalformed""[^>]*>\s*<value>)[^<]*(</value>)", "${1}False${2}");
                            modified = true;
                        }
                        if (content.Contains("RecreateDbScheduled"))
                        {
                            content = Regex.Replace(content, @"(<setting name=""RecreateDbScheduled""[^>]*>\s*<value>)[^<]*(</value>)", "${1}False${2}");
                            modified = true;
                        }
                        if (modified) File.WriteAllText(configFile, content);
                    }
                }
            }
            catch { }
        }

        private void SetWorkingState(string status)
        {
            StatusTextBlock.Text = status;
            ProgressBarControl.Visibility = Visibility.Visible;
            EnableButtons(false);
        }

        private void EnableButtons(bool enable)
        {
            RepairButton.IsEnabled = enable;
            RebuildButton.IsEnabled = enable;
            ResetButton.IsEnabled = enable;
            CloseButton.IsEnabled = enable;
        }
    }
}
