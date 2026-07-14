using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class ImportTaskTemplateRequest
    {
        public int TemplateId { get; set; }
        public string TemplateCode { get; set; }
        public string ParserType { get; set; }
        public AcquisitionConfig Config { get; set; }
        public AcquisitionGroup Group { get; set; }
        public AcquisitionTask Task { get; set; }
    }

    public class ImportTaskTemplateResponse
    {
        public int ConfigId { get; set; }
        public int GroupId { get; set; }
        public int TaskId { get; set; }
    }
}
