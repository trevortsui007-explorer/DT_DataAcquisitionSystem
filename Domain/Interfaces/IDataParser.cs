using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public interface IDataParser
    {
        List<T> Parse<T>(Stream stream, object options = null) where T : class, new();

        Task<List<T>> ParseAsync<T>(Stream stream, object options = null, CancellationToken ct = default) where T : class, new();
    }
}