using System;
using System.Collections.Generic;
using System.IO;
using SodRpg.Core;
using SodRpg.Core.Game;

internal static class FixtureGenerator
{
    private const string TradeId = "10280000000000000000000000000001";

    private static Profile Player(ulong seed, string uid, int shards)
    {
        var profile = Profile.CreateNew(seed);
        profile.Hero("default");
        var relic = Loot.RollRelic(new Rng(seed), Rarity.Common, 1);
        relic.Uid = uid;
        profile.Stash.Add(relic);
        profile.AddMaterial(Materials.Shard, shards);
        profile.Run = new RunState { RunId = "run", HeroKey = "default", SatchelShards = 9, SatchelTuning = 3 };
        profile.ContinueCheckpoints.Add(RunCheckpoint.Capture(profile, "native-continue"));
        return profile;
    }

    public static void Main(string[] args)
    {
        if (Profile.CurrentVersion != 7) throw new InvalidOperationException("Expected historical format 7 sources.");
        string output = args[0];
        Directory.CreateDirectory(output);
        var first = Player(17, "first-relic", 100);
        var second = Player(19, "second-relic", 50);
        var receipt = CoopTradeRules.CreateReceipt(TradeId, "host-key", "player-a", first,
            new CoopTradeOffer { RelicUids = new List<string> { "first-relic" }, Shards = 20 },
            "player-b", second, new CoopTradeOffer { RelicUids = new List<string> { "second-relic" }, Shards = 7 });
        CoopTradeRules.Prepare(first, TradeId, receipt.HostKey, receipt.FirstOffer);
        CoopTradeRules.Prepare(second, TradeId, receipt.HostKey, receipt.SecondOffer);
        File.WriteAllText(Path.Combine(output, "v7-first-escrow.json"), ProfileCodec.Write(first));
        File.WriteAllText(Path.Combine(output, "v7-second-escrow.json"), ProfileCodec.Write(second));
        string journalPath = Path.Combine(output, "v7-journal.json");
        if (File.Exists(journalPath)) throw new InvalidOperationException("Generate into a fresh output directory.");
        var journal = new CoopTradeJournal(new RealFileSystem(), journalPath);
        journal.Commit(receipt);
        journal = new CoopTradeJournal(new RealFileSystem(), journalPath);
        if (!journal.TryGet(TradeId, out var saved)) throw new InvalidOperationException("Historical query failed.");
        if (!CoopTradeRules.Resolve(first, saved, "player-a") || !CoopTradeRules.Resolve(second, saved, "player-b"))
            throw new InvalidOperationException("Historical resolution failed.");
        if (first.Material(Materials.Shard) != 87 || second.Material(Materials.Shard) != 63
            || first.CoopTradePending != null || second.CoopTradePending != null)
            throw new InvalidOperationException("Historical asset accounting failed.");
        File.WriteAllText(Path.Combine(output, "v7-first-completed.json"), ProfileCodec.Write(first));
        File.WriteAllText(Path.Combine(output, "v7-second-completed.json"), ProfileCodec.Write(second));
        if (CoopTradeRules.Resolve(first, saved, "player-a") || CoopTradeRules.Resolve(second, saved, "player-b"))
            throw new InvalidOperationException("Historical resolution was not idempotent.");
        Console.WriteLine("Format 7: journal reopened; both escrows resolved once; completed balances 87/63; reservations cleared.");
    }
}
