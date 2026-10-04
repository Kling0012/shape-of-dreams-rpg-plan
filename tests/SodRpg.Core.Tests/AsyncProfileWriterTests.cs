using System.IO;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class AsyncProfileWriterTests
    {
        private const string SavePath = "writer/profile.json";

        [Fact]
        public void Failed_write_is_reported_and_a_reenqueued_retry_succeeds()
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var store = new ProfileStore(fs, SavePath, 5);
            var profile = store.Load();
            profile.AddMaterial(Materials.Shard, 9);
            var writer = new AsyncProfileWriter(store);
            Assert.False(writer.HasPendingFailure);

            fs.Arm(0, FaultMode.IoError); // the temp-file write fails
            writer.Enqueue(profile);
            Assert.False(writer.WaitForRevision(profile.Revision, 5000));
            Assert.True(writer.HasPendingFailure);
            Assert.NotNull(writer.LastError);

            // The failed text is discarded; progress survives only if the caller enqueues it again
            // (the periodic save does this while a failure is pending).
            fs.Disarm();
            writer.Enqueue(profile);
            Assert.True(writer.WaitForRevision(profile.Revision, 5000));
            Assert.False(writer.HasPendingFailure);
            Assert.Null(writer.LastError);
            Assert.True(writer.Flush());
            Assert.Equal(9, new ProfileStore(disk, SavePath, 6).Load().Material(Materials.Shard));
        }

        [Fact]
        public void Later_success_clears_an_earlier_failure()
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var store = new ProfileStore(fs, SavePath, 5);
            var profile = store.Load();
            var writer = new AsyncProfileWriter(store);
            fs.Arm(1, FaultMode.IoError); // the replace fails: the write never committed
            writer.Enqueue(profile);
            Assert.False(writer.WaitForRevision(profile.Revision, 5000));
            Assert.True(writer.HasPendingFailure);

            fs.Disarm();
            writer.Enqueue(profile);
            Assert.True(writer.WaitForRevision(profile.Revision, 5000));
            Assert.False(writer.HasPendingFailure);
            Assert.Null(writer.LastError);
            Assert.Equal(profile.Revision, writer.WrittenRevision);
        }
    }
}
