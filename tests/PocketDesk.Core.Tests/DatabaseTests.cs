using Microsoft.Data.Sqlite;
using PocketDesk.Core;
using PocketDesk.Core.Tests.Infrastructure;

namespace PocketDesk.Core.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public void Initialize_CreatesSchemaV2_WithWidgetsTable()
    {
        using var workspace = new TempWorkspace();

        using var connection = Database.Open();
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        Assert.Equal(2, Convert.ToInt32(versionCommand.ExecuteScalar()));

        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        using var reader = tableCommand.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("boxes", tables);
        Assert.Contains("items", tables);
        Assert.Contains("journal", tables);
        Assert.Contains("settings", tables);
        Assert.Contains("widgets", tables);
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        using var workspace = new TempWorkspace();
        Database.Initialize();
        Database.Initialize();

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_version;";
        Assert.Equal(1, Convert.ToInt32(command.ExecuteScalar()));
    }

    [Fact]
    public void WidgetsTable_HasExpectedColumns()
    {
        using var workspace = new TempWorkspace();

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(widgets);";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("id", columns);
        Assert.Contains("widget_type_id", columns);
        Assert.Contains("settings_json", columns);
        Assert.Contains("x", columns);
        Assert.Contains("y", columns);
    }

    [Fact]
    public void MigrateV1Database_PreservesBoxes_AndAddsWidgets()
    {
        var root = Path.Combine(Path.GetTempPath(), "PocketDesk-New.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AppPaths.OverrideRoot(root);
        Database.Reset();
        LogService.Reset();

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = AppPaths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE schema_version (version INTEGER NOT NULL);
                    INSERT INTO schema_version(version) VALUES (1);
                    CREATE TABLE boxes (
                        id TEXT PRIMARY KEY, name TEXT NOT NULL, type INTEGER NOT NULL,
                        target_path TEXT NULL, appearance_json TEXT NOT NULL DEFAULT '{}',
                        window_json TEXT NULL, z_order INTEGER NOT NULL DEFAULT 0,
                        is_pinned INTEGER NOT NULL DEFAULT 0, is_visible INTEGER NOT NULL DEFAULT 1,
                        sort_field TEXT NOT NULL DEFAULT 'name', sort_desc INTEGER NOT NULL DEFAULT 0,
                        created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                    CREATE TABLE items (
                        id TEXT PRIMARY KEY, box_id TEXT NOT NULL, kind INTEGER NOT NULL,
                        display_name TEXT NOT NULL, source_path TEXT NULL, stored_path TEXT NULL,
                        icon_cache_key TEXT NULL, sort_index REAL NOT NULL DEFAULT 0,
                        added_at TEXT NOT NULL, original_box_id TEXT NULL,
                        is_link INTEGER NOT NULL DEFAULT 0, modified_at TEXT NULL,
                        size_bytes INTEGER NOT NULL DEFAULT 0, shortcut_target TEXT NULL);
                    CREATE TABLE journal (
                        id TEXT PRIMARY KEY, op_type INTEGER NOT NULL, box_id TEXT NULL,
                        item_id TEXT NULL, from_path TEXT NULL, to_path TEXT NULL,
                        state INTEGER NOT NULL, created_at TEXT NOT NULL, completed_at TEXT NULL);
                    CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                    INSERT INTO boxes(id, name, type, appearance_json, created_at, updated_at)
                    VALUES ('v1-box', '旧版收纳盒', 0, '{}', '2026-01-01T00:00:00.0000000', '2026-01-01T00:00:00.0000000');
                    """;
                command.ExecuteNonQuery();
            }

            Database.Initialize();

            using var migrated = Database.Open();
            using var versionCommand = migrated.CreateCommand();
            versionCommand.CommandText = "SELECT version FROM schema_version LIMIT 1;";
            Assert.Equal(2, Convert.ToInt32(versionCommand.ExecuteScalar()));

            Assert.Equal("旧版收纳盒", BoxRepository.Get("v1-box")!.Name);
            using var widgetCommand = migrated.CreateCommand();
            widgetCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='widgets';";
            Assert.Equal(1, Convert.ToInt32(widgetCommand.ExecuteScalar()));
            Assert.True(Directory.EnumerateFiles(AppPaths.BackupDir, "*.bak").Any());
        }
        finally
        {
            Database.Reset();
            SqliteConnection.ClearAllPools();
            AppPaths.Reset();
            LogService.Reset();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
