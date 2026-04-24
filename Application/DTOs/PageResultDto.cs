using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class PagedResultDto<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public int Total { get; set; }
        public int PageNo { get; set; }
        public int PageSize { get; set; }
    }
}