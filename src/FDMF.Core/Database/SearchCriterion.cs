namespace FDMF.Core.Database;

public interface ISearchCriterion;

public class SearchQuery : ISearchCriterion
{
    public required Guid TypId;
    public ISearchCriterion? SearchCriterion;
}

public class MultiCriterion : ISearchCriterion
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

public class IdCriterion : ISearchCriterion
{
    public Guid Guid;
}

public class AssocCriterion : ISearchCriterion
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

public class LongCriterion : ISearchCriterion
{
    public Guid FieldId;
    public long From;
    public long To;
}

public class DecimalCriterion : ISearchCriterion
{
    public Guid FieldId;
    public decimal From;
    public decimal To;
}

public class DateTimeCriterion : ISearchCriterion
{
    public Guid FieldId;
    public DateTime From;
    public DateTime To;
}

public class StringCriterion : ISearchCriterion
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

public class BooleanCriterion : ISearchCriterion
{
    public Guid FieldId;
    public bool Value;
}