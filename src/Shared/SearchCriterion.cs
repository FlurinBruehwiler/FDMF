namespace Shared;

public struct SearchCriterion
{
    public CriterionType Type;
    public StringCriterion String;

    //todo overlap the following fields so that the struct is smaller
    public LongCriterion Long;
    public DecimalCriterion Decimal;
    public DateTimeCriterion DateTime;
    public AssocCriterion Assoc;

    public enum CriterionType
    {
        String,
        Long,
        Decimal,
        DateTime,
        Assoc
    }

    public struct AssocCriterion
    {
        public Guid FieldId;
        public Guid ObjId;
        public AssocCriterionType Type;

        public enum AssocCriterionType
        {
            MatchGuid = 0, //Default
            Null,
            NotNull,
        }
    }

    public struct LongCriterion
    {
        public Guid FieldId;
        public long From;
        public long To;
    }

    public struct DecimalCriterion
    {
        public Guid FieldId;
        public decimal From;
        public decimal To;
    }

    public struct DateTimeCriterion
    {
        public Guid FieldId;
        public DateTime From;
        public DateTime To;
    }

    public struct StringCriterion
    {
        public Guid FieldId;
        public string Value;
        public MatchType Type;

        public enum MatchType
        {
            Substring = 0, //default
            Exact,
            Prefix,
            Postfix,
        }
    }
}