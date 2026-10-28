using System.Text.Json;
using Placebook.Models;
using Placebook.Services;

namespace Placebook.Tests;

public class SyncMergeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

    private static VisitChange Change(string placeId, DateTimeOffset at, bool deleted = false, string? note = null) =>
        new("mcdonalds", placeId, deleted ? null : new Visit(new DateOnly(2026, 10, 1), note, at), at);

    [Fact]
    public void Enqueue_KeepsOnlyNewestChangePerPlace()
    {
        var pending = SyncMerge.Enqueue([Change("n1", T0)], [Change("n1", T0.AddMinutes(1), deleted: true), Change("n2", T0)]);

        Assert.Equal(2, pending.Count);
        Assert.Null(pending.Single(p => p.PlaceId == "n1").Visit);
    }

    [Fact]
    public void SelectRemote_AppliesNewerRemoteChange()
    {
        var local = new Visit(null, "alt", T0);

        var result = SyncMerge.SelectRemote([Change("n1", T0.AddMinutes(5), note: "neu")], (_, _) => local, []);

        Assert.Equal("neu", Assert.Single(result).Visit!.Note);
    }

    [Fact]
    public void SelectRemote_IgnoresOlderRemoteChange()
    {
        var local = new Visit(null, "lokal", T0.AddMinutes(5));

        Assert.Empty(SyncMerge.SelectRemote([Change("n1", T0, note: "server")], (_, _) => local, []));
    }

    [Fact]
    public void SelectRemote_DoesNotOverrideNewerPendingDeletion()
    {
        // Lokal gelöscht, aber noch nicht hochgeladen – der ältere Server-Stand darf ihn nicht zurückholen
        VisitChange[] pending = [Change("n1", T0.AddMinutes(5), deleted: true)];

        Assert.Empty(SyncMerge.SelectRemote([Change("n1", T0)], (_, _) => null, pending));
    }

    [Fact]
    public void SelectRemote_AppliesRemoteDeletion_AndSkipsDeletionOfUnknownPlace()
    {
        var local = new Visit(null, null, T0);
        var deletion = Change("n1", T0.AddMinutes(1), deleted: true);

        Assert.Single(SyncMerge.SelectRemote([deletion], (_, _) => local, []));
        Assert.Empty(SyncMerge.SelectRemote([deletion], (_, _) => null, []));
    }

    [Fact]
    public void SelectRemote_VisitFromOldVersionWithoutTimestamp_LosesToServer()
    {
        var legacy = new Visit(new DateOnly(2025, 1, 1), null);

        Assert.Single(SyncMerge.SelectRemote([Change("n1", T0)], (_, _) => legacy, []));
    }

    [Fact]
    public void PushRow_ForSyncCode_SerializesSnakeCase_WithExplicitNulls()
    {
        // PostgREST verlangt bei Mehrfach-Inserts in jeder Zeile dieselben Spalten
        var row = new VisitPushRow(Guid.Empty, null, "mcdonalds", "n1", null, null, true, T0);

        var json = JsonSerializer.Serialize(new List<VisitPushRow> { row }, PlacebookJsonContext.Default.ListVisitPushRow);

        Assert.Contains("\"sync_key\":\"00000000-0000-0000-0000-000000000000\"", json);
        Assert.DoesNotContain("user_id", json);
        Assert.Contains("\"visited_at\":null", json);
        Assert.Contains("\"note\":null", json);
        Assert.Contains("\"deleted\":true", json);
        Assert.Contains("\"updated_at\":\"2026-10-28T10:00:00+00:00\"", json);
    }

    [Fact]
    public void PushRow_ForAccount_HasUserId_AndNoSyncKey()
    {
        var userId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var row = new VisitPushRow(null, userId, "ikea", "w9", new DateOnly(2026, 9, 1), "x", false, T0);

        var json = JsonSerializer.Serialize(new List<VisitPushRow> { row }, PlacebookJsonContext.Default.ListVisitPushRow);

        Assert.Contains("\"user_id\":\"11111111-2222-3333-4444-555555555555\"", json);
        Assert.DoesNotContain("sync_key", json);
    }

    [Fact]
    public void SyncTarget_IdsDistinguishCodeAndAccount()
    {
        var id = Guid.NewGuid();

        Assert.Equal($"code:{id}", new SyncTarget.Code(id).Id);
        Assert.Equal($"user:{id}", new SyncTarget.Account(id).Id);
    }

    [Fact]
    public void PullRow_DeletedRow_BecomesDeletion()
    {
        const string json = """[{"category_id":"ikea","place_id":"w9","visited_at":"2026-09-01","note":"x","deleted":true,"updated_at":"2026-10-28T10:00:00+00:00","synced_at":"2026-10-28T10:00:01.123456+00:00"}]""";

        var row = Assert.Single(JsonSerializer.Deserialize(json, PlacebookJsonContext.Default.ListVisitPullRow)!);

        Assert.Null(row.ToChange().Visit);
        Assert.Equal(T0, row.ToChange().UpdatedAt);
    }
}
