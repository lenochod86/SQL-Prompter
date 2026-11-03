using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class MetadataCacheTests
    {
        [Fact]
        public async Task LoadsInBackgroundAndThenServesSnapshot()
        {
            var loader = new FakeLoader();
            var cache = new DatabaseMetadataCache();

            Assert.Null(cache.Current);
            await cache.EnsureLoaded(loader);

            Assert.NotNull(cache.Current);
            await cache.EnsureLoaded(loader);
            Assert.Equal(1, loader.Loads);
        }

        [Fact]
        public async Task LoadsColumnsOnDemandIntoNewSnapshot()
        {
            var loader = new FakeLoader();
            var cache = new DatabaseMetadataCache();
            await cache.EnsureLoaded(loader);
            var before = cache.Current;
            var users = before.FindObject(null, "Users");

            await cache.RequestColumns(new[] { users }, loader);

            Assert.Null(before.GetColumns(users));
            Assert.Equal(3, cache.Current.GetColumns(users).Count);
            await cache.RequestColumns(new[] { users }, loader);
            Assert.Equal(1, loader.ColumnLoads);
        }

        [Fact]
        public async Task MissingObjectGetsEmptyColumns()
        {
            var loader = new FakeLoader();
            var cache = new DatabaseMetadataCache();
            await cache.EnsureLoaded(loader);
            var ghost = new DbObject(999, "dbo", "Ghost", DbObjectKind.Table, false);

            await cache.RequestColumns(new[] { ghost }, loader);

            Assert.Empty(cache.Current.GetColumns(ghost));
        }

        [Fact]
        public async Task FailureIsRetriedOnlyAfterDelay()
        {
            var now = new DateTime(2026, 1, 1);
            var loader = new FakeLoader { Fail = true };
            var cache = new DatabaseMetadataCache(clock: () => now);

            await cache.EnsureLoaded(loader);
            await cache.EnsureLoaded(loader);
            Assert.Equal(1, loader.Loads);

            now += TimeSpan.FromMinutes(1);
            loader.Fail = false;
            await cache.EnsureLoaded(loader);
            Assert.Equal(2, loader.Loads);
            Assert.NotNull(cache.Current);
        }

        [Fact]
        public async Task ReloadsWhenVersionChanges()
        {
            var now = new DateTime(2026, 1, 1);
            var loader = new FakeLoader();
            var cache = new DatabaseMetadataCache(clock: () => now);
            await cache.EnsureLoaded(loader);

            await cache.CheckForChanges(loader);
            Assert.Equal(0, loader.VersionChecks);

            now += TimeSpan.FromMinutes(1);
            await cache.CheckForChanges(loader);
            Assert.Equal(1, loader.VersionChecks);
            Assert.Equal(1, loader.Loads);

            loader.Version = "v2";
            now += TimeSpan.FromMinutes(1);
            await cache.CheckForChanges(loader);
            Assert.Equal(2, loader.Loads);
            Assert.Equal("v2", cache.Current.Version);
        }

        [Fact]
        public async Task ForcedCheckIgnoresInterval()
        {
            var now = new DateTime(2026, 1, 1);
            var loader = new FakeLoader();
            var cache = new DatabaseMetadataCache(clock: () => now);
            await cache.EnsureLoaded(loader);

            loader.Version = "v2";
            await cache.CheckForChanges(loader, force: true);

            Assert.Equal(1, loader.VersionChecks);
            Assert.Equal("v2", cache.Current.Version);
            await cache.CheckForChanges(loader);
            Assert.Equal(1, loader.VersionChecks);
        }

        [Fact]
        public async Task InvalidateDropsSnapshotAndIgnoresStaleLoad()
        {
            var loader = new FakeLoader { Gate = new TaskCompletionSource<bool>() };
            var cache = new DatabaseMetadataCache();
            var pending = cache.EnsureLoaded(loader);

            cache.Invalidate();
            loader.Gate.SetResult(true);
            await pending;

            Assert.Null(cache.Current);
        }

        [Fact]
        public void CacheKeyDoesNotContainPassword()
        {
            var settings = new ConnectionSettings("srv", "db", AuthenticationKind.SqlPassword, "sa", "Secret123", "False", "True");

            Assert.DoesNotContain("secret", settings.CacheKey);
            Assert.DoesNotContain("Secret", settings.ToString());
        }

        [Fact]
        public void ConnectionStringCarriesWindowSettings()
        {
            var settings = new ConnectionSettings("srv,1433", "test", AuthenticationKind.SqlPassword, "sa", "pw", "False", "True");

            var connectionString = settings.BuildConnectionString("SQL Prompter");

            Assert.Contains("initial catalog=test", connectionString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("encrypt=False", connectionString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("trust server certificate=True", connectionString, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("nvarchar", 100, 0, 0, "nvarchar(50)")]
        [InlineData("nvarchar", -1, 0, 0, "nvarchar(max)")]
        [InlineData("varchar", 20, 0, 0, "varchar(20)")]
        [InlineData("decimal", 9, 10, 2, "decimal(10,2)")]
        [InlineData("datetime2", 8, 27, 7, "datetime2(7)")]
        [InlineData("int", 4, 10, 0, "int")]
        public void FormatsTypes(string type, short maxLength, byte precision, byte scale, string expected)
        {
            Assert.Equal(expected, SqlMetadataLoader.FormatType(type, maxLength, precision, scale));
        }

        private sealed class FakeLoader : IMetadataLoader
        {
            public int Loads;
            public int ColumnLoads;
            public int VersionChecks;
            public bool Fail;
            public string Version = "v1";
            public TaskCompletionSource<bool> Gate;

            public async Task<DatabaseSnapshot> LoadAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Loads);
                if (Gate != null)
                {
                    await Gate.Task;
                }

                if (Fail)
                {
                    throw new InvalidOperationException("db down");
                }

                var sample = SampleDatabase(withColumns: false);
                return new DatabaseSnapshot(sample.DatabaseName, sample.DefaultSchema, sample.Schemas, sample.Objects, Version);
            }

            public Task<IReadOnlyDictionary<int, IReadOnlyList<DbColumn>>> LoadColumnsAsync(IReadOnlyCollection<int> objectIds, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref ColumnLoads);
                var full = SampleDatabase();
                IReadOnlyDictionary<int, IReadOnlyList<DbColumn>> result = full.Objects
                    .Where(o => objectIds.Contains(o.Id))
                    .ToDictionary(o => o.Id, o => full.GetColumns(o));
                return Task.FromResult(result);
            }

            public Task<string> GetVersionAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref VersionChecks);
                return Task.FromResult(Version);
            }
        }
    }
}
