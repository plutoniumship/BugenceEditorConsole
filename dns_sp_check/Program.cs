using Microsoft.Data.Sqlite;

var dbPath = @"C:\PublishedNew\app.db";
using var conn = new SqliteConnection($"Data Source={dbPath};Cache=Shared");
conn.Open();

Console.WriteLine("== CertificateWebhook rows ==");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = @"
SELECT Id, OwnerUserId, CompanyId, Name, Category, Host, Port, RouteUrl, UpdatedAtUtc
FROM SystemProperties
WHERE lower(Category)='certificatewebhook'
ORDER BY UpdatedAtUtc DESC, Id DESC;";
    using var r = cmd.ExecuteReader();
    var any = false;
    while (r.Read())
    {
        any = true;
        Console.WriteLine($"Id={r.GetInt64(0)} Owner={r.GetString(1)} Company={(r.IsDBNull(2) ? "null" : r.GetString(2))} Host={(r.IsDBNull(5) ? "" : r.GetString(5))} Port={(r.IsDBNull(6) ? "" : r.GetString(6))} Route={(r.IsDBNull(7) ? "" : r.GetString(7))} Updated={(r.IsDBNull(8) ? "" : r.GetString(8))}");
    }
    if (!any) Console.WriteLine("none");
}

Console.WriteLine("== Latest 3 SystemProperties ==");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = @"
SELECT Id, OwnerUserId, CompanyId, Name, Category, UpdatedAtUtc
FROM SystemProperties
ORDER BY UpdatedAtUtc DESC, Id DESC
LIMIT 3;";
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        Console.WriteLine($"Id={r.GetInt64(0)} Owner={r.GetString(1)} Company={(r.IsDBNull(2) ? "null" : r.GetString(2))} Name={(r.IsDBNull(3) ? "" : r.GetString(3))} Category={(r.IsDBNull(4) ? "" : r.GetString(4))} Updated={(r.IsDBNull(5) ? "" : r.GetString(5))}");
    }
}

var apiKey = Environment.GetEnvironmentVariable("Webhook__ApiKey", EnvironmentVariableTarget.Machine) ?? string.Empty;
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("ERR: Webhook__ApiKey not set on machine.");
    return;
}

string? owner = null;
string? company = null;
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT OwnerUserId, CompanyId FROM SystemProperties ORDER BY UpdatedAtUtc DESC, Id DESC LIMIT 1;";
    using var r = cmd.ExecuteReader();
    if (r.Read())
    {
        owner = r.IsDBNull(0) ? null : r.GetString(0);
        company = r.IsDBNull(1) ? null : r.GetString(1);
    }
}

if (string.IsNullOrWhiteSpace(owner))
{
    Console.WriteLine("ERR: cannot infer owner.");
    return;
}

var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = @"
INSERT INTO SystemProperties
(OwnerUserId, CompanyId, DGUID, Name, Category, Username, PasswordEncrypted, Host, Port, RouteUrl, Notes, CreatedAtUtc, UpdatedAtUtc)
VALUES
($owner, $company, lower(hex(randomblob(16))), $name, $category, $username, $password, $host, $port, $route, $notes, $created, $updated);";
    cmd.Parameters.AddWithValue("$owner", owner);
    cmd.Parameters.AddWithValue("$company", (object?)company ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$name", "CertificateWebhook");
    cmd.Parameters.AddWithValue("$category", "CertificateWebhook");
    cmd.Parameters.AddWithValue("$username", apiKey);
    cmd.Parameters.AddWithValue("$password", string.Empty);
    cmd.Parameters.AddWithValue("$host", "http://127.0.0.1");
    cmd.Parameters.AddWithValue("$port", "5097");
    cmd.Parameters.AddWithValue("$route", "/api/certificates/issue");
    cmd.Parameters.AddWithValue("$notes", "Server configured webhook.");
    cmd.Parameters.AddWithValue("$created", now);
    cmd.Parameters.AddWithValue("$updated", now);
    cmd.ExecuteNonQuery();
}

using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT Id, Host, Port, RouteUrl FROM SystemProperties WHERE lower(Category)='certificatewebhook' ORDER BY UpdatedAtUtc DESC, Id DESC LIMIT 1;";
    using var r = cmd.ExecuteReader();
    if (r.Read())
    {
        Console.WriteLine($"INSERTED CertificateWebhook Id={r.GetInt64(0)} endpoint={r.GetString(1)}:{r.GetString(2)}{r.GetString(3)}");
    }
}

Console.WriteLine("== Domain verification token ==");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT Id, DomainName, VerificationToken, Status, SslStatus, UpdatedAtUtc FROM ProjectDomains WHERE lower(DomainName)='www.bizbridgeconnect.com' ORDER BY UpdatedAtUtc DESC, Id DESC LIMIT 1;";
    using var r = cmd.ExecuteReader();
    if (r.Read())
    {
        Console.WriteLine($"Id={r.GetInt64(0)} Domain={r.GetString(1)} Token={r.GetString(2)} Status={r.GetInt32(3)} Ssl={r.GetInt32(4)} Updated={r.GetString(5)}");
    }
}
