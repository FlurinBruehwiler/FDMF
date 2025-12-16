using Shared;
using Shared.Database;
using Shared.Generated;

//we can store all fields objId+fieldIds that where changed in a dictionary within the transaction,
//when saving, we have a separate table where we store the "history" of all objects
//we could directly add the entries to hist db in a new transaction.
//what we want is to group often used objects together for better cache efficiency, and so that these pages can be unloaded from memory

try
{
    var env = Shared.Environment.Create([Folder.Fields.Name]);

    using (var tsx = new DbSession(env))
    {
        new Folder(tsx)
        {
            Name = "Hallo Flurin"
        };

        new Folder(tsx)
        {
            Name = "Brühwiler Flurin der Erste"
        };

        new Folder(tsx)
        {
            Name = "Flurin Flu"
        };

        new Folder(tsx)
        {
            Name = "Anna max Wynn"
        };

        new Folder(tsx)
        {
            Name = "Flurin"
        };

        tsx.Commit();
    }

    {
        using var t = new DbSession(env);

        foreach (var matchType in Enum.GetValues<SearchCriterion.StringCriterion.MatchType>())
        {
            Console.WriteLine(matchType + ":");

            foreach (var folder in Searcher.Search<Folder>(t, new SearchCriterion
                     {
                         Type = SearchCriterion.CriterionType.String,
                         String = new SearchCriterion.StringCriterion
                         {
                             FieldId = Folder.Fields.Name,
                             Value = "FLurIN",
                             Type = matchType
                         }
                     }))
            {
                Console.WriteLine(folder.Name);
            }

            Console.WriteLine();
        }
    }
}
catch (Exception e)
{
    Console.WriteLine(e);
    Console.WriteLine(e.StackTrace);
}