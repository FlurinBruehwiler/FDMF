using FDMF.Core.DatabaseLayer;

namespace FDMF.Core;

public static class ModelExtensions
{
    extension(Model model)
    {
        public List<EntityDefinition> GetAllEntityDefinitions()
        {
            var result = new List<EntityDefinition>();

            AddFromModel(model);

            return result;

            void AddFromModel(Model mdl)
            {
                foreach (var ed in mdl.EntityDefinitions)
                {
                    result.Add(ed);
                }

                foreach (var importedModel in mdl.ImportedModels)
                {
                    AddFromModel(importedModel);
                }
            }
        }

        public List<FieldDefinition> GetAllFieldDefinitions()
        {
            var result = new List<FieldDefinition>();

            foreach (var ed in GetAllEntityDefinitions(model))
            {
                foreach (var fld in ed.FieldDefinitions)
                {
                    result.Add(fld);
                }
            }

            return result;
        }
    }
}