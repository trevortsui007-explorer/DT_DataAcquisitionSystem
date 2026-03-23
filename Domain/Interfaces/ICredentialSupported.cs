using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public interface ICredentialSupported
    {
        void SetCredentials(string username, string password);
    }
}