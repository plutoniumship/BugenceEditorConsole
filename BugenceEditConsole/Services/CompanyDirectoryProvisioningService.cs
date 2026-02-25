using System.Data;
using System.Data.Common;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public static class CompanyDirectoryProvisioningService
{
    public static async Task EnsureUserCompanyRecordsAsync(
        ApplicationDbContext db,
        ApplicationUser user,
        CompanyProfile company,
        string? fullName,
        string? phone)
    {
        await EnsureDirectoryTablesAsync(db);

        var companyOrdinal = await GetCompanyOrdinalIdAsync(db, company.Id);
        var resolvedName = ResolveFullName(user, fullName);
        var firstName = resolvedName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "User";
        var lastName = string.Join(" ", resolvedName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1));
        if (string.IsNullOrWhiteSpace(lastName))
        {
            lastName = "Member";
        }

        var numericPhone = ParsePhone(phone ?? user.PhoneNumber);
        var profilePic = "/images/bugence-logo.svg";
        var country = string.IsNullOrWhiteSpace(company.Country) ? "N/A" : company.Country!;
        var position = user.IsCompanyAdmin ? "Admin" : "Member";

        await using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var usersCompanyTable = Quote("DUsersCompany", isSqlite);
        var usersTable = Quote("DUsers", isSqlite);
        var companyIdCol = Quote("CompanyID", isSqlite);
        var idCol = Quote("ID", isSqlite);
        var dguidCol = Quote("DGUID", isSqlite);

        // Upsert DUsersCompany first.
        int? usersCompanyId = null;
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = isSqlite
                ? $"SELECT {idCol} FROM {usersCompanyTable} WHERE {companyIdCol} = @company AND lower(FullName) = lower(@fullName) LIMIT 1"
                : $"SELECT TOP 1 {idCol} FROM {usersCompanyTable} WHERE {companyIdCol} = @company AND LOWER(FullName) = LOWER(@fullName)";
            AddParameter(find, "@company", companyOrdinal);
            AddParameter(find, "@fullName", resolvedName);
            var result = await find.ExecuteScalarAsync();
            if (result != null && result is not DBNull)
            {
                usersCompanyId = Convert.ToInt32(result);
            }
        }

        if (usersCompanyId.HasValue)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = $"UPDATE {usersCompanyTable} SET FullName=@fullName, {companyIdCol}=@company, ProfilePic=@pic, Phone=@phone, Position=@position WHERE {idCol}=@id";
            AddParameter(update, "@fullName", resolvedName);
            AddParameter(update, "@company", companyOrdinal);
            AddParameter(update, "@pic", profilePic);
            AddParameter(update, "@phone", numericPhone);
            AddParameter(update, "@position", position);
            AddParameter(update, "@id", usersCompanyId.Value);
            await update.ExecuteNonQueryAsync();
        }
        else
        {
            var nextUsersCompanyId = await GetNextIdAsync(connection, usersCompanyTable, isSqlite);
            await using var insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO {usersCompanyTable} ({dguidCol}, {idCol}, FullName, {companyIdCol}, ProfilePic, Phone, Position) VALUES (@dguid, @id, @fullName, @company, @pic, @phone, @position)";
            AddParameter(insert, "@dguid", Guid.NewGuid().ToString().ToUpperInvariant());
            AddParameter(insert, "@id", nextUsersCompanyId);
            AddParameter(insert, "@fullName", resolvedName);
            AddParameter(insert, "@company", companyOrdinal);
            AddParameter(insert, "@pic", profilePic);
            AddParameter(insert, "@phone", numericPhone);
            AddParameter(insert, "@position", position);
            await insert.ExecuteNonQueryAsync();
        }

        // Then upsert DUsers.
        int? dUsersId = null;
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = isSqlite
                ? $"SELECT {idCol} FROM {usersTable} WHERE {companyIdCol} = @company AND lower(FirstName) = lower(@firstName) AND lower(LastName) = lower(@lastName) LIMIT 1"
                : $"SELECT TOP 1 {idCol} FROM {usersTable} WHERE {companyIdCol} = @company AND LOWER(FirstName) = LOWER(@firstName) AND LOWER(LastName) = LOWER(@lastName)";
            AddParameter(find, "@company", companyOrdinal);
            AddParameter(find, "@firstName", firstName);
            AddParameter(find, "@lastName", lastName);
            var result = await find.ExecuteScalarAsync();
            if (result != null && result is not DBNull)
            {
                dUsersId = Convert.ToInt32(result);
            }
        }

        if (dUsersId.HasValue)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = $"UPDATE {usersTable} SET FirstName=@firstName, LastName=@lastName, Image=@pic, Country=@country, Phone=@phone, {companyIdCol}=@company WHERE {idCol}=@id";
            AddParameter(update, "@firstName", firstName);
            AddParameter(update, "@lastName", lastName);
            AddParameter(update, "@pic", profilePic);
            AddParameter(update, "@country", country);
            AddParameter(update, "@phone", numericPhone);
            AddParameter(update, "@company", companyOrdinal);
            AddParameter(update, "@id", dUsersId.Value);
            await update.ExecuteNonQueryAsync();
        }
        else
        {
            var nextDUsersId = await GetNextIdAsync(connection, usersTable, isSqlite);
            await using var insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO {usersTable} ({dguidCol}, {idCol}, FirstName, LastName, Image, Country, Phone, {companyIdCol}) VALUES (@dguid, @id, @firstName, @lastName, @pic, @country, @phone, @company)";
            AddParameter(insert, "@dguid", Guid.NewGuid().ToString().ToUpperInvariant());
            AddParameter(insert, "@id", nextDUsersId);
            AddParameter(insert, "@firstName", firstName);
            AddParameter(insert, "@lastName", lastName);
            AddParameter(insert, "@pic", profilePic);
            AddParameter(insert, "@country", country);
            AddParameter(insert, "@phone", numericPhone);
            AddParameter(insert, "@company", companyOrdinal);
            await insert.ExecuteNonQueryAsync();
        }

        var ownerUserId = await ResolveCompanyOwnerUserIdAsync(db, company.Id, user.Id);
        await ToolDataSyncService.SyncTableForOwnerAsync(db, ownerUserId, "DUsersCompany", "DUsersCompany");
        await ToolDataSyncService.SyncTableForOwnerAsync(db, ownerUserId, "DUsers", "DUsers");
    }

    public static async Task<int> GetCompanyOrdinalIdAsync(ApplicationDbContext db, Guid companyId)
    {
        var orderedCompanyIds = await db.CompanyProfiles
            .AsNoTracking()
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync();

        var index = orderedCompanyIds.FindIndex(id => id == companyId);
        return index >= 0 ? index + 1 : 1;
    }

    private static async Task EnsureDirectoryTablesAsync(ApplicationDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS DUsersCompany (
    DGUID TEXT NOT NULL DEFAULT (lower(hex(randomblob(16)))),
    ID INTEGER NOT NULL,
    FullName NVARCHAR(500) NOT NULL,
    CompanyID INTEGER NOT NULL,
    ProfilePic TEXT NOT NULL,
    Phone NUMERIC(18,0) NOT NULL,
    Position NVARCHAR(500) NOT NULL
);");
            await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS DUsers (
    DGUID TEXT NOT NULL DEFAULT (lower(hex(randomblob(16)))),
    ID INTEGER NOT NULL,
    FirstName NVARCHAR(500) NOT NULL,
    LastName NVARCHAR(500) NOT NULL,
    Image TEXT NOT NULL,
    Country NVARCHAR(500) NOT NULL,
    Phone NUMERIC(18,0) NOT NULL,
    CompanyID INTEGER NOT NULL DEFAULT 0
);");

            var hasCompanyId = await ColumnExistsAsync(db, "DUsers", "CompanyID");
            if (!hasCompanyId)
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"DUsers\" ADD COLUMN \"CompanyID\" INTEGER NOT NULL DEFAULT 0;");
            }
            return;
        }

        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DUsersCompany' AND xtype='U')
