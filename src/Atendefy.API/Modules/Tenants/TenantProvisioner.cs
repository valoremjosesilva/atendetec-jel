using Npgsql;

namespace Atendefy.API.Modules.Tenants;

public class TenantProvisioner(string connectionString) : ITenantProvisioner
{
    public async Task ProvisionSchemaAsync(string schemaName)
    {
        // schemaName comes from Tenant.SchemaName = $"tenant_{Id:N}" — derived from GUID, never from user input
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var sql = $"""
            CREATE SCHEMA IF NOT EXISTS "{schemaName}";

            CREATE TABLE IF NOT EXISTS "{schemaName}".whatsapp_accounts (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                provider VARCHAR(50) NOT NULL,
                phone VARCHAR(20),
                config_json JSONB,
                status VARCHAR(50) DEFAULT 'disconnected',
                created_at TIMESTAMPTZ DEFAULT NOW(),
                updated_at TIMESTAMPTZ,
                is_deleted BOOLEAN DEFAULT FALSE
            );

            CREATE TABLE IF NOT EXISTS "{schemaName}".ai_configs (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                provider VARCHAR(50) NOT NULL,
                api_key_encrypted TEXT,
                model VARCHAR(100),
                system_prompt TEXT,
                created_at TIMESTAMPTZ DEFAULT NOW(),
                updated_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS "{schemaName}".conversations (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                contact_phone VARCHAR(30) NOT NULL,
                started_at TIMESTAMPTZ DEFAULT NOW(),
                message_count INT DEFAULT 0,
                is_deleted BOOLEAN DEFAULT FALSE
            );

            CREATE TABLE IF NOT EXISTS "{schemaName}".messages (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                conversation_id UUID REFERENCES "{schemaName}".conversations(id),
                role VARCHAR(20) NOT NULL,
                content TEXT NOT NULL,
                tokens_used INT DEFAULT 0,
                created_at TIMESTAMPTZ DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS "{schemaName}".usage_counters (
                month VARCHAR(7) PRIMARY KEY,
                messages_sent INT DEFAULT 0,
                tokens_consumed BIGINT DEFAULT 0,
                cost_usd DECIMAL(10,4) DEFAULT 0
            );
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
