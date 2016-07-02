using Commons.Requests;
using Commons.Responses;
using NativeBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RpcServer
{
    public class Service
    {
        private Bus bus;
        public Service(Bus bus)
        {
            this.bus = bus;
        }

        public void Start()
        {
            bus.Respond<SampleRequestDTO, SampleResponseDTO>(req =>
            {
                return new SampleResponseDTO()
                {
                    Message = string.Format("Hello {0}", req.Name)
                };
            });
        }

        public void Stop()
        {

        }
    }
}
