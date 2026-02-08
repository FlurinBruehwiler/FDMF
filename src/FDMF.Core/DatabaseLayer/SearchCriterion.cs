namespace FDMF.Core.DatabaseLayer;

public interface ISearchCriterion;

public sealed class SearchQuery : ISearchCriterion
{
    public required Guid TypId;
    public ISearchCriterion? SearchCriterion;
}

public sealed class MultiCriterion : ISearchCriterion
{
    public List<ISearchCriterion> Criterions = [];
    public MultiType Type;

    public enum MultiType
    {
        AND,
        OR,
        XOR,
    }
}

public sealed class IdCriterion : ISearchCriterion
{
    public Guid Guid;
}

public sealed class AssocCriterion : ISearchCriterion
{
    public Guid FieldId;

    public ISearchCriterion? SearchCriterion;

    public AssocCriterionType Type;

    public enum AssocCriterionType
    {
        Subquery = 0, //default
        Null,
        NotNull,
    }
}

public sealed class LongCriterion : ISearchCriterion
{
    public Guid FieldId;
    public long From;
    public long To;
}

public sealed class DecimalCriterion : ISearchCriterion
{
    public Guid FieldId;
    public decimal From;
    public decimal To;
}

public sealed class DateTimeCriterion : ISearchCriterion
{
    public Guid FieldId;
    public DateTime From;
    public DateTime To;
}

public sealed class StringCriterion : ISearchCriterion
{
    public Guid FieldId;
    public string Value = string.Empty;
    public MatchType Type;
    public float FuzzyCutoff = 0.5f;

    public enum MatchType
    {
        Substring = 0, //default
        Exact,
        Prefix,
        Postfix,
        Fuzzy
    }
}

public sealed class BooleanCriterion : ISearchCriterion
{
    public Guid FieldId;
    public bool Value;
}