using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DT_DataAcquisitionSystem.Domain.Entities
{
    public class ExcelImportTemplate
    {
        public int Id { get; set; }
        public string TemplateCode { get; set; }
        public string TemplateName { get; set; }
        public string ParserType { get; set; }
        public int TemplateVersion { get; set; }
        public string DefinitionJson { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }

        public ExcelTemplateDefinition ParseDefinition()
        {
            if (string.IsNullOrWhiteSpace(DefinitionJson))
            {
                return new ExcelTemplateDefinition();
            }

            return JsonConvert.DeserializeObject<ExcelTemplateDefinition>(DefinitionJson)
                   ?? new ExcelTemplateDefinition();
        }
    }

    public class ExcelTemplateDefinition
    {
        public ExcelTemplateIdentity Identity { get; set; }
        public List<ExcelTemplateMetadataField> Metadata { get; set; }
        public ExcelTemplateDataRegion DataRegion { get; set; }
        public List<ExcelTemplateColumn> Columns { get; set; }
        public List<string> SystemFields { get; set; }
        public Dictionary<string, object> FixedFields { get; set; }
        public Dictionary<string, string> RawFields { get; set; }
        public Dictionary<string, string> FilenameFields { get; set; }
    }

    public class ExcelTemplateIdentity
    {
        public string TemplateCode { get; set; }
        public string TemplateName { get; set; }
        public string TitleCell { get; set; }
        public string TitleContains { get; set; }
    }

    public class ExcelTemplateMetadataField
    {
        public string Field { get; set; }
        public string Source { get; set; }
        public string Parser { get; set; }
        public List<string> Options { get; set; }
    }

    public class ExcelTemplateDataRegion
    {
        public int StartRow { get; set; }
        public List<ExcelTemplateRule> StopRules { get; set; }
        public List<ExcelTemplateRule> SkipRules { get; set; }
        public List<string> CarryForwardColumns { get; set; }
    }

    public class ExcelTemplateRule
    {
        public string Type { get; set; }
        public List<string> Columns { get; set; }
        public List<string> Keywords { get; set; }
        public int Count { get; set; }
    }

    public class ExcelTemplateColumn
    {
        public string Field { get; set; }
        public string Column { get; set; }
        public string Type { get; set; }
        public bool CarryForward { get; set; }
        public string AccColumn { get; set; }
        public string RejColumn { get; set; }
    }
}
