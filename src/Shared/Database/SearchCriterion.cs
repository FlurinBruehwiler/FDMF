namespace Shared.Database;

public interface ISearchCriterion;

public class SearchQuery : ISearchCriterion
{
    public required Guid TypId;
    public ISearchCriterion? SearchCriterion;
}

public class OrCriterion : ISearchCriterion
{
    public List<ISearchCriterion> OrCombinations = [];
}

public class AndCriterion : ISearchCriterion
{
    public List<ISearchCriterion> AndCombinations = [];
}

public class IdCriterion : ISearchCriterion
{
    public Guid Guid;
}

public class AssocCriterion : ISearchCriterion
{
    public Guid FieldId;
    public Guid ObjId;

    public ISearchCriterion? SearchCriterion;

    public AssocCriterionType Type;

    public enum AssocCriterionType
    {
        MatchGuid = 0, //Default
        Null,
        NotNull,
        Subquery,
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