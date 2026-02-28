using System;
using Microsoft.Data.Sqlite;

var db = @"C:\PublishedNew\app.db";
using var con = new SqliteConnection($"Data Source={db}");
con.Open();
using var cmd = con.CreateCommand();
cmd.CommandText = @"SELECT Id,Name,Category,Username,Host,Port,RouteUrl,UpdatedAtUtc,substr(PasswordEncrypted,1,20) as Pfx FROM SystemProperties WHERE lower(Category)='smtp' ORDER BY UpdatedAtUtc DESC";
using var r = cmd.ExecuteReader();
Console.WriteLine("Id | Name | Category | Username | Host | Port | RouteUrl | UpdatedAtUtc | Pfx");
while(r.Read())
{
  Console.WriteLine($"{r.GetInt32(0)} | {r.GetString(1)} | {r.GetString(2)} | {r.GetString(3)} | {r.GetString(4)} | {r.GetString(5)} | {r.GetString(6)} | {r.GetString(7)} | {r.GetString(8)}");
}
