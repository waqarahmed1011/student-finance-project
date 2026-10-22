using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Workbench.Core.Reporting;

public sealed record SqlGuardResult(bool IsAllowed, string? Reason)
{
    public static SqlGuardResult Allowed() => new(true, null);
    public static SqlGuardResult Rejected(string reason) => new(false, reason);
}

/// <summary>
/// Checks AI-generated (or hand-edited) SQL before it runs. Uses Microsoft's real T-SQL parser
/// instead of keyword matching, so things like comments or odd casing can't sneak past it.
///
/// Rules: exactly one SELECT statement, no SELECT INTO, and every table it reads must be a view
/// in the rpt schema. This is the second line of defense. The first is that the query runs under
/// a database login that only has SELECT on rpt.
/// </summary>
public static class ReportSqlGuard
{
    public const string AllowedSchema = "rpt";

    public static SqlGuardResult Validate(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return SqlGuardResult.Rejected("No SQL to run.");

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        using (var reader = new StringReader(sql))
        {
            fragment = parser.Parse(reader, out var errors);
            if (errors.Count > 0)
                return SqlGuardResult.Rejected($"SQL doesn't parse: {errors[0].Message} (line {errors[0].Line}).");
        }

        var script = (TSqlScript)fragment;
        var statements = script.Batches.SelectMany(b => b.Statements).ToList();

        if (statements.Count != 1)
            return SqlGuardResult.Rejected("Only a single SELECT statement is allowed.");

        if (statements[0] is not SelectStatement select)
            return SqlGuardResult.Rejected($"Only SELECT is allowed, this is a {statements[0].GetType().Name.Replace("Statement", "")} statement.");

        if (select.Into is not null)
            return SqlGuardResult.Rejected("SELECT INTO isn't allowed.");

        var cteNames = select.WithCtesAndXmlNamespaces?.CommonTableExpressions
            .Select(c => c.ExpressionName.Value) ?? [];

        var visitor = new TableReferenceVisitor(cteNames);
        select.Accept(visitor);
        return visitor.Problem is null ? SqlGuardResult.Allowed() : SqlGuardResult.Rejected(visitor.Problem);
    }

    private sealed class TableReferenceVisitor : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _cteNames;

        public TableReferenceVisitor(IEnumerable<string> cteNames)
        {
            _cteNames = new HashSet<string>(cteNames, StringComparer.OrdinalIgnoreCase);
        }

        public string? Problem { get; private set; }

        public override void Visit(NamedTableReference node)
        {
            var name = node.SchemaObject;
            var schema = name.SchemaIdentifier?.Value;

            if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
                Fail($"Cross-database references aren't allowed ({Describe(name)}).");
            else if (schema is null && _cteNames.Contains(name.BaseIdentifier.Value))
                return;
            else if (!string.Equals(schema, AllowedSchema, StringComparison.OrdinalIgnoreCase))
                Fail($"{Describe(name)} isn't a reporting view. Only {AllowedSchema}.* views can be queried.");
        }

        public override void Visit(OpenRowsetTableReference node) => Fail("OPENROWSET isn't allowed.");
        public override void Visit(BulkOpenRowset node) => Fail("OPENROWSET(BULK) isn't allowed.");
        public override void Visit(OpenQueryTableReference node) => Fail("OPENQUERY isn't allowed.");
        public override void Visit(AdHocTableReference node) => Fail("OPENDATASOURCE isn't allowed.");
        public override void Visit(SchemaObjectFunctionTableReference node) => Fail("Table-valued functions aren't allowed.");

        private void Fail(string reason) => Problem ??= reason;

        private static string Describe(SchemaObjectName name) =>
            string.Join(".", name.Identifiers.Select(i => i.Value));
    }
}
