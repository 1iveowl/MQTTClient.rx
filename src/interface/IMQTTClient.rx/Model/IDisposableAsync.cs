using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace IMQTTClientRx.Model
{
    public interface IDisposableAsync
    {
        Task DisposeAsync();
    }
}
