using Npgsql;

namespace SinalFrio.Worker;

internal sealed class OutboxStore(NpgsqlDataSource database) : IAsyncDisposable
{
    internal sealed record Entry(Guid Id, string Payload, DateTime OccurredAt);

    internal async Task<List<Entry>> ReadAsync(CancellationToken ct)
    {
        await using var command = database.CreateCommand("""
            SELECT id, payload::text, occurred_at
            FROM public.sf_outbox WHERE published_at IS NULL
            ORDER BY occurred_at, id LIMIT 32
            """);
        command.CommandTimeout = 5;
        await using var rows = await command.ExecuteReaderAsync(ct);
        var batch = new List<Entry>();
        while (await rows.ReadAsync(ct))
            batch.Add(new(rows.GetGuid(0), rows.GetString(1), rows.GetDateTime(2)));
        return batch;
    }

    internal async Task MarkPublishedAsync(Guid id, CancellationToken ct)
    {
        await using var command = database.CreateCommand("""
            UPDATE public.sf_outbox SET published_at = clock_timestamp()
            WHERE id = $1 AND published_at IS NULL
            """);
        command.CommandTimeout = 5;
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });
        await command.ExecuteNonQueryAsync(ct);
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
