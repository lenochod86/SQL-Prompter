using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlPrompter.Core.Analysis;
using SqlPrompter.Core.Completion;
using SqlPrompter.Core.Model;
using SqlPrompter.Metadata;
using Xunit;
using static SqlPrompter.Tests.TestSupport;

namespace SqlPrompter.Tests
{
    public class LinkedServerTests
    {
        private static DatabaseSnapshot Current() =>
            new DatabaseSnapshot("test", "dbo", new[] { "dbo", "sales" },
                new[] { new DbObject(1, "dbo", "Users", DbObjectKind.Table, false) },
                "v1", null, new[] { "master", "Archive" }, new[] { "Remote1", "sales", "Archive" });

        private static RemoteSnapshot RemoteServer() => RemoteSnapshot.OfServer(new[] { "Crm", "Billing" });

        private static RemoteSnapshot RemoteDb() => RemoteSnapshot.OfDatabase("Crm", new[]
        {
            new RemoteObject("dbo", "Customers", DbObjectKind.Table, false),
            new RemoteObject("dbo", "ActiveCustomers", DbObjectKind.View, false),
            new RemoteObject("sales", "Orders", DbObjectKind.Table, false),
            new RemoteObject("sys", "objects", DbObjectKind.View, true),
        });

        [Fact]
        public void LinkedServersAreOfferedAmongTheNamesOfTheDatabase()
        {
            var items = SuggestionBuilder.Build(Analyze("SELECT * FROM |"), Current());

            var server = items.Single(i => i.Kind == SuggestionKind.LinkedServer && i.DisplayText == "Remote1");
            Assert.Equal("linked server", server.Description);
        }

        [Theory]
        [InlineData("SELECT * FROM Remote1.|", null, 0)]
        [InlineData("SELECT * FROM Remote1.Crm.|", "Crm", 0)]
        [InlineData("SELECT * FROM Remote1.Crm.dbo.|", "Crm", 1)]
        [InlineData("SELECT * FROM Remote1.Crm..|", "Crm", 1)]
        public void QualifierIsSplitIntoServerDatabaseAndTheRest(string sql, string expectedDatabase, int remaining)
        {
            var context = Analyze(sql);

            Assert.True(LinkedServerName.TryResolve(context, Current(), out var server, out var database, out var inside));
            Assert.Equal("Remote1", server);
            Assert.Equal(expectedDatabase, database);
            Assert.Equal(remaining, inside.Qualifier.Count);
        }

        [Fact]
        public void ASchemaOrDatabaseOfTheCurrentDatabaseWinsOverALinkedServer()
        {
            // "sales" is a schema here and a linked server too; "Archive" is a database and a linked server.
            Assert.False(LinkedServerName.TryResolve(Analyze("SELECT * FROM sales.|"), Current(), out _, out _, out _));
            Assert.False(LinkedServerName.TryResolve(Analyze("SELECT * FROM Archive.|"), Current(), out _, out _, out _));
            Assert.False(LinkedServerName.TryResolve(Analyze("SELECT * FROM Nope.|"), Current(), out _, out _, out _));
            Assert.False(LinkedServerName.TryResolve(Analyze("SELECT * FROM |"), Current(), out _, out _, out _));
        }

        [Fact]
        public void ServerNameOffersItsDatabases()
        {
            Assert.True(LinkedServerName.TryResolve(Analyze("SELECT * FROM Remote1.|"), Current(), out _, out _, out var inside));

            var items = SuggestionBuilder.Build(inside, RemoteServer());

            Assert.Equal(new[] { "Billing", "Crm" }, items.Where(i => i.Kind == SuggestionKind.Database).Select(i => i.DisplayText).OrderBy(x => x).ToArray());
            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Keyword);
        }

