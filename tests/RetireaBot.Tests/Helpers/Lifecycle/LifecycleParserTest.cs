using Microsoft.RetireaBot.Helpers.Lifecycle;

namespace Microsoft.RetireaBot.Tests.Helpers.Lifecycle
{
    public class LifecycleParserTest
    {
        private const string AksTable = """
            | Kubernetes version | Upstream release | AKS preview | AKS GA | End of life | Platform support |
            | ------------------ | ---------------- | ----------- | ------ | ----------- | ---------------- |
            | 1.32 | Dec 2024 | Feb 2025 | Apr 2025 | Mar 2026 | Until 1.36 GA |
            | 1.33 | Apr 2025 | May 2025 | Jun 2025 | Jun 2026 | Until 1.37 GA |
            """;

        [Fact]
        public void ParseAksVersionTable_ReturnsCorrectEntries()
        {
            var entries = LifecycleParser.ParseAksVersionTable(AksTable, "https://example.com");

            Assert.Equal(2, entries.Count);
            Assert.Equal("1.32", entries[0].Version);
            Assert.Equal(new DateTime(2026, 3, 31), entries[0].EndOfLife);
            Assert.Equal("1.33", entries[1].Version);
            Assert.Equal(new DateTime(2026, 6, 30), entries[1].EndOfLife);
        }

        private const string AksTableWithExactDate = """
    | Kubernetes version | Upstream release | AKS preview | AKS GA | End of life | Platform support |
    | ------------------ | ---------------- | ----------- | ------ | ----------- | ---------------- |
    | 1.30 | Apr 2024 | Jun 2024 | Jul 2024 | Aug 22, 2025 | Until 1.34 GA |
    """;

        [Fact]
        public void ParseAksVersionTable_HandlesExactDate()
        {
            var entries = LifecycleParser.ParseAksVersionTable(AksTableWithExactDate, "https://example.com");

            Assert.Single(entries);
            Assert.Equal(new DateTime(2025, 8, 22), entries[0].EndOfLife);
        }

        private const string PostGreSQLVersionTable = """
                | PostgreSQL Version | What's New | Azure Standard Support Start Date | Azure Standard Support End Date |
                | --- | --- | --- | --- |
                | [PostgreSQL 18](https://www.postgresql.org/about/press/) | [Release notes](https://www.postgresql.org/docs/18/release-18.html) | 25-Sep-2025| 14-Nov-2030 |
                | [PostgreSQL 17](https://www.postgresql.org/about/news/postgresql-17-released-2936/) | [Release notes](https://www.postgresql.org/docs/17/release-17.html) | 30-Sep-2024 | 8-Nov-2029 |
            """;

        [Fact]
        public void ParsePostgreSQLVersionTable_ReturnsCorrectEntries()
        {
            var entries = LifecycleParser.ParsePostgreSqlVersionTable(PostGreSQLVersionTable, "https://example.com");

            Assert.Equal(2, entries.Count);
            Assert.Equal("18", entries[0].Version);
            Assert.Equal(new DateTime(2030, 11, 14), entries[0].EndOfLife);
            Assert.Equal("17", entries[1].Version);
            Assert.Equal(new DateTime(2029, 11, 8), entries[1].EndOfLife);
        }

        private const string PostGreSQLTableWithPatchVersion = """
    | PostgreSQL Version | What's New | Azure Standard Support Start Date | Azure Standard Support End Date |
                | --- | --- | --- | --- |
                | [PostgreSQL 18.3](https://www.postgresql.org/about/press/) | [Release notes](https://www.postgresql.org/docs/18/release-18.html) | 25-Sep-2025| 14-Nov-2030 |
    """;

        [Fact]
        public void ParsePostgreSQLVersionTable_HandlesExactVersion()
        {
            var entries = LifecycleParser.ParsePostgreSqlVersionTable(PostGreSQLTableWithPatchVersion, "https://example.com");

            Assert.Single(entries);
            Assert.Equal("18.3", entries[0].Version);
        }
    }
}