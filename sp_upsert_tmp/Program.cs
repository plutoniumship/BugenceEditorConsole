using Microsoft.Data.Sqlite;

var dbPath = @"C:\PublishedNew\app.db";
var endpointHost = "http://127.0.0.1";
var port = "5097";
var route = "/api/certificates/issue";
var category = "CertificateWebhook";
var name = "Local Certificate Webhook";
var apiKey = Environment.GetEnvironmentVariable("Webhook__ApiKey", EnvironmentVariableTarget.Machine) ?? string.Empty;
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("ERR: Webhook__ApiKey missing.");
    return;
}

using var conn = new SqliteConnection($"Data Source={dbPath};Cache=Shared");
conn.Open();

string owner = string.Empty;
string? company = null;
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT OwnerUserId, CompanyId FROM SystemProperties ORDER BY UpdatedAtUtc DESC, Id DESC LIMIT 1";
    using var r = cmd.ExecuteReader();
    if (r.Read())
    {
        owner = r.IsDBNull(0) ? string.Empty : r.GetString(0);
        company = r.IsDBNull(1) ? null : r.GetString(1);
    }
}

if (string.IsNullOrWhiteSpace(owner))
{
    Console.WriteLine("ERR: could not resolve OwnerUserId from SystemProperties.");
    return;
}

long? existingId = null;
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT Id FROM SystemProperties WHERE lower(Category)='certificatewebhook' AND OwnerUserId=$owner ORDER BY UpdatedAtUtc DESC, Id DESC LIMIT 1";
    cmd.Parameters.AddWithValue("$owner", owner);
    var obj = cmd.ExecuteScalar();
    if (obj != null && obj != DBNull.Value)
    {
        existingId = Convert.ToInt64(obj);
    }
}

var now = DateTime.UtcNow.ToString("O");
if (existingId.HasValue)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"UPDATE SystemProperties
SET Name=$name, Category=$category, Username=$username, PasswordEncrypted=$password, Host=$host, Port=$port, RouteUrl=$route, Notes=$notes, UpdatedAtUtc=$updated
WHERE Id=$id";
    cmd.Parameters.AddWithValue("$name", name);
    cmd.Parameters.AddWithValue("$category", category);
    cmd.Parameters.AddWithValue("$username", apiKey);
    cmd.Parameters.AddWithValue("$password", "");
    cmd.Parameters.AddWithValue("$host", endpointHost);
    cmd.Parameters.AddWithValue("$port", port);
    cmd.Parameters.AddWithValue("$route", route);
    cmd.Parameters.AddWithValue("$notes", "Managed by server setup.");
    cmd.Parameters.AddWithValue("$updated", now);
    cmd.Parameters.AddWithValue("$id", existingId.Value);
    cmd.ExecuteNonQuery();
    Console.WriteLine($"UPDATED_ID={existingId.Value}");
}
else
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"INSERT INTO SystemProperties
(OwnerUserId, CompanyId, DGUID, Name, Category, Username, PasswordEncrypted, Host, Port, RouteUrl, Notes, CreatedAtUtc, UpdatedAtUtc)
VALUES ($owner, $company, lower(hex(randomblob(16))), $name, $category, $username, $password, $host, $port, $route, $notes, $created, $updated)";
    cmd.Parameters.AddWithValue("$owner", owner);
    cmd.Parameters.AddWithValue("$company", (object?)company ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$name", name);
    cmd.Parameters.AddWithValue("$category", category);
    cmd.Parameters.AddWithValue("$username", apiKey);
    cmd.Parameters.AddWithValue("$password", "");
    cmd.Parameters.AddWithValue("$host", endpointHost);
    cmd.Parameters.AddWithValue("$port", port);
    cmd.Parameters.AddWithValue("$route", route);
    cmd.Parameters.AddWithValue("$notes", "Managed by server setup.");
    cmd.Parameters.AddWithValue("$created", now);
    cmd.Parameters.AddWithValue("$updated", now);
    cmd.ExecuteNonQuery();
    using var idCmd = conn.CreateCommand();
    idCmd.CommandText = "SELECT last_insert_rowid()";
    Console.WriteLine($"INSERTED_ID={idCmd.ExecuteScalar()}");
}

using (var verify = conn.CreateCommand())
{
    verify.CommandText = "SELECT Id, Name, Category, Host, Port, RouteUrl, Username FROM SystemProperties WHERE lower(Category)='certificatewebhook' ORDER BY UpdatedAtUtc DESC, Id DESC LIMIT 1";
    using var r = verify.ExecuteReader();
    if (r.Read())
    {
        var uname = r.IsDBNull(6) ? string.Empty : r.GetString(6);
        var masked = uname.Length > 12 ? uname[..6] + "..." + uname[^6..] : "***";
        Console.WriteLine($"CERT_WEBHOOK_OK Id={r.GetInt64(0)} Host={r.GetString(3)} Port={r.GetString(4)} Route={r.GetString(5)} ApiKey={masked}");
    }
}
