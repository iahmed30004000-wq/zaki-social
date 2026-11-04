using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ZakiSocial.Core.Tenancy;

namespace ZakiSocial.Infrastructure.Tenancy;

/// <summary>
/// كل ما يفتح اتصال بقاعدة البيانات: يحوّل لدور zaki_app (خاضع لعزل الصفوف) ويحدد المساحة الحالية.
/// وضع النظام بس بيبقى بالدور الرئيسي. Npgsql بيرجّع حالة الجلسة لما يرجّع الاتصال للمجمّع.
/// </summary>
public sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    private const string Sql = "SET ROLE zaki_app; SELECT set_config('app.workspace_id', @w, false);";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (tenant.IsSystem) return;
        using var command = Build(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (tenant.IsSystem) return;
        await using var command = Build(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand Build(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = Sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "w";
        parameter.Value = tenant.WorkspaceId?.ToString() ?? "";
        command.Parameters.Add(parameter);
        return command;
    }
}