CREATE TABLE DUsersCompany (
    DGUID UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ID INT NOT NULL,
    FullName NVARCHAR(500) NOT NULL,
    CompanyID INT NOT NULL,
    ProfilePic NVARCHAR(MAX) NOT NULL,
    Phone NUMERIC(18,0) NOT NULL,
    Position NVARCHAR(500) NOT NULL
);");
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DUsers' AND xtype='U')
CREATE TABLE DUsers (
    DGUID UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ID INT NOT NULL,
    FirstName NVARCHAR(500) NOT NULL,
    LastName NVARCHAR(500) NOT NULL,
    Image NVARCHAR(MAX) NOT NULL,
    Country NVARCHAR(500) NOT NULL,
    Phone NUMERIC(18,0) NOT NULL,
    CompanyID INT NOT NULL DEFAULT 0
);");
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('DUsers', 'CompanyID') IS NULL
ALTER TABLE DUsers ADD CompanyID INT NOT NULL DEFAULT 0;");
    }

    private static async Task<bool> ColumnExistsAsync(ApplicationDbContext db, string tableName, string columnName)
    {
        await using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = db.Database.ProviderName ?? string.Empty;
        await using var command = connection.CreateCommand();
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND COLUMN_NAME = @column";
        AddParameter(command, "@table", tableName);
        AddParameter(command, "@column", columnName);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<int> GetNextIdAsync(DbConnection connection, string quotedTableName, bool isSqlite)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MAX({Quote("ID", isSqlite)}) FROM {quotedTableName}";
        var result = await command.ExecuteScalarAsync();
        return result == null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;
    }

    private static string ResolveFullName(ApplicationUser user, string? fullName)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName.Trim();
        }

        var fromUser = user.GetFriendlyName();
        if (!string.IsNullOrWhiteSpace(fromUser))
        {
            return fromUser.Trim();
        }

        return user.Email?.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Bugence User";
    }

    private static long ParsePhone(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            return 0;
        }

        var digits = new string(rawPhone.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var value) ? value : 0;
    }

    private static string Quote(string identifier, bool isSqlite) => isSqlite ? $"\"{identifier}\"" : $"[{identifier}]";

    private static async Task<string> ResolveCompanyOwnerUserIdAsync(ApplicationDbContext db, Guid companyId, string fallbackUserId)
    {
        var companyUsers = await db.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId)
            .OrderByDescending(u => u.IsCompanyAdmin)
            .ThenBy(u => string.Equals(u.Email, "admin@bugence.com", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(u => u.Email)
            .Select(u => u.Id)
            .ToListAsync();

        return companyUsers.FirstOrDefault() ?? fallbackUserId;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