        [Fact]
        public void DatabaseNameOffersSchemasAndObjects()
        {
            Assert.True(LinkedServerName.TryResolve(Analyze("SELECT * FROM Remote1.Crm.|"), Current(), out _, out _, out var inside));

            var items = SuggestionBuilder.Build(inside, RemoteDb());

            Assert.Contains(items, i => i.Kind == SuggestionKind.Table && i.InsertText == "dbo.Customers");
            Assert.Contains(items, i => i.Kind == SuggestionKind.View && i.InsertText == "dbo.ActiveCustomers");
            Assert.Contains(items, i => i.Kind == SuggestionKind.Schema && i.DisplayText == "sales");
            Assert.DoesNotContain(items, i => i.DisplayText == "objects");
            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Database);
        }

        [Fact]
        public void SchemaAndDefaultSchemaOfTheRemoteDatabase()
        {
            Assert.True(LinkedServerName.TryResolve(Analyze("SELECT * FROM Remote1.Crm.sales.|"), Current(), out _, out _, out var inSchema));
            Assert.True(LinkedServerName.TryResolve(Analyze("SELECT * FROM Remote1.Crm..|"), Current(), out _, out _, out var inDefault));

            Assert.Equal(new[] { "Orders" }, SuggestionBuilder.Build(inSchema, RemoteDb()).Select(i => i.InsertText).ToArray());
            Assert.Equal(new[] { "ActiveCustomers", "Customers" }, SuggestionBuilder.Build(inDefault, RemoteDb()).Select(i => i.InsertText).OrderBy(x => x).ToArray());
        }

        [Fact]
        public void RemoteColumnsAreNotKnown()
        {
            var db = RemoteDb();

            Assert.Null(db.GetColumns(db.FindObject("dbo", "Customers")));
        }

        [Fact]
        public async Task NothingIsLoadedUntilAskedAndNeverTwice()
        {
            var loader = new FakeLinked();
            var cache = new LinkedServerCache();

            Assert.Equal(0, loader.Calls);
            Assert.Null(cache.GetServer("Remote1", loader));
            await loader.WaitAsync();
            await Task.Delay(100);
            var again = cache.GetServer("Remote1", loader);
            var third = cache.GetServer("remote1", loader);

            Assert.NotNull(again);
            Assert.Same(again, third);
            Assert.Equal(new[] { "Billing", "Crm" }, again.Databases.OrderBy(x => x).ToArray());
            Assert.Equal(1, loader.Calls);
        }

        [Fact]
        public async Task ObjectsAreLoadedPerDatabase()
        {
            var loader = new FakeLinked();
            var cache = new LinkedServerCache();

            Assert.Null(cache.GetDatabase("Remote1", "Crm", loader));
            await loader.WaitAsync();
            await Task.Delay(100);

            var db = cache.GetDatabase("Remote1", "Crm", loader);
            Assert.Equal("Crm", db.DatabaseName);
            Assert.Contains(db.Objects, o => o.Name == "Customers");
            Assert.Null(cache.GetDatabase("Remote1", "Billing", loader));
        }

        [Fact]
        public async Task AFailureIsRememberedAndRetriedLater()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var loader = new FakeLinked { Fail = true };
            var cache = new LinkedServerCache(clock: () => now) { RetryDelay = TimeSpan.FromSeconds(60) };

            Assert.Null(cache.GetServer("Remote1", loader));
            await loader.WaitAsync();
            await Task.Delay(150);
            Assert.Null(cache.GetServer("Remote1", loader));
            Assert.Equal(1, loader.Calls);

            now = now.AddSeconds(61);
            loader.Fail = false;
            Assert.Null(cache.GetServer("Remote1", loader));
            await loader.WaitAsync();
            await Task.Delay(150);
            Assert.NotNull(cache.GetServer("Remote1", loader));
            Assert.Equal(2, loader.Calls);
        }

        [Fact]
        public async Task ColumnsAreLoadedWhenATableIsUsed()
        {
            var loader = new FakeLinked();
            var cache = new LinkedServerCache();

            Assert.Null(cache.GetColumns("Remote1", "Crm", "dbo", "Customers", loader));
            await loader.WaitAsync();
            await Task.Delay(100);

            Assert.Equal(new[] { "IdCustomer", "Name" }, cache.GetColumns("Remote1", "Crm", "dbo", "Customers", loader).Select(c => c.Name).ToArray());
            Assert.Null(cache.GetColumns("Remote1", "Crm", "dbo", "Orders", loader));
        }

        [Fact]
        public void ColumnsOfAFourPartSourceComeFromTheLinkedServer()
        {
            var calls = new List<string>();
            Func<TableSource, IReadOnlyList<DbColumn>> remote = s =>
            {
                calls.Add(string.Join(".", s.NameParts));
                return Columns("IdCustomer", "Name");
            };
            var options = new SqlPrompter.Core.Completion.SuggestionOptions(SqlPrompter.Core.Settings.KeywordCase.MatchTyped, null, remote);

            var items = SuggestionBuilder.Build(Analyze("SELECT c.| FROM Remote1.Crm.dbo.Customers c"), Current(), null, options);

            Assert.Equal(new[] { "IdCustomer", "Name" }, items.Where(i => i.Kind == SuggestionKind.Column).Select(i => i.DisplayText).ToArray());
            Assert.Contains("Remote1.Crm.dbo.Customers", calls);
        }

        [Fact]
        public void NothingIsOfferedWhileTheRemoteColumnsLoad()
        {
            var options = new SqlPrompter.Core.Completion.SuggestionOptions(SqlPrompter.Core.Settings.KeywordCase.MatchTyped, null, s => null);

            var items = SuggestionBuilder.Build(Analyze("SELECT c.| FROM Remote1.Crm.dbo.Customers c"), Current(), null, options);

            Assert.DoesNotContain(items, i => i.Kind == SuggestionKind.Column);
        }

        [Theory]
        [InlineData("nvarchar", 50, 0, "nvarchar(50)")]
        [InlineData("varchar", 0, 0, "varchar(max)")]
        [InlineData("decimal", 18, 2, "decimal(18,2)")]
        [InlineData("int", 10, 0, "int")]
        public void LinkedColumnTypesGetTheirLength(string type, int size, int scale, string expected)
        {
            Assert.Equal(expected, SqlMetadataLoader.FormatLinkedType(type, size, scale));
        }

        [Fact]
        public async Task ASlowServerDoesNotBlockAndTimesOut()
        {
            var loader = new FakeLinked { Hang = true };
            // Independent of the speed of the machine: the timeout is long and the call must return well within it; the cancellation is awaited.
            var cache = new LinkedServerCache { LoadTimeout = TimeSpan.FromSeconds(2) };
            var watch = System.Diagnostics.Stopwatch.StartNew();

            Assert.Null(cache.GetServer("Remote1", loader));

            Assert.True(watch.ElapsedMilliseconds < 1000, "the call must return without waiting for the server");
            await loader.WaitAsync();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!loader.Cancelled && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.True(loader.Cancelled);
        }

        private sealed class FakeLinked : IMetadataLoader
        {
            private readonly SemaphoreSlim called = new SemaphoreSlim(0);
            public int Calls;
            public bool Fail;
            public bool Hang;
            public bool Cancelled;

            public Task WaitAsync() => called.WaitAsync(TimeSpan.FromSeconds(5));

            public async Task<IReadOnlyList<string>> LoadLinkedDatabasesAsync(string linkedServer, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                called.Release();
                await Behave(cancellationToken);
                return new[] { "Crm", "Billing" };
            }

            public async Task<IReadOnlyList<RemoteObject>> LoadLinkedObjectsAsync(string linkedServer, string database, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                called.Release();
                await Behave(cancellationToken);
                return new[] { new RemoteObject("dbo", "Customers", DbObjectKind.Table, false) };
            }

            public async Task<IReadOnlyList<DbColumn>> LoadLinkedColumnsAsync(string linkedServer, string database, string schema, string table, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                called.Release();
                await Behave(cancellationToken);
                return Columns("IdCustomer", "Name");
            }

            private async Task Behave(CancellationToken cancellationToken)
            {
                if (Hang)
                {
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Cancelled = true;
                        throw;
                    }
                }

                if (Fail)
                {
                    throw new InvalidOperationException("linked server down");
                }
            }

            public Task<DatabaseSnapshot> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<IReadOnlyDictionary<int, IReadOnlyList<DbColumn>>> LoadColumnsAsync(IReadOnlyCollection<int> objectIds, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<string> GetVersionAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        }
    }
}
