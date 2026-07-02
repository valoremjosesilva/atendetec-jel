using Npgsql;
using Serilog;
using System.Security.Cryptography;
using System.Text;

namespace Atendefy.API.Infrastructure.Database;

// Aplica o patch de schema per-tenant no startup, pulando quem já está em dia.
// O SHA-256 do PatchSqlTemplate fica registrado em public.tenant_schema_patches;
// no caso comum (nada mudou) o boot faz 1 query e zero DDL, independente do número
// de tenants. O DDL roda só para tenants novos ou quando o template muda — e aí o
// hash novo reaplica o patch em todos no próximo boot.
public class TenantSchemaMigrator(string connectionString)
{
    // DDL idempotente que traz schemas de tenants antigos para a estrutura atual.
    // {0} = schema name (vem de public.tenants, nunca de fonte externa).
    // Ao alterar este template, o hash muda e o patch reaplica em todos os tenants.
    private const string PatchSqlTemplate = """
        ALTER TABLE IF EXISTS "{0}".conversations
            ADD COLUMN IF NOT EXISTS bot_paused BOOLEAN DEFAULT FALSE,
            ADD COLUMN IF NOT EXISTS account_id UUID,
            ADD COLUMN IF NOT EXISTS is_resolved BOOLEAN DEFAULT FALSE,
            ADD COLUMN IF NOT EXISTS resolved_at TIMESTAMPTZ;
        ALTER TABLE IF EXISTS "{0}".calendar_configs
            ADD COLUMN IF NOT EXISTS api_base_url TEXT,
            ADD COLUMN IF NOT EXISTS tenant_slug TEXT,
            ADD COLUMN IF NOT EXISTS api_key_encrypted TEXT,
            ADD COLUMN IF NOT EXISTS default_service_id UUID,
            ADD COLUMN IF NOT EXISTS default_resource_id UUID,
            ADD COLUMN IF NOT EXISTS webhook_secret_encrypted TEXT;
        CREATE TABLE IF NOT EXISTS "{0}".contacts (
            phone VARCHAR(30) PRIMARY KEY,
            name VARCHAR(200),
            created_at TIMESTAMPTZ DEFAULT NOW()
        );
        CREATE TABLE IF NOT EXISTS "{0}".quick_replies (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            title VARCHAR(100) NOT NULL,
            body TEXT NOT NULL,
            created_at TIMESTAMPTZ DEFAULT NOW()
        );
        """;

    public static string CurrentHash { get; } =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PatchSqlTemplate)));

    public async Task RunAsync(IReadOnlyCollection<string> schemaNames, CancellationToken ct = default)
    {
        if (schemaNames.Count == 0) return;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // Tabela de controle — criada pelo próprio migrator, sem migração EF.
        await using (var create = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS public.tenant_schema_patches (
                schema_name TEXT PRIMARY KEY,
                sql_hash    TEXT NOT NULL,
                applied_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """, conn))
        {
            await create.ExecuteNonQueryAsync(ct);
        }

        // Schemas que já receberam exatamente este patch.
        var upToDate = new HashSet<string>();
        await using (var query = new NpgsqlCommand(
            "SELECT schema_name FROM public.tenant_schema_patches WHERE sql_hash = @hash", conn))
        {
            query.Parameters.AddWithValue("hash", CurrentHash);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                upToDate.Add(reader.GetString(0));
        }

        // Schemas que existem de fato no banco — tenants pendentes de aprovação ainda
        // não foram provisionados; sem este filtro o patch falharia (e logaria) por boot.
        var existing = new HashSet<string>();
        await using (var query = new NpgsqlCommand(
            "SELECT schema_name FROM information_schema.schemata WHERE schema_name LIKE 'tenant\\_%'", conn))
        {
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                existing.Add(reader.GetString(0));
        }

        var pending = schemaNames
            .Where(s => existing.Contains(s) && !upToDate.Contains(s))
            .ToList();

        if (pending.Count == 0)
        {
            Log.Information("Schemas de tenant em dia ({Count} tenants, patch {Hash})",
                schemaNames.Count, CurrentHash[..8]);
            return;
        }

        var applied = 0;
        foreach (var schema in pending)
        {
            try
            {
                await using var cmd = new NpgsqlCommand(string.Format(PatchSqlTemplate, schema), conn);
                await cmd.ExecuteNonQueryAsync(ct);

                await using var upsert = new NpgsqlCommand("""
                    INSERT INTO public.tenant_schema_patches (schema_name, sql_hash, applied_at)
                    VALUES (@schema, @hash, NOW())
                    ON CONFLICT (schema_name)
                    DO UPDATE SET sql_hash = EXCLUDED.sql_hash, applied_at = EXCLUDED.applied_at
                    """, conn);
                upsert.Parameters.AddWithValue("schema", schema);
                upsert.Parameters.AddWithValue("hash", CurrentHash);
                await upsert.ExecuteNonQueryAsync(ct);
                applied++;
            }
            catch (Exception ex)
            {
                // Não registra o hash — será retentado no próximo boot.
                Log.Error(ex, "Tenant schema patch failed for {SchemaName}", schema);
            }
        }

        Log.Information(
            "Patch de schema aplicado em {Applied}/{Pending} tenants ({UpToDate} já em dia, patch {Hash})",
            applied, pending.Count, upToDate.Count, CurrentHash[..8]);
    }
}
