namespace TAS.Services;

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using TAS.ViewModels;

public sealed class TaskStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public TaskStore()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TAS");
        Directory.CreateDirectory(appDataDir);

        var dbPath = Path.Combine(appDataDir, "tasks.db");
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS tasks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                duration TEXT NOT NULL,
                start_time TEXT NOT NULL DEFAULT '',
                end_time TEXT NOT NULL DEFAULT '',
                is_completed INTEGER NOT NULL DEFAULT 0,
                date TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """;
        cmd.ExecuteNonQuery();

        EnsureColumn("tasks", "start_time", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("tasks", "end_time", "TEXT NOT NULL DEFAULT ''");
    }

    private void EnsureColumn(string tableName, string columnName, string definition)
    {
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }

    /// <summary>Load tasks for a specific date (format: yyyy-MM-dd).</summary>
    public List<TaskEntry> LoadTasks(string date)
    {
        var tasks = new List<TaskEntry>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, duration, start_time, end_time, is_completed FROM tasks WHERE date = $date ORDER BY id";
        cmd.Parameters.AddWithValue("$date", date);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var entry = new TaskEntry(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4))
            {
                Id = reader.GetInt64(0),
                IsCompleted = reader.GetInt64(5) == 1
            };
            tasks.Add(entry);
        }

        return tasks;
    }

    /// <summary>Insert a new task and return its row id.</summary>
    public long InsertTask(string name, string duration, string startTime, string endTime, string date)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO tasks (name, duration, start_time, end_time, is_completed, date)
            VALUES ($name, $duration, $startTime, $endTime, 0, $date);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$duration", duration);
        cmd.Parameters.AddWithValue("$startTime", startTime);
        cmd.Parameters.AddWithValue("$endTime", endTime);
        cmd.Parameters.AddWithValue("$date", date);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Update the completed status of a task.</summary>
    public void UpdateCompleted(long id, bool completed)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE tasks SET is_completed = $val WHERE id = $id";
        cmd.Parameters.AddWithValue("$val", completed ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Update editable task fields.</summary>
    public void UpdateTask(long id, string name, string duration, string startTime, string endTime)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE tasks
            SET name = $name,
                duration = $duration,
                start_time = $startTime,
                end_time = $endTime
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$duration", duration);
        cmd.Parameters.AddWithValue("$startTime", startTime);
        cmd.Parameters.AddWithValue("$endTime", endTime);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Delete a single task.</summary>
    public void DeleteTask(long id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM tasks WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Delete all tasks for a specific date.</summary>
    public void ClearDate(string date)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM tasks WHERE date = $date";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
