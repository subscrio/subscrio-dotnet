using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Npgsql;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;

namespace Subscrio.Core.Infrastructure.Database;

internal sealed class DatabaseRow : Dictionary<string, object?>
{
    public string Text(string key) => Convert.ToString(this.GetValueOrDefault(key), CultureInfo.InvariantCulture) ?? "";
    public long Long(string key) => Convert.ToInt64(this.GetValueOrDefault(key), CultureInfo.InvariantCulture);
    public bool Bool(string key) => Convert.ToBoolean(this.GetValueOrDefault(key), CultureInfo.InvariantCulture);
    public DateTime? Date(string key) => this.GetValueOrDefault(key) switch { DateTime d => DateTime.SpecifyKind(d, DateTimeKind.Utc), DateTimeOffset d => d.UtcDateTime, string s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), _ => null };
}

internal sealed class DatabaseSession
{
    private static readonly AsyncLocal<DatabaseSession?> Ambient = new();
    internal IDisposable EnterAmbient()
    {
        var previous = Ambient.Value;
        Ambient.Value = this;
        return new AmbientScope(() => Ambient.Value = previous);
    }
    private sealed class AmbientScope(Action leave) : IDisposable
    {
        public void Dispose() => leave();
    }
    private readonly List<Func<Task>> _afterCommit = new();
    internal static Task AfterCommit(Func<Task> action)
    {
        if (Ambient.Value is { } current)
        {
            current._afterCommit.Add(action);
            return Task.CompletedTask;
        }
        return action();
    }
    internal async Task DispatchAfterCommit()
    {
        var callbacks = _afterCommit.ToArray();
        _afterCommit.Clear();
        foreach (var callback in callbacks)
            await callback();
    }
    private readonly DatabaseConfig _config;
    private readonly DbConnection? _connection;
    private readonly DbTransaction? _transaction;
    internal bool IsSqlServer => _config.DatabaseType == DatabaseType.SqlServer;
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    internal DatabaseSession(DatabaseConfig config, DbConnection? connection = null, DbTransaction? transaction = null)
    {
        _config = config;
        _connection = connection;
        _transaction = transaction;
    }
    private DbConnection Connect()
    {
        var cs = DatabaseInitializer.ApplySslSettings(_config.ConnectionString, _config.DatabaseType, _config.Ssl);
        return IsSqlServer ? new SqlConnection(cs) : new NpgsqlConnection(cs);
    }
    internal async Task<T> Transaction<T>(Func<DatabaseSession, Task<T>> action)
    {
        if (Ambient.Value is { } current)
            return await action(current);
        if (_transaction != null)
            return await action(this);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var store = new DatabaseSession(_config, connection, tx);
        T result;
        using (store.EnterAmbient())
        {
            try
            {
                result = await action(store);
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }
        await store.DispatchAfterCommit();
        return result;
    }
    internal async Task<List<DatabaseRow>> Rows(FormattableString query)
    {
        if (Ambient.Value is { } current && !ReferenceEquals(current, this))
            return await current.Rows(query);
        if (_connection == null)
        {
            await using var connection = Connect();
            await connection.OpenAsync();
            return await new DatabaseSession(_config, connection).Rows(query);
        }
        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        var names = new object[query.ArgumentCount];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = "@p" + i;
            var p = command.CreateParameter();
            p.ParameterName = (string)names[i];
            p.Value = query.GetArgument(i) ?? DBNull.Value;
            if (p.Value == DBNull.Value)
            {
                if (p is NpgsqlParameter np)
                    np.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Unknown;
                else
                    p.DbType = DbType.String;
            }
            command.Parameters.Add(p);
        }
        command.CommandText = Dialect(string.Format(CultureInfo.InvariantCulture, query.Format, names));
        var result = new List<DatabaseRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new DatabaseRow();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            result.Add(row);
        }
        return result;
    }
    internal async Task<DatabaseRow?> One(FormattableString query) => (await Rows(query)).FirstOrDefault();
    internal async Task<DatabaseRow> Require(FormattableString query, string label) => (await One(query)) ?? throw new NotFoundException(label + " not found");
    internal string Dialect(string query)
    {
        if (!IsSqlServer)
            return query;
        query = Regex.Replace(query, @"\bkey\b", "[key]");
        query = query.Replace("NOW()", "SYSUTCDATETIME()").Replace("TRUE", "1").Replace("FALSE", "0");
        return query;
    }
    internal async Task<DatabaseRow> LockCustomer(string key)
    {
        return IsSqlServer ? await Require($"SELECT * FROM subscrio.customers WITH (UPDLOCK,HOLDLOCK) WHERE key={key}", "Customer") : await Require($"SELECT * FROM subscrio.customers WHERE key={key} FOR UPDATE", "Customer");
    }
    internal async Task<DatabaseRow> Insert(string table, Dictionary<string, object?> values)
    {
        var names = values.Keys.ToArray();
        var args = values.Values.ToArray();
        var placeholders = names.Select((name, i) => !IsSqlServer && new[] { "metadata", "result_snapshot", "rule_snapshot" }.Contains(name) ? $"CAST({{{i}}} AS jsonb)" : $"{{{i}}}");
        var command = $"INSERT INTO subscrio.{table} ({string.Join(',', names)}) {(IsSqlServer ? "OUTPUT INSERTED.*" : "")} VALUES({string.Join(',', placeholders)}) {(IsSqlServer ? "" : "RETURNING *")}";
        return await Require(System.Runtime.CompilerServices.FormattableStringFactory.Create(command, args), table);
    }
    internal async Task Update(string table, long id, Dictionary<string, object?> values)
    {
        var args = values.Values.Append<object?>(id).ToArray();
        var sets = values.Keys.Select((name, i) => $"{name}=" + (!IsSqlServer && new[] { "metadata", "result_snapshot", "rule_snapshot" }.Contains(name) ? $"CAST({{{i}}} AS jsonb)" : $"{{{i}}}"));
        await Rows(System.Runtime.CompilerServices.FormattableStringFactory.Create($"UPDATE subscrio.{table} SET {string.Join(',', sets)} WHERE id={{{args.Length - 1}}}", args));
    }
    internal static string Json(object? value) => JsonSerializer.Serialize(value, JsonOptions);
    internal static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    internal static string Iso(DateTime date) => date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    // Versioned encoding shared with TypeScript; independent of JSON escaping/exponent spelling.
    internal static string Fingerprint(object value)
    {
        static string Text(string text) => "s" + text.Length.ToString(CultureInfo.InvariantCulture) + ":" + string.Concat(text.Select(c => ((int)c).ToString("x4", CultureInfo.InvariantCulture)));
        static string Encode(JsonElement node) => node.ValueKind switch
        {
            JsonValueKind.Null => "z",
            JsonValueKind.True => "t",
            JsonValueKind.False => "f",
            JsonValueKind.String => Text(node.GetString()!),
            JsonValueKind.Number => "n" + BitConverter.DoubleToInt64Bits(node.GetDouble() == 0 ? 0 : node.GetDouble()).ToString("x16", CultureInfo.InvariantCulture),
            JsonValueKind.Array => "a[" + string.Concat(node.EnumerateArray().Select(Encode)) + "]",
            JsonValueKind.Object => "o{" + string.Concat(node.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => Text(p.Name) + Encode(p.Value))) + "}",
            _ => throw new ValidationException("Unsupported request metadata")
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("subscrio-request-v1:" + Encode(JsonSerializer.SerializeToElement(value, JsonOptions))))).ToLowerInvariant();
    }
    internal static long Amount(long value, string name = "amount", long minimum = 0)
    {
        if (value < minimum || value > 9007199254740991)
            throw new ValidationException($"{name} must be a safe integer >= {minimum}");
        return value;
    }
    internal static string Key(string value)
    {
        if (!Regex.IsMatch(value, @"^[a-zA-Z0-9_-]{1,255}$"))
            throw new ValidationException("Invalid key");
        return value;
    }
    internal static void RetryKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
            throw new ValidationException("idempotencyKey must contain 1 to 255 characters");
    }
    internal static bool Eligible(DatabaseRow s, DateTime at) => !s.Bool("is_archived") && (!s.Date("activation_date").HasValue || s.Date("activation_date") <= at) && (!s.Date("cancellation_date").HasValue || s.Date("cancellation_date") > at) && (!s.Date("expiration_date").HasValue || s.Date("expiration_date") > at);
    internal static (int Limit, int Offset) Paging(int limit, int offset)
    {
        if (limit < 1 || limit > 500 || offset < 0)
            throw new ValidationException("Invalid pagination");
        return (limit, offset);
    }
}
